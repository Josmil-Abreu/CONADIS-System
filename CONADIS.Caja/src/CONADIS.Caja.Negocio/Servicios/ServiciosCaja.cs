using System.Data;
using System.Text.Json;
using CONADIS.Caja.Negocio.Datos;
using CONADIS.Caja.Negocio.Externos;
using CONADIS.Caja.Negocio.Modelos;
using CONADIS.Caja.Negocio.Reportes;
using CONADIS.Caja.Negocio.Seguridad;
using Microsoft.Extensions.Logging;

namespace CONADIS.Caja.Negocio.Servicios;

/// <summary>Inicio de sesión de cajeros y supervisores (bloqueo tras varios intentos fallidos).</summary>
public sealed class ServicioAutenticacionCaja(EjecutorSp db, ILogger<ServicioAutenticacionCaja> log)
{
    private const string MensajeGenerico = "Usuario o contraseña incorrectos.";

    public async Task<ResultadoLoginCaja> IniciarSesionAsync(string? usuario, string? contrasena, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrEmpty(contrasena))
            return ResultadoLoginCaja.Fallido("Escriba usuario y contraseña.");

        var fila = (await db.ConsultarAsync("seg.ppUsuario_ObtenerParaLogin", [Parametro.Texto("@Username", usuario.Trim(), 50)], ct))
            .Rows.Cast<DataRow>().FirstOrDefault();
        if (fila is null)
        {
            HasherContrasena.SimularVerificacion(contrasena);
            log.LogWarning("Intento de acceso a la caja con usuario inexistente");
            return ResultadoLoginCaja.Fallido(MensajeGenerico);
        }

        var id = fila.Entero("UsuarioId");
        if (!fila.Booleano("Activo")) return ResultadoLoginCaja.Fallido("El usuario está inactivo.");
        var bloqueo = fila.FechaHoraONula("BloqueadoHasta");
        if (bloqueo > DateTimeOffset.Now)
            return ResultadoLoginCaja.Fallido($"Usuario bloqueado hasta las {bloqueo:HH:mm} por intentos fallidos.");

        if (!HasherContrasena.Verificar(contrasena, fila.TextoONulo("PasswordHash")))
        {
            await db.EjecutarAsync("seg.ppUsuario_RegistrarIntentoFallido", [Parametro.Entero("@UsuarioId", id)], ct);
            log.LogWarning("Contraseña incorrecta para el usuario de caja {UsuarioId}", id);
            return ResultadoLoginCaja.Fallido(MensajeGenerico);
        }

        await db.EjecutarAsync("seg.ppUsuario_RegistrarAccesoExitoso", [Parametro.Entero("@UsuarioId", id)], ct);
        var u = new UsuarioCaja(id, fila.Texto("Username"), fila.Texto("NombreCompleto"), fila.Texto("Rol"));
        await Eventos.RegistrarAsync(db, u.Username, "Login", null, ct);
        log.LogInformation("Usuario de caja {Usuario} inició sesión ({Rol})", u.Username, u.Rol);
        return ResultadoLoginCaja.Correcto(u);
    }

    /// <summary>Sesión a partir de un pase de CONADIS.Inicio (la contraseña ya se verificó allí). Null si no es válido.</summary>
    public async Task<UsuarioCaja?> IniciarConPaseAsync(string? pase, string claveTraspaso, CancellationToken ct = default)
    {
        var usuario = PaseInicio.Validar(pase, "Caja", claveTraspaso);
        if (usuario is null) return null;

        var fila = (await db.ConsultarAsync("seg.ppUsuario_ObtenerParaLogin", [Parametro.Texto("@Username", usuario, 50)], ct))
            .Rows.Cast<DataRow>().FirstOrDefault();
        if (fila is null || !fila.Booleano("Activo") || fila.FechaHoraONula("BloqueadoHasta") > DateTimeOffset.Now) return null;

        var u = new UsuarioCaja(fila.Entero("UsuarioId"), fila.Texto("Username"), fila.Texto("NombreCompleto"), fila.Texto("Rol"));
        await db.EjecutarAsync("seg.ppUsuario_RegistrarAccesoExitoso", [Parametro.Entero("@UsuarioId", u.UsuarioId)], ct);
        await Eventos.RegistrarAsync(db, u.Username, "Login", "Con pase de CONADIS.Inicio", ct);
        return u;
    }

    /// <summary>Autorización de un supervisor para una acción puntual (nota de crédito, cierre).</summary>
    public async Task<UsuarioCaja> AutorizarSupervisorAsync(string? usuario, string? contrasena, CancellationToken ct = default)
    {
        var r = await IniciarSesionAsync(usuario, contrasena, ct);
        if (!r.Exito || r.Usuario is null) throw new ReglaNegocioException(58011, r.Mensaje ?? MensajeGenerico);
        if (!r.Usuario.EsSupervisor) throw new ReglaNegocioException(58011, "La acción debe autorizarla un supervisor de caja.");
        return r.Usuario;
    }
}

