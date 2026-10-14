using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Externos;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Repositorios;
using CONADIS.Integracion.Negocio.Resiliencia;
using CONADIS.Integracion.Negocio.Seguridad;
using log4net;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Solicitudes del Portal Ciudadano (CU-01, RF-WEB-01 y RF-WEB-03). Antes de llegar al Core:
///   - valida la identidad del titular y del tutor con la JCE (los datos oficiales prevalecen);
///   - valida el diagnóstico de origen con el MSP;
///   - valida el RNC con la DGII (certificación de accesibilidad).
/// El Core recibe la solicitud ya enriquecida con esos resultados.
/// </summary>
public sealed class ServicioPortal(PuenteCore core, ServicioIdentidad identidad, ServicioDiagnostico diagnostico, ServicioDgii dgii)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioPortal));

    /// <summary>Registra una solicitud de certificación, renovación o duplicado (cuerpo = NuevaSolicitud del Core).</summary>
    public async Task<RespuestaCore> RegistrarSolicitudAsync(JsonObject solicitud, ContextoLlamada contexto, CancellationToken ct = default)
    {
        var cedula = Texto(solicitud, "cedula");
        if (cedula is not null)
        {
            var id = await identidad.ValidarAsync(cedula, contexto with { Proposito = "Registro de solicitud (titular)" }, ct);
            if (id.Estado == EstadosIdentidad.Invalida)
                throw new ReglaNegocioException(0, $"La JCE no valida la cédula del solicitante: {id.Motivo}");

            solicitud["estadoIdentidad"] = id.Estado;
            if (id.Estado == EstadosIdentidad.Validada)
            {
                // RF-INT-01 paso 3: los datos de la JCE prevalecen sobre los declarados
                solicitud["nombres"] = id.Nombres;
                solicitud["apellidos"] = id.Apellidos;
                solicitud["fechaNacimiento"] = id.FechaNacimiento?.ToString("yyyy-MM-dd");
                solicitud["sexo"] = id.Sexo;
            }
        }
        else
        {
            // Menor sin cédula: el analista verifica el acta de nacimiento
            solicitud["estadoIdentidad"] = EstadosIdentidad.Pendiente;
        }

        var cedulaTutor = Texto(solicitud, "cedulaTutor");
        if (cedulaTutor is not null)
        {
            var tutor = await identidad.ValidarAsync(cedulaTutor, contexto with { Proposito = "Registro de solicitud (tutor)" }, ct);
            if (tutor.Estado == EstadosIdentidad.Invalida)
                throw new ReglaNegocioException(0, $"La JCE no valida la cédula del tutor: {tutor.Motivo}");
        }

        solicitud["estadoDiagnostico"] = await diagnostico.ValidarAsync(new ConsultaDiagnostico(
            cedula ?? string.Empty, Texto(solicitud, "establecimiento"), Texto(solicitud, "numeroCertificadoMedico"),
            Texto(solicitud, "codigoCie10")), contexto with { Proposito = "Validación del diagnóstico de origen" }, ct);

        solicitud["canal"] = "Web";

        var respuesta = await core.PostAsync("api/v1/solicitudes", solicitud.ToJsonString(), contexto, "RegistrarSolicitud", ct: ct);
        if (respuesta.EsExito) Log.Info($"Solicitud web registrada en el Core ({respuesta.Estado}).");
        return respuesta;
    }

    /// <summary>Ayuda técnica o certificación de accesibilidad (cuerpo = NuevaSolicitudServicio del Core).</summary>
    public async Task<RespuestaCore> RegistrarServicioAsync(JsonObject solicitud, ContextoLlamada contexto, CancellationToken ct = default)
    {
        if (Texto(solicitud, "tipo") == "CERT_ACCESIBILIDAD")
        {
            var rnc = Texto(solicitud, "documento") ?? string.Empty;
            var r = await dgii.ConsultarRncAsync(rnc, contexto with { Proposito = "Certificación de accesibilidad" }, ct);
            if (!r.Existe)
                throw new ReglaNegocioException(0, "El RNC no existe en la DGII. Verifique el número o regularice su registro ante la DGII.");
            if (!r.Activo)
                throw new ReglaNegocioException(0, "El RNC está inactivo en la DGII. Regularice su situación ante la DGII antes de solicitar la certificación.");
        }

        return await core.PostAsync("api/v1/servicios", solicitud.ToJsonString(), contexto, "RegistrarServicio", ct: ct);
    }

    private static string? Texto(JsonObject o, string propiedad)
    {
        // Las propiedades llegan en camelCase, pero se aceptan también en PascalCase
        var nodo = o[propiedad] ?? o[char.ToUpperInvariant(propiedad[0]) + propiedad[1..]];
        var valor = nodo?.GetValueKind() == JsonValueKind.String ? nodo.GetValue<string>() : null;
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }
}

