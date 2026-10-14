using System.Data;
using log4net;
using CONADIS.Integracion.Negocio.Conectores;
using CONADIS.Integracion.Negocio.Contratos;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Logging;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Pagos que llegan a Integración: cobros de ventanilla (Caja) y pagos en línea (portal).
/// snd.ppPago_Registrar guarda el pago y encola su mensaje para el Core en una sola
/// transacción: aunque el Core esté caído, el pago queda guardado y llegará después.
/// </summary>
public sealed class ServicioPagos(EjecutorSp sp, ServicioResiliencia resiliencia, IConectorPasarela pasarela)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioPagos));

    public async Task<AcuseCobroDto> RegistrarCobroCajaAsync(ClienteApi cliente, CobroCajaDto cobro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(cobro);

        if (cobro.TransaccionId == Guid.Empty)
            throw new SolicitudInvalidaException("El identificador de la transacción es obligatorio.");
        if (!MediosPago.DeCaja.Contains(cobro.MedioPago))
            throw new SolicitudInvalidaException($"Medio de pago no válido en caja. Válidos: {string.Join(", ", MediosPago.DeCaja)}.");
        ValidarComunes(cobro.Cantidad, cobro.MontoUnitario, cobro.Total);

        var acuse = await RegistrarAsync(cliente, cobro.TransaccionId, OrigenesPago.Caja,
            Validaciones.Requerido(cobro.Caja, "caja", 60),
            Validaciones.Requerido(cobro.Cajero, "cajero", 100),
            Validaciones.Requerido(cobro.NumeroComprobante, "número de comprobante", 30),
            Validaciones.Requerido(cobro.TasaCodigo, "código de tasa", 40),
            Validaciones.Requerido(cobro.Concepto, "concepto", 200),
            cobro.Cantidad, cobro.MontoUnitario, cobro.Total, cobro.MedioPago,
            Validaciones.Documento(cobro.Documento),
            Validaciones.Requerido(cobro.NombrePagador, "nombre del pagador", 200),
            Validaciones.Opcional(cobro.NumeroOrden, "número de orden", 30),
            Validaciones.Opcional(cobro.Referencia, "referencia", 100),
            cobro.FechaCobro.Kind == DateTimeKind.Local ? cobro.FechaCobro.ToUniversalTime() : cobro.FechaCobro,
            ct);

        Log.Info($"Cobro de caja {cobro.TransaccionId} ({cobro.Caja}, {cobro.Total:0.00}): {acuse.Estado}.");
        return acuse;
    }

    /// <summary>
    /// Pago con tarjeta desde el portal. Primero revisa si ya se registró (para no cobrar dos
    /// veces la misma transacción), luego autoriza con la pasarela y por último lo registra.
    /// </summary>
    public async Task<AcuseCobroDto> RegistrarPagoEnLineaAsync(ClienteApi cliente, PagoEnLineaDto pago, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(pago);

        if (pago.TransaccionId == Guid.Empty)
            throw new SolicitudInvalidaException("El identificador de la transacción es obligatorio.");
        ValidarComunes(pago.Cantidad, pago.MontoUnitario, pago.Total);
        var documento = Validaciones.Documento(pago.Documento);
        var tarjeta = Validaciones.Tarjeta(pago.NumeroTarjeta);
        var titular = Validaciones.Requerido(pago.Titular, "titular", 100);

        var existente = await ObtenerAsync(pago.TransaccionId, ct);
        if (existente is not null)
        {
            if (existente.Total != pago.Total)
                throw new ReglaNegocioException(50309, "Ya existe un pago con ese identificador de transacción y un monto distinto.");
            return new AcuseCobroDto(existente.TransaccionId, EstadosAcuse.Duplicado, existente.PagoId,
                existente.FechaRecepcion, existente.Referencia);
        }

        var autorizacion = await resiliencia.EjecutarAsync(CodigosServicio.Pasarela, "Pasarela.Autorizar",
            c => pasarela.AutorizarAsync(new SolicitudAutorizacionDto(pago.TransaccionId, pago.Total, titular, tarjeta), c), ct);

        if (!autorizacion.Aprobado || string.IsNullOrEmpty(autorizacion.Referencia))
            throw new RechazoServicioExternoException(CodigosServicio.Pasarela, autorizacion.Mensaje ?? "El pago fue declinado.");

        var acuse = await RegistrarAsync(cliente, pago.TransaccionId, OrigenesPago.Web, null, null,
            Recortar("WEB-" + autorizacion.Referencia, 30),
            Validaciones.Requerido(pago.TasaCodigo, "código de tasa", 40),
            Validaciones.Requerido(pago.Concepto, "concepto", 200),
            pago.Cantidad, pago.MontoUnitario, pago.Total, MediosPago.EnLinea, documento,
            Validaciones.Requerido(pago.NombrePagador, "nombre del pagador", 200),
            Validaciones.Requerido(pago.NumeroOrden, "número de orden", 30),
            autorizacion.Referencia, DateTime.UtcNow, ct);

        Log.Info($"Pago en línea {pago.TransaccionId} autorizado ({autorizacion.Referencia}): {acuse.Estado}.");
        return acuse with { ReferenciaPasarela = autorizacion.Referencia };
    }

    /// <summary>Para la Caja (IVerificadorPago): confirma con la pasarela que una referencia existe y es por ese monto.</summary>
    public Task<bool> VerificarPagoAsync(string referencia, decimal monto, CancellationToken ct = default)
    {
        Validaciones.Requerido(referencia, "referencia", 100);
        Validaciones.Monto(monto, "monto");
        return resiliencia.EjecutarAsync(CodigosServicio.Pasarela, "Pasarela.Verificar",
            c => pasarela.VerificarAsync(referencia.Trim(), monto, c), ct);
    }

    public Task<PagoRecibidoDto?> ObtenerAsync(Guid transaccionId, CancellationToken ct = default)
        => sp.ConsultarUnoAsync("snd.ppPago_ObtenerPorTransaccion", Mapear, ct,
            Parametro.Guid("@TransaccionId", transaccionId));

    public Task<List<PagoRecibidoDto>> ListarAsync(DateTime desde, DateTime hasta, string? origen = null,
        string? estado = null, CancellationToken ct = default)
    {
        Validaciones.Rango(desde, hasta);
        return sp.ConsultarListaAsync("snd.ppPago_Listar", Mapear, ct,
            Parametro.Fecha("@Desde", desde),
            Parametro.Fecha("@Hasta", hasta),
            Parametro.Texto("@Origen", origen, 10),
            Parametro.Texto("@Estado", estado, 15));
    }

    internal Task MarcarAplicadoAsync(Guid transaccionId, CancellationToken ct)
        => sp.EjecutarAsync("snd.ppPago_MarcarAplicado", ct, Parametro.Guid("@TransaccionId", transaccionId));

    internal Task MarcarRechazadoAsync(Guid transaccionId, string motivo, CancellationToken ct)
        => sp.EjecutarAsync("snd.ppPago_MarcarRechazado", ct,
            Parametro.Guid("@TransaccionId", transaccionId),
            Parametro.Texto("@Motivo", Recortar(motivo, 500), 500));

    // -------------------------------------------------------------------------

    private async Task<AcuseCobroDto> RegistrarAsync(
        ClienteApi cliente, Guid transaccionId, string origen, string? caja, string? cajero,
        string numeroComprobante, string tasaCodigo, string concepto, int cantidad, decimal montoUnitario,
        decimal total, string medioPago, string documento, string nombrePagador, string? numeroOrden,
        string? referencia, DateTime fechaCobro, CancellationToken ct)
    {
        var pagoId = Parametro.Salida("@PagoId", SqlDbType.BigInt);
        var resultado = Parametro.Salida("@Resultado", SqlDbType.NVarChar, 15);

        await sp.EjecutarAsync("snd.ppPago_Registrar", ct,
            Parametro.Entero("@ClienteId", cliente.ClienteId),
            Parametro.Guid("@TransaccionId", transaccionId),
            Parametro.Texto("@Origen", origen, 10),
            Parametro.Texto("@Caja", caja, 60),
            Parametro.Texto("@Cajero", cajero, 100),
            Parametro.Texto("@NumeroComprobante", numeroComprobante, 30),
            Parametro.Texto("@TasaCodigo", tasaCodigo, 40),
            Parametro.Texto("@Concepto", concepto, 200),
            Parametro.Entero("@Cantidad", cantidad),
            Parametro.Decimal("@MontoUnitario", montoUnitario),
            Parametro.Decimal("@Total", total),
            Parametro.Texto("@MedioPago", medioPago, 20),
            Parametro.Texto("@Documento", documento, 11),
            Parametro.Texto("@NombrePagador", nombrePagador, 200),
            Parametro.Texto("@NumeroOrden", numeroOrden, 30),
            Parametro.Texto("@Referencia", referencia, 100),
            Parametro.Fecha("@FechaCobro", fechaCobro),
            Parametro.Guid("@CorrelacionId", Correlacion.Id),
            pagoId, resultado);

        return new AcuseCobroDto(transaccionId, resultado.Texto() ?? EstadosAcuse.Recibido,
            pagoId.EnteroLargo() ?? 0, DateTime.UtcNow);
    }

    private static void ValidarComunes(int cantidad, decimal montoUnitario, decimal total)
    {
        if (cantidad is < 1 or > 1000)
            throw new SolicitudInvalidaException("La cantidad debe estar entre 1 y 1000.");
        Validaciones.Monto(montoUnitario, "monto unitario", permitirCero: true);
        Validaciones.Monto(total, "total");
    }

    private static PagoRecibidoDto Mapear(DataRow f) => new(
        f.EnteroLargo("PagoId"), f.Guid("TransaccionId"), f.Texto("ClienteCodigo"), f.Texto("Origen"),
        f.TextoONulo("Caja"), f.TextoONulo("Cajero"), f.Texto("NumeroComprobante"), f.Texto("TasaCodigo"),
        f.Texto("Concepto"), f.Entero("Cantidad"), f.Decimal("MontoUnitario"), f.Decimal("Total"),
        f.Texto("MedioPago"), f.Texto("Documento"), f.Texto("NombrePagador"), f.TextoONulo("NumeroOrden"),
        f.TextoONulo("Referencia"), f.Fecha("FechaCobro"), f.Fecha("FechaRecepcion"), f.Texto("Estado"),
        f.FechaONula("FechaAplicacion"), f.TextoONulo("Observacion"), f.GuidONulo("MensajeGuid"),
        f.TextoONulo("EstadoMensaje"));

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