internal static class Eventos
{
    public static Task RegistrarAsync(EjecutorSp db, string usuario, string accion, string? detalle, CancellationToken ct)
        => db.EjecutarAsync("caja.ppEvento_Registrar",
            [Parametro.Texto("@Usuario", usuario, 50), Parametro.Texto("@Accion", accion, 50), Parametro.Texto("@Detalle", detalle, 500)], ct);
}

/// <summary>Apertura, arqueo ciego y cierre de caja (RF-CAJ-03).</summary>
public sealed class ServicioSesiones(EjecutorSp db, ILogger<ServicioSesiones> log)
{
    public Task<List<CajaFisica>> CajasAsync(CancellationToken ct = default)
        => db.ConsultarListaAsync("caja.ppCaja_Listar", f => new CajaFisica(f.Entero("CajaId"), f.Texto("Codigo"), f.Texto("Sucursal")), ct: ct);

    public Task<SesionCaja?> ObtenerAbiertaAsync(int usuarioId, CancellationToken ct = default)
        => db.ConsultarUnoAsync("caja.ppSesion_ObtenerAbierta", f => new SesionCaja(f.Entero("SesionId"), f.Entero("CajaId"),
            f.Texto("CajaCodigo"), f.Texto("Sucursal"), f.Decimal("FondoInicial"), f.Texto("Estado"), f.FechaHora("FechaApertura")),
            [Parametro.Entero("@UsuarioId", usuarioId)], ct);

    public async Task<SesionCaja> AbrirAsync(int cajaId, UsuarioCaja cajero, decimal fondoInicial, CancellationToken ct = default)
    {
        if (fondoInicial < 0) throw new ReglaNegocioException(58001, "El fondo inicial no puede ser negativo.");
        await db.ConsultarAsync("caja.ppSesion_Abrir",
            [Parametro.Entero("@CajaId", cajaId), Parametro.Entero("@UsuarioId", cajero.UsuarioId), Parametro.Decimal("@FondoInicial", fondoInicial)], ct);
        log.LogInformation("Caja {CajaId} abierta por {Usuario} con fondo {Fondo}", cajaId, cajero.Username, fondoInicial);
        return await ObtenerAbiertaAsync(cajero.UsuarioId, ct)
               ?? throw new ErrorBaseDatosException("La sesión se abrió pero no se pudo leer.");
    }

    /// <summary>Arqueo ciego: el cajero declara lo contado sin ver lo esperado.</summary>
    public async Task<ResultadoArqueo> ArquearAsync(int sesionId, UsuarioCaja cajero, decimal efectivo, decimal tarjeta, decimal transferencia,
        CancellationToken ct = default)
    {
        if (efectivo < 0 || tarjeta < 0 || transferencia < 0) throw new ReglaNegocioException(58014, "Los montos declarados no pueden ser negativos.");
        var f = (await db.ConsultarAsync("caja.ppSesion_Arquear",
        [
            Parametro.Entero("@SesionId", sesionId), Parametro.Entero("@UsuarioId", cajero.UsuarioId),
            Parametro.Decimal("@EfectivoDeclarado", efectivo), Parametro.Decimal("@TarjetaDeclarado", tarjeta),
            Parametro.Decimal("@TransferenciaDeclarado", transferencia)
        ], ct)).Rows[0];
        return new ResultadoArqueo(f.Decimal("EfectivoDeclarado"), f.Decimal("TarjetaDeclarado"), f.Decimal("TransferenciaDeclarado"),
            f.Decimal("EfectivoEsperado"), f.Decimal("TarjetaEsperado"), f.Decimal("TransferenciaEsperado"),
            f.Decimal("DiferenciaEfectivo"), f.Decimal("DiferenciaTarjeta"), f.Decimal("DiferenciaTransferencia"));
    }