/// <summary>
/// Verificación de la condición de discapacidad para instituciones con convenio (RF-INT-03, NORTIC A4):
///   - exige el ámbito discapacidad.verificar y convenio vigente (si no, 403 y registro del intento);
///   - responde solo los atributos mínimos autorizados por el convenio;
///   - registra cada consulta (institución, propósito, fecha, resultado) para la rendición de cuentas.
/// </summary>
public sealed class ServicioVerificacion(PuenteCore core, IRepositorioBitacora bitacora, HuellaPrivada huella)
{
    public async Task<VerificacionInstitucional> VerificarAsync(ClienteAutenticado institucion, string cedula, string? proposito,
        ContextoLlamada contexto, CancellationToken ct = default)
    {
        var referencia = huella.Cedula(cedula);
        var ctx = contexto with { Cliente = institucion.Nombre, Proposito = proposito };

        if (!institucion.TieneAmbito(Ambitos.Verificar) || !institucion.ConvenioVigente)
        {
            await bitacora.RegistrarAsync(new EntradaBitacora(ServiciosExternos.Verificacion, "VerificarCondicion", "Entrante", false,
                "Denegado", ctx, referencia, "403", Detalle: institucion.ConvenioVigente ? "Sin el ámbito discapacidad.verificar." : "Convenio vencido."), ct);
            throw new AccesoDenegadoException(institucion.ConvenioVigente
                ? "La institución no tiene autorizado el ámbito discapacidad.verificar."
                : $"El convenio de la institución venció el {institucion.ConvenioHasta:dd/MM/yyyy}.");
        }

        if (string.IsNullOrWhiteSpace(proposito) || proposito.Trim().Length < 5)
            throw new ReglaNegocioException(0, "Indique el propósito de la consulta (mínimo 5 caracteres).");
        if (!ValidadorCedula.EsValida(cedula))
            throw new ReglaNegocioException(0, "La cédula consultada no es válida.");

        var respuesta = await core.GetAsync($"api/v1/verificacion/institucional/{PuenteCore.Segmento(ValidadorCedula.Normalizar(cedula))}",
            ctx, "VerificarInstitucional", ct);
        var v = ClienteCore.LeerOFallar<VerificacionInstitucional>(respuesta, "la verificación institucional");

        // Tipo y grado solo si el convenio lo autoriza
        if (!institucion.VerTipoGrado) v = v with { TipoDiscapacidad = null, GradoDiscapacidad = null };

        await bitacora.RegistrarAsync(new EntradaBitacora(ServiciosExternos.Verificacion, "VerificarCondicion", "Entrante", true,
            v.TieneCertificacionVigente ? "Vigente" : "NoVigente", ctx, referencia, "200"), ct);
        return v;
    }
}

