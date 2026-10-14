using System.Text.Json;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Externos;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Repositorios;
using CONADIS.Integracion.Negocio.Resiliencia;
using CONADIS.Integracion.Negocio.Seguridad;
using log4net;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// RF-INT-01: validación de identidad con la JCE.
///   1. Caché local (30 días) para no repetir consultas.
///   2. JCE con tiempo de espera, reintentos y cortacircuito.
///   3. Los datos de la JCE prevalecen sobre los declarados.
///   4. Si la JCE no responde: resultado "Pendiente" (el servicio de Windows reintenta después).
/// </summary>
public sealed class ServicioIdentidad(IServicioJce jce, EjecutorResiliente ejecutor, IRepositorioCache cache, HuellaPrivada huella)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioIdentidad));
    private const string Operacion = "ValidarCedula";

    public async Task<ResultadoIdentidad> ValidarAsync(string cedula, ContextoLlamada contexto, CancellationToken ct = default)
    {
        var c = ValidadorCedula.Normalizar(cedula);
        if (!ValidadorCedula.EsValida(c))
            return new ResultadoIdentidad(EstadosIdentidad.Invalida, null, null, null, null, "La cédula no tiene un formato válido.", false);

        var referencia = huella.Cedula(c);

        var guardado = await LeerCacheAsync(referencia, ct);
        if (guardado is not null)
        {
            await ejecutor.RegistrarDesdeCacheAsync(ServiciosExternos.Jce, Operacion, guardado.Estado, contexto, referencia, ct);
            return guardado;
        }

        var llamada = await ejecutor.EjecutarAsync(ServiciosExternos.Jce, new OpcionesLlamada(Operacion, referencia), contexto,
            t => jce.ConsultarCedulaAsync(c, t), r => r.Valida ? "Valida" : "Invalida", ct: ct);

        if (!llamada.Exito)
            return ResultadoIdentidad.Pendiente($"La JCE no está disponible ({llamada.Resultado}). Se reintentará más tarde.");

        var r = llamada.Valor!;
        var resultado = r.Valida
            ? new ResultadoIdentidad(EstadosIdentidad.Validada, r.Nombres, r.Apellidos, r.FechaNacimiento, r.Sexo, null, false)
            : new ResultadoIdentidad(EstadosIdentidad.Invalida, null, null, null, null, r.Motivo, false);

        await GuardarCacheAsync(referencia, resultado, ct);
        return resultado;
    }

    private async Task<ResultadoIdentidad?> LeerCacheAsync(string referencia, CancellationToken ct)
    {
        try
        {
            var e = await cache.ObtenerAsync(ServiciosExternos.Jce, referencia, ct);
            if (e is null) return null;
            var d = e.Datos is null ? null : JsonSerializer.Deserialize<DatosCache>(e.Datos, ClienteCore.Json);
            return new ResultadoIdentidad(e.Resultado, d?.Nombres, d?.Apellidos, d?.FechaNacimiento, d?.Sexo, d?.Motivo, true);
        }
        catch (Exception ex) when (ex is BaseDatosNoDisponibleException or ErrorBaseDatosException or JsonException)
        {
            Log.Warn("No se pudo leer la caché de la JCE; se consulta directamente.", ex);
            return null;
        }
    }

    private async Task GuardarCacheAsync(string referencia, ResultadoIdentidad r, CancellationToken ct)
    {
        try
        {
            var datos = JsonSerializer.Serialize(new DatosCache(r.Nombres, r.Apellidos, r.FechaNacimiento, r.Sexo, r.Motivo), ClienteCore.Json);
            await cache.GuardarAsync(ServiciosExternos.Jce, referencia, r.Estado, datos, ct: ct);
        }
        catch (Exception ex) when (ex is BaseDatosNoDisponibleException or ErrorBaseDatosException)
        {
            Log.Warn("No se pudo guardar la validación de la JCE en la caché.", ex);
        }
    }

    private sealed record DatosCache(string? Nombres, string? Apellidos, DateOnly? FechaNacimiento, int? Sexo, string? Motivo);
}

/// <summary>
/// RF-INT-02: validación del diagnóstico de origen con el MSP/SNS.
/// Si el MSP no responde (o no hay datos del certificado) el resultado es "ValidacionManual":
/// la solicitud NO se rechaza; el evaluador médico revisa la evidencia.
/// </summary>
public sealed class ServicioDiagnostico(IServicioMsp msp, EjecutorResiliente ejecutor, HuellaPrivada huella)
{
    public async Task<string> ValidarAsync(ConsultaDiagnostico consulta, ContextoLlamada contexto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(consulta.NumeroCertificado) && string.IsNullOrWhiteSpace(consulta.CodigoCie10))
            return EstadosDiagnostico.ValidacionManual;