    public async Task<ResultadoCierre> CerrarAsync(int sesionId, UsuarioCaja supervisor, string? justificacion, string? observaciones,
        CancellationToken ct = default)
    {
        if (!supervisor.EsSupervisor) throw new ReglaNegocioException(58011, "El cierre de caja debe aprobarlo un supervisor de caja.");
        var f = (await db.ConsultarAsync("caja.ppSesion_Cerrar",
        [
            Parametro.Entero("@SesionId", sesionId), Parametro.Entero("@SupervisorId", supervisor.UsuarioId),
            Parametro.Texto("@Justificacion", justificacion, 500), Parametro.Texto("@Observaciones", observaciones, 500)
        ], ct)).Rows[0];
        log.LogInformation("Sesión {SesionId} cerrada por {Supervisor}", sesionId, supervisor.Username);
        return new ResultadoCierre(f.Decimal("DiferenciaTotal"), f.Entero("EcfEnContingencia"));
    }

    public async Task CerrarForzadoAsync(int sesionId, UsuarioCaja supervisor, string motivo, CancellationToken ct = default)
    {
        await db.EjecutarAsync("caja.ppSesion_CerrarForzado",
            [Parametro.Entero("@SesionId", sesionId), Parametro.Entero("@SupervisorId", supervisor.UsuarioId), Parametro.Texto("@Motivo", motivo, 500)], ct);
        log.LogWarning("Cierre forzado de la sesión {SesionId} por {Supervisor}", sesionId, supervisor.Username);
    }

    public Task<List<SesionSinCerrar>> SinCerrarAsync(CancellationToken ct = default)
        => db.ConsultarListaAsync("caja.ppSesion_ListarSinCerrar", f => new SesionSinCerrar(f.Entero("SesionId"), f.Texto("CajaCodigo"),
            f.Texto("Cajero"), f.Texto("Estado"), f.FechaHora("FechaApertura")), ct: ct);
}

/// <summary>Cobro de servicios y emisión de e-CF (CU-02, RF-CAJ-01/02).</summary>
public sealed class ServicioCobro(EjecutorSp db, ClienteIntegracionCaja integracion, ILogger<ServicioCobro> log)
{
    public async Task<List<TasaLocal>> TasasAsync(bool soloActivas = true, CancellationToken ct = default)
        => await db.ConsultarListaAsync("caja.ppTasa_Listar", f => new TasaLocal(f.Texto("Codigo"), f.Texto("Descripcion"), f.Decimal("Monto"),
            f.Decimal("ItbisPorcentaje"), f.Booleano("Activa"), f.FechaHora("FechaSincronizacion")), [Parametro.Booleano("@SoloActivas", soloActivas)], ct);