/// <summary>
/// Pagos de la Caja y del portal. Patrón outbox transaccional (RIC-07): el pago se guarda en la base de
/// Integración junto con su mensaje al Core, se responde "Recibido" al canal y el servicio de Windows lo
/// entrega al Core (aunque el Core esté caído en ese momento). Reenviar el mismo TransaccionId no lo duplica.
/// </summary>
public sealed class ServicioPagos(RepositorioPagos pagos, PuenteCore core, IPasarelaPagos pasarela, EjecutorResiliente ejecutor)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioPagos));
    private static readonly string[] Medios = ["Efectivo", "Tarjeta", "Transferencia", "EnLinea"];

    public async Task<ResultadoRecepcionPago> RecibirAsync(PagoEntrante p, ClienteAutenticado cliente, ContextoLlamada contexto,
        CancellationToken ct = default)
    {
        var origenPermitido = cliente.TieneAmbito(Ambitos.Caja) ? "CAJA" : cliente.TieneAmbito(Ambitos.Portal) ? "WEB" : null;
        if (origenPermitido is null) throw new AccesoDenegadoException("Este sistema no puede registrar pagos.");
        if (p.Origen != origenPermitido) throw new ReglaNegocioException(0, $"El origen del pago debe ser {origenPermitido}.");
        if (p.TransaccionId == Guid.Empty) throw new ReglaNegocioException(0, "Falta el identificador de la transacción.");
        if (string.IsNullOrWhiteSpace(p.NumeroComprobante) || p.NumeroComprobante.Length > 20)
            throw new ReglaNegocioException(0, "El número de comprobante es obligatorio (máximo 20 caracteres).");
        if (p.Total < 0 || p.Cantidad < 1) throw new ReglaNegocioException(0, "Monto o cantidad no válidos.");
        if (!Medios.Contains(p.MedioPago)) throw new ReglaNegocioException(0, "Medio de pago no válido.");

        var carga = JsonSerializer.Serialize(p, ClienteCore.Json);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(carga)));

        var resultado = await pagos.RecibirAsync(p, cliente.ClienteId, hash, carga, contexto.CorrelacionId, ct);
        Log.Info(resultado.EsNuevo
            ? $"Pago {p.TransaccionId} recibido de {cliente.Nombre} (RD${p.Total:N2}); encolado para el Core."
            : $"Pago {p.TransaccionId} reenviado por {cliente.Nombre}: ya estaba recibido ({resultado.Pago.Estado}).");
        return resultado;
    }

    public Task<PagoRecibido?> ObtenerAsync(Guid transaccionId, ClienteAutenticado cliente, CancellationToken ct = default)
        => pagos.ObtenerAsync(transaccionId, cliente.ClienteId, ct);

    /// <summary>
    /// Pago en línea desde el portal: consulta la orden en el Core (monto oficial), cobra con la pasarela
    /// y, si se aprueba, registra el pago por la cola. La pasarela NO se reintenta (evita cobrar dos veces).
    /// </summary>
    public async Task<(ResultadoRecepcionPago Pago, string CodigoAutorizacion)> PagarEnLineaAsync(string numeroOrden, string tokenTarjeta,
        ClienteAutenticado cliente, ContextoLlamada contexto, CancellationToken ct = default)
    {
        if (!cliente.TieneAmbito(Ambitos.Portal)) throw new AccesoDenegadoException("Solo el portal puede cobrar en línea.");
        if (string.IsNullOrWhiteSpace(tokenTarjeta)) throw new ReglaNegocioException(0, "Falta el token de la tarjeta.");

        var respuesta = await core.GetAsync($"api/v1/ordenes/{PuenteCore.Segmento(numeroOrden)}", contexto, "ObtenerOrden", ct);
        if (respuesta.Estado == 404) throw new ReglaNegocioException(0, "La orden de cobro no existe.");
        var orden = ClienteCore.LeerOFallar<OrdenCore>(respuesta, "la consulta de la orden");
        if (orden.Estado != "Pendiente") throw new ReglaNegocioException(0, $"La orden está {orden.Estado}; no se puede pagar.");

        var cobro = await ejecutor.EjecutarAsync(ServiciosExternos.Pasarela, new OpcionesLlamada("Cobrar", Reintentar: false), contexto,
            t => pasarela.CobrarAsync(new CobroEnLinea(orden.NumeroOrden, orden.Total, tokenTarjeta, orden.Documento, orden.NombrePagador), t),
            r => r.Aprobado ? "Aprobado" : "Rechazado", ct: ct);

        if (!cobro.Exito)
            throw new ServicioNoDisponibleException(ServiciosExternos.Pasarela, "La pasarela de pagos no está disponible. No se realizó ningún cargo.");
        if (!cobro.Valor!.Aprobado)
            throw new ReglaNegocioException(0, $"El pago fue rechazado: {cobro.Valor.Motivo}");

        var pago = new PagoEntrante(Guid.NewGuid(), "WEB", cobro.Valor.Referencia!, orden.TasaCodigo, orden.Cantidad, orden.Total,
            "EnLinea", orden.Documento, orden.NombrePagador, orden.NumeroOrden, null, null, null, DateTimeOffset.Now);
        return (await RecibirAsync(pago, cliente, contexto, ct), cobro.Valor.CodigoAutorizacion!);
    }
}