        var llamada = await ejecutor.EjecutarAsync(ServiciosExternos.Msp, new OpcionesLlamada("ValidarDiagnostico", huella.Cedula(consulta.Cedula)),
            contexto, t => msp.ValidarDiagnosticoAsync(consulta, t), r => r, ct: ct);

        return llamada.Exito ? llamada.Valor! : EstadosDiagnostico.ValidacionManual;
    }
}

/// <summary>DGII: RNC para la certificación de accesibilidad y envío de e-CF de la Caja.</summary>
public sealed class ServicioDgii(IServicioDgii dgii, EjecutorResiliente ejecutor, HuellaPrivada huella)
{
    public async Task<RespuestaRnc> ConsultarRncAsync(string rnc, ContextoLlamada contexto, CancellationToken ct = default)
    {
        var llamada = await ejecutor.EjecutarAsync(ServiciosExternos.Dgii, new OpcionesLlamada("ConsultarRnc", huella.Calcular("RNC", rnc)),
            contexto, t => dgii.ConsultarRncAsync(rnc, t), r => !r.Existe ? "NoExiste" : r.Activo ? "Activo" : "Inactivo", ct: ct);

        return llamada.Exito ? llamada.Valor!
            : throw new ServicioNoDisponibleException(ServiciosExternos.Dgii, "La DGII no está disponible para validar el RNC. Intente más tarde.");
    }

    /// <summary>
    /// Envía un e-CF. Si la DGII no responde lanza <see cref="ServicioNoDisponibleException"/>:
    /// la Caja guarda el comprobante "en contingencia" y lo reenvía después (no se pierde la venta).
    /// Se reintenta porque la DGII identifica el comprobante por su e-NCF (no se duplica).
    /// </summary>
    public async Task<RespuestaEcf> EnviarComprobanteAsync(ComprobanteFiscal comprobante, ContextoLlamada contexto, CancellationToken ct = default)
    {
        var llamada = await ejecutor.EjecutarAsync(ServiciosExternos.Dgii, new OpcionesLlamada("EnviarEcf", huella.Calcular("ECF", comprobante.Encf)),
            contexto, t => dgii.EnviarComprobanteAsync(comprobante, t), r => r.Estado, ct: ct);

        return llamada.Exito ? llamada.Valor!
            : throw new ServicioNoDisponibleException(ServiciosExternos.Dgii, "La DGII no está disponible. El comprobante queda en contingencia.");
    }
}

/// <summary>
/// RF-INT-03: firma digital de documentos oficiales (lo pide el Core al emitir un carnet).
/// Bloquea la firma si el certificado venció y alerta 30 días antes del vencimiento.
/// </summary>
public sealed class ServicioFirmaDigital(IProveedorFirma proveedor, EjecutorResiliente ejecutor, IRepositorioAlertas alertas,
    IParametros parametros)
{
    public async Task<FirmaDocumento> FirmarAsync(string contenido, ContextoLlamada contexto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contenido) || contenido.Length > 10_000)
            throw new ReglaNegocioException(0, "El contenido a firmar debe tener entre 1 y 10 000 caracteres.");

        if (DateOnly.FromDateTime(DateTime.Today) > proveedor.VencimientoCertificado)
        {
            await alertas.RegistrarAsync(Severidades.Alta, ServiciosExternos.Firma,
                $"El certificado de firma venció el {proveedor.VencimientoCertificado:dd/MM/yyyy}. La emisión de carnets está bloqueada.", ct);
            throw new ServicioNoDisponibleException(ServiciosExternos.Firma, "El certificado de firma está vencido; no se puede firmar.");
        }

        var llamada = await ejecutor.EjecutarAsync(ServiciosExternos.Firma, new OpcionesLlamada("Firmar"), contexto,
            t => proveedor.FirmarAsync(contenido, t), ct: ct);

        return llamada.Exito ? llamada.Valor!
            : throw new ServicioNoDisponibleException(ServiciosExternos.Firma, "El servicio de firma digital no está disponible.");
    }

    public bool Verificar(string contenido, string firma) => proveedor.Verificar(contenido, firma);

    /// <summary>Alerta si el certificado vence pronto. Devuelve los días que faltan.</summary>
    public async Task<int> RevisarVencimientoAsync(CancellationToken ct = default)
    {
        var dias = proveedor.VencimientoCertificado.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
        var aviso = await parametros.EnteroAsync("FirmaDiasAvisoVencimiento", 30, ct);
        if (dias <= aviso)
            await alertas.RegistrarAsync(dias < 0 ? Severidades.Alta : Severidades.Media, ServiciosExternos.Firma,
                dias < 0 ? "El certificado de firma está vencido."
                         : $"El certificado de firma vence en {dias} día(s) ({proveedor.VencimientoCertificado:dd/MM/yyyy}). Renuévelo.", ct);
        return dias;
    }
}