    /// <summary>Busca una orden del Core para cobrar el monto exacto. Devuelve null si no existe o no hay conexión.</summary>
    public async Task<OrdenParaCobro?> BuscarOrdenAsync(string numeroOrden, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(numeroOrden)) return null;
        try
        {
            var o = await integracion.OrdenAsync(numeroOrden, ct);
            if (o is not null && o.Estado != "Pendiente")
                throw new ReglaNegocioException(58115, $"La orden {o.NumeroOrden} está {o.Estado}: no se puede cobrar.");
            return o;
        }
        catch (SinConexionException ex)
        {
            log.LogWarning("No se pudo consultar la orden {Orden}: {Motivo}", numeroOrden, ex.Message);
            return null;
        }
    }

    public async Task<ComprobanteEmitido> CobrarAsync(SesionCaja sesion, UsuarioCaja cajero, SolicitudCobro s, CancellationToken ct = default)
    {
        var documento = ReglasCobro.Validar(s);

        RespuestaRnc? rnc = null;
        var sinConexion = false;
        if (s.QuiereCreditoFiscal || s.EsGubernamental)
        {
            try { rnc = await integracion.RncAsync(documento, ct); }
            catch (SinConexionException) { sinConexion = true; }
        }
        var tipo = ReglasCobro.ElegirTipo(s, rnc, sinConexion);
        var nombre = (tipo is TiposEcf.CreditoFiscal or TiposEcf.Gubernamental) && !string.IsNullOrWhiteSpace(rnc?.RazonSocial)
            ? rnc!.RazonSocial! : s.NombreCliente.Trim();

        var lineas = JsonSerializer.Serialize(s.Lineas.Select(l => new { tasaCodigo = l.TasaCodigo.Trim().ToUpperInvariant(), cantidad = l.Cantidad }));
        var f = (await db.ConsultarAsync("caja.ppFactura_Emitir",
        [
            Parametro.Entero("@SesionId", sesion.SesionId), Parametro.Entero("@UsuarioId", cajero.UsuarioId),
            Parametro.TextoAnsi("@Tipo", tipo, 2), Parametro.Texto("@NumeroOrden", s.NumeroOrden?.Trim().ToUpperInvariant(), 30),
            Parametro.TextoAnsi("@DocumentoCliente", documento, 11), Parametro.Texto("@NombreCliente", nombre, 150),
            Parametro.Texto("@CorreoCliente", s.CorreoCliente, 150), Parametro.Texto("@Lineas", lineas),
            Parametro.Texto("@MedioPago", s.MedioPago, 15), Parametro.Decimal("@MontoRecibido", s.MontoRecibido),
            Parametro.Texto("@Referencia", s.Referencia, 50), Parametro.Texto("@Autorizacion", s.Autorizacion, 50)
        ], ct)).Rows[0];
        var emitido = LeerEmitido(f);
        log.LogInformation("Comprobante {Encf} emitido por {Cajero}: RD${Total}", emitido.Encf, cajero.Username, emitido.Total);

        // La DGII se intenta enseguida; si falla, el comprobante queda en contingencia y lo reenvía el sincronizador.
        var estado = await EnviarDgiiAsync(emitido.FacturaId, emitido.Encf, emitido.Tipo, documento, emitido.Total, emitido.Itbis, emitido.Fecha, null, ct);
        return emitido with { EstadoDgii = estado };
    }

    /// <summary>Nota de crédito (E34) que anula un comprobante; la autoriza un supervisor.</summary>
    public async Task<ComprobanteEmitido> EmitirNotaCreditoAsync(long facturaId, SesionCaja sesion, UsuarioCaja supervisor, string motivo,
        CancellationToken ct = default)
    {
        if (!supervisor.EsSupervisor) throw new ReglaNegocioException(58011, "La nota de crédito debe autorizarla un supervisor de caja.");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10)
            throw new ReglaNegocioException(58012, "Indique el motivo de la nota de crédito (mínimo 10 caracteres).");
        var f = (await db.ConsultarAsync("caja.ppFactura_EmitirNotaCredito",
        [
            Parametro.EnteroLargo("@FacturaId", facturaId), Parametro.Entero("@SesionId", sesion.SesionId),
            Parametro.Entero("@SupervisorId", supervisor.UsuarioId), Parametro.Texto("@Motivo", motivo.Trim(), 300)
        ], ct)).Rows[0];
        var nota = LeerEmitido(f);
        log.LogWarning("Nota de crédito {Encf} sobre la factura {FacturaId} autorizada por {Supervisor}", nota.Encf, facturaId, supervisor.Username);
        var original = await ObtenerAsync(facturaId, ct);
        var estado = await EnviarDgiiAsync(nota.FacturaId, nota.Encf, nota.Tipo, original?.DocumentoCliente, nota.Total, nota.Itbis, nota.Fecha, null, ct);
        return nota with { EstadoDgii = estado };
    }

    public async Task<ComprobanteDetalle?> ObtenerAsync(long facturaId, CancellationToken ct = default)
    {
        var ds = await db.ConsultarVariosAsync("caja.ppFactura_Obtener", [Parametro.EnteroLargo("@FacturaId", facturaId)], ct);
        if (ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return null;
        var f = ds.Tables[0].Rows[0];
        var lineas = ds.Tables[1].Rows.Cast<DataRow>().Select(l => new LineaComprobante(l.Entero("Linea"), l.Texto("TasaCodigo"),
            l.Texto("Descripcion"), l.Entero("Cantidad"), l.Decimal("PrecioUnitario"), l.Decimal("ItbisPorcentaje"), l.Decimal("Itbis"),
            l.Decimal("Total"), l.Guid("TransaccionId"), l.Texto("EstadoSincronizacion"))).ToList();
        var pagos = ds.Tables[2].Rows.Cast<DataRow>().Select(p => new PagoComprobante(p.Texto("MedioPago"), p.Decimal("Monto"),
            p.DecimalONulo("MontoRecibido"), p.DecimalONulo("Cambio"), p.TextoONulo("Referencia"), p.TextoONulo("Autorizacion"))).ToList();
        return new ComprobanteDetalle(f.EnteroLargo("FacturaId"), f.Entero("SesionId"), f.Texto("Tipo"), f.Texto("Encf"), f.TextoONulo("NumeroOrden"),
            f.Texto("DocumentoCliente"), f.Texto("NombreCliente"), f.TextoONulo("CorreoCliente"), f.Decimal("Subtotal"), f.Decimal("Itbis"),
            f.Decimal("Total"), f.Texto("CodigoSeguridad"), f.Texto("EstadoDgii"), f.TextoONulo("TrackId"), f.TextoONulo("MensajeDgii"),
            f.Booleano("Anulada"), f.FechaHora("Fecha"), f.TextoONulo("EncfReferencia"), f.Texto("CajaCodigo"), f.Texto("Cajero"), lineas, pagos);
    }

    public Task<List<ComprobanteResumen>> ListarSesionAsync(int sesionId, CancellationToken ct = default)
        => db.ConsultarListaAsync("caja.ppFactura_ListarSesion", f => new ComprobanteResumen(f.EnteroLargo("FacturaId"), f.Texto("Tipo"),
            f.Texto("Encf"), f.Texto("NombreCliente"), f.Decimal("Total"), f.Texto("EstadoDgii"), f.Booleano("Anulada"), f.FechaHora("Fecha"),
            f.TextoONulo("MedioPago")), [Parametro.Entero("@SesionId", sesionId)], ct);

    public Task<List<EstadoSecuencia>> SecuenciasAsync(CancellationToken ct = default)
        => db.ConsultarListaAsync("caja.ppSecuencia_Estado", f => new EstadoSecuencia(f.Texto("Tipo"), f.EnteroLargo("Siguiente"),
            f.EnteroLargo("Hasta"), f.EnteroLargo("Disponibles"), f.FechaONula("VenceEn") ?? DateOnly.MaxValue, f.Booleano("RequiereAtencion")), ct: ct);

    internal async Task<string> EnviarDgiiAsync(long facturaId, string encf, string tipo, string? documento, decimal total, decimal itbis,
        DateTimeOffset fecha, string? rncEmisor, CancellationToken ct)
    {
        string estado;
        string? trackId = null, mensaje;
        try
        {
            var r = await integracion.EnviarEcfAsync(new ComprobanteFiscal(encf, tipo, rncEmisor ?? "401000001", documento, total, itbis, fecha), ct);
            estado = ReglasCobro.EstadoDesdeDgii(r.Estado);
            trackId = r.TrackId;
            mensaje = r.Mensaje;
        }
        catch (Exception ex) when (ex is SinConexionException or ReglaNegocioException)
        {
            estado = EstadosDgii.Contingencia;
            mensaje = ex.Message;
            log.LogWarning("e-CF {Encf} en contingencia: {Motivo}", encf, ex.Message);
        }
        await db.EjecutarAsync("caja.ppFactura_RegistrarRespuestaDgii",
        [
            Parametro.EnteroLargo("@FacturaId", facturaId), Parametro.Texto("@Estado", estado, 20),
            Parametro.TextoAnsi("@TrackId", trackId, 64), Parametro.Texto("@Mensaje", mensaje, 500)
        ], ct);
        return estado;
    }

    private static ComprobanteEmitido LeerEmitido(DataRow f) => new(f.EnteroLargo("FacturaId"), f.Texto("Tipo"), f.Texto("Encf"),
        f.Texto("CodigoSeguridad"), f.Decimal("Subtotal"), f.Decimal("Itbis"), f.Decimal("Total"), f.DecimalONulo("Cambio"),
        f.FechaHora("Fecha"), f.Texto("EstadoDgii"));
}

/// <summary>
/// Sincroniza la Caja con Integración: tasas (bajada), cobros (subida idempotente por TransaccionId),
/// confirmación del Core y reenvío de e-CF en contingencia. Se puede llamar con un temporizador o con un botón.
/// </summary>
public sealed class SincronizadorCaja(EjecutorSp db, ClienteIntegracionCaja integracion, ServicioCobro cobro, ILogger<SincronizadorCaja> log)
{
    public const int Lote = 50;

    /// <summary>Origen que exigen Integración y el Core para los cobros de la caja.</summary>
    public const string OrigenCaja = "CAJA";

    public async Task<ResumenSincronizacion> ResumenAsync(CancellationToken ct = default)
    {
        var f = (await db.ConsultarAsync("caja.ppSincronizacion_Resumen", null, ct)).Rows[0];
        return new ResumenSincronizacion(f.Entero("PagosPendientes"), f.Entero("PagosPorConfirmar"), f.Entero("PagosRechazados"),
            f.Entero("EcfEnContingencia"), f.FechaHoraONula("UltimaActualizacionTasas"));
    }

    public async Task<ResultadoSincronizacion> SincronizarAsync(CancellationToken ct = default)
    {
        var avisos = new List<string>();
        int tasas = 0, enviados = 0, confirmados = 0, ecf = 0;
        try
        {
            tasas = await TasasAsync(ct);
            enviados = await EnviarPagosAsync(avisos, ct);
            confirmados = await ConfirmarPagosAsync(ct);
            ecf = await ReenviarEcfAsync(ct);
            return new ResultadoSincronizacion(true, tasas, enviados, confirmados, ecf, avisos);
        }
        catch (SinConexionException ex)
        {
            log.LogWarning("Sincronización interrumpida: {Motivo}", ex.Message);
            avisos.Add("Sin conexión con Integración: se reintentará más tarde. La caja sigue cobrando.");
            return new ResultadoSincronizacion(false, tasas, enviados, confirmados, ecf, avisos);
        }
    }

    public async Task<int> TasasAsync(CancellationToken ct = default)
    {
        var lista = await integracion.TasasAsync(ct);
        if (lista is null || lista.Count == 0) return 0;
        var json = JsonSerializer.Serialize(lista.Select(t => new
            { codigo = t.Codigo, descripcion = t.Descripcion, monto = t.Monto, itbisPorcentaje = t.ItbisPorcentaje, activa = t.Activa }));
        await db.ConsultarAsync("caja.ppTasa_Sincronizar", [Parametro.Texto("@Tasas", json)], ct);
        return lista.Count;
    }

    public async Task<int> EnviarPagosAsync(List<string>? avisos = null, CancellationToken ct = default)
    {
        var pendientes = await db.ConsultarAsync("caja.ppLinea_PendientesSincronizar", [Parametro.Entero("@Maximo", Lote)], ct);
        var enviados = 0;
        foreach (DataRow f in pendientes.Rows)
        {
            var tx = f.Guid("TransaccionId");
            var pago = new PagoCaja(tx, OrigenCaja, f.Texto("NumeroComprobante"), f.Texto("TasaCodigo"), f.Entero("Cantidad"), f.Decimal("Total"),
                f.TextoONulo("MedioPago") ?? MediosPagoCaja.Efectivo, f.Texto("DocumentoCliente"), f.Texto("NombreCliente"),
                f.TextoONulo("NumeroOrden"), null, f.Texto("CajaCodigo"), f.Texto("Cajero"), f.FechaHora("Fecha"));
            try
            {
                var r = await integracion.EnviarPagoAsync(pago, pago.Cajero, ct);
                await RegistrarAsync(tx, ReglasCobro.EstadoLinea(r.Estado), r.Detalle, ct);
                enviados++;
            }
            catch (ReglaNegocioException ex)
            {
                await RegistrarAsync(tx, "Error", ex.Message, ct);
                avisos?.Add($"Cobro {pago.NumeroComprobante}: {ex.Message}");
            }
        }
        return enviados;
    }

    public async Task<int> ConfirmarPagosAsync(CancellationToken ct = default)
    {
        var lista = await db.ConsultarListaAsync("caja.ppLinea_PendientesConfirmacion", f => f.Guid("TransaccionId"), [Parametro.Entero("@Maximo", Lote)], ct);
        var confirmados = 0;
        foreach (var tx in lista)
        {
            var r = await integracion.EstadoPagoAsync(tx, ct);
            if (r is null) { await RegistrarAsync(tx, "Error", "Integración no conoce el cobro: se reenviará.", ct); continue; }
            var estado = ReglasCobro.EstadoLinea(r.Estado);
            if (estado == "Recibido") continue;
            await RegistrarAsync(tx, estado, r.Detalle, ct);
            confirmados++;
        }
        return confirmados;
    }

    public async Task<int> ReenviarEcfAsync(CancellationToken ct = default)
    {
        var pendientes = await db.ConsultarAsync("caja.ppFactura_PendientesDgii", [Parametro.Entero("@Maximo", Lote)], ct);
        var aceptados = 0;
        foreach (DataRow f in pendientes.Rows)
        {
            var estado = await cobro.EnviarDgiiAsync(f.EnteroLargo("FacturaId"), f.Texto("Encf"), f.Texto("Tipo"), f.TextoONulo("DocumentoCliente"),
                f.Decimal("Total"), f.Decimal("Itbis"), f.FechaHora("Fecha"), f.Texto("RncEmisor"), ct);
            if (estado == EstadosDgii.Contingencia) break;   // la DGII sigue sin responder: no insistir con el resto
            aceptados++;
        }
        return aceptados;
    }

    private Task RegistrarAsync(Guid tx, string estado, string? error, CancellationToken ct)
        => db.EjecutarAsync("caja.ppLinea_RegistrarSincronizacion",
            [Parametro.Guid("@TransaccionId", tx), Parametro.Texto("@Estado", estado, 12), Parametro.Texto("@Error", error, 500)], ct);
}

/// <summary>
/// Reportes de la caja (RDLC → PDF o Excel). Cada transacción tiene su reporte:
/// los cobros y notas de crédito, su e-CF impreso; el cierre, su reporte de cierre; y cualquier evento
/// de la bitácora (apertura, cobro, cierre...), su comprobante. Generar un reporte también queda en la bitácora.
/// </summary>
public sealed class ServicioReportesCaja(EjecutorSp db)
{
    private const string Modulo = "Módulo de Caja";

    /// <summary>Tablas: [0] encabezado de la sesión, [1] totales por medio de pago, [2] totales por servicio.</summary>
    public Task<DataSet> CierreCajaAsync(int sesionId, CancellationToken ct = default)
        => db.ConsultarVariosAsync("caja.ppRptCierreCaja", [Parametro.Entero("@SesionId", sesionId)], ct);

    public Task<DataTable> IngresosAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        if (desde > hasta) throw new ReglaNegocioException(58018, "La fecha inicial no puede ser mayor que la final.");
        return db.ConsultarAsync("caja.ppRptIngresos", [Parametro.Fecha("@Desde", desde), Parametro.Fecha("@Hasta", hasta)], ct);
    }

    // ------------------------------------------------------------------ archivos

    /// <summary>e-CF impreso (factura o nota de crédito). Se usa al cobrar y para reimprimir.</summary>
    public async Task<ArchivoReporte> FacturaAsync(UsuarioCaja u, long facturaId, FormatoReporte formato = FormatoReporte.Pdf, CancellationToken ct = default)
    {
        var ds = await db.ConsultarVariosAsync("caja.ppRptFactura", [Parametro.EnteroLargo("@FacturaId", facturaId)], ct);
        var encf = ds.Tables[0].Rows[0].Texto("Encf");
        return await ArchivoAsync(u, "rptFactura", formato, $"e-NCF {encf}", ct,
            ("Encabezado", ds.Tables[0]), ("Lineas", ds.Tables[1]), ("Pagos", ds.Tables[2]));
    }

    public async Task<ArchivoReporte> CierreAsync(UsuarioCaja u, int sesionId, FormatoReporte formato = FormatoReporte.Pdf, CancellationToken ct = default)
    {
        var ds = await CierreCajaAsync(sesionId, ct);
        if (ds.Tables[0].Rows.Count == 0) throw new ReglaNegocioException(58023, "La sesión de caja indicada no existe.");
        return await ArchivoAsync(u, "rptCierreCaja", formato, $"Sesión {sesionId}", ct,
            ("Sesion", ds.Tables[0]), ("MediosPago", ds.Tables[1]), ("Servicios", ds.Tables[2]));
    }

    /// <summary>Ingresos por servicio (solo supervisor o administrador).</summary>
    public async Task<ArchivoReporte> IngresosArchivoAsync(UsuarioCaja u, DateOnly desde, DateOnly hasta, FormatoReporte formato = FormatoReporte.Pdf,
        CancellationToken ct = default)
    {
        ExigirSupervisor(u);
        return await ArchivoAsync(u, "rptIngresosCaja", formato, $"Del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}", ct, ("Datos", await IngresosAsync(desde, hasta, ct)));
    }

    /// <summary>Transacciones del período: el supervisor ve las de todos; el cajero, solo las suyas.</summary>
    public Task<DataTable> TransaccionesAsync(UsuarioCaja u, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
        => db.ConsultarAsync("caja.ppRptTransacciones",
        [
            Parametro.Fecha("@Desde", desde), Parametro.Fecha("@Hasta", hasta),
            Parametro.Texto("@Usuario", u.EsSupervisor ? null : u.Username, 50)
        ], ct);

    public async Task<ArchivoReporte> TransaccionesArchivoAsync(UsuarioCaja u, DateOnly desde, DateOnly hasta, FormatoReporte formato = FormatoReporte.Pdf,
        CancellationToken ct = default)
        => await ArchivoAsync(u, "rptTransaccionesCaja", formato, $"Del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}", ct,
            ("Datos", await TransaccionesAsync(u, desde, hasta, ct)));

    /// <summary>Comprobante de una transacción de la bitácora (el cajero solo los suyos).</summary>
    public async Task<ArchivoReporte> ComprobanteAsync(UsuarioCaja u, long eventoId, FormatoReporte formato = FormatoReporte.Pdf, CancellationToken ct = default)
    {
        var t = await db.ConsultarAsync("caja.ppRptComprobanteTransaccion", [Parametro.EnteroLargo("@EventoId", eventoId)], ct);
        if (!u.EsSupervisor && !string.Equals(t.Rows[0].Texto("Usuario"), u.Username, StringComparison.OrdinalIgnoreCase))
            throw new ReglaNegocioException(58024, "Solo puede imprimir el comprobante de sus propias transacciones.");
        return await ArchivoAsync(u, "rptComprobanteTransaccionCaja", formato, $"Transacción N.º {eventoId}", ct, ("Datos", t));
    }

    private async Task<ArchivoReporte> ArchivoAsync(UsuarioCaja u, string reporte, FormatoReporte formato, string filtros, CancellationToken ct,
        params (string, DataTable)[] datos)
    {
        var archivo = GeneradorReportes.Generar(reporte, new EncabezadoReporte(Modulo, $"{u.NombreCompleto} ({u.Username})", filtros), formato, datos);
        await Eventos.RegistrarAsync(db, u.Username, "ReporteGenerado", $"{reporte} · {formato} · {filtros}", ct);
        return archivo;
    }

    private static void ExigirSupervisor(UsuarioCaja u)
    {
        if (!u.EsSupervisor) throw new ReglaNegocioException(58011, "Este reporte es solo para supervisores de caja.");
    }
}
