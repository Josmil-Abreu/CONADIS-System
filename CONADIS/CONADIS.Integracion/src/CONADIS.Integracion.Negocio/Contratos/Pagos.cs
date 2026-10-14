namespace CONADIS.Integracion.Negocio.Contratos;

public static class MediosPago
{
    public const string Efectivo = "Efectivo";
    public const string Tarjeta = "Tarjeta";
    public const string Transferencia = "Transferencia";
    public const string EnLinea = "EnLinea";

    public static readonly string[] DeCaja = [Efectivo, Tarjeta, Transferencia];
}

public static class OrigenesPago
{
    public const string Caja = "CAJA";
    public const string Web = "WEB";
}

public static class EstadosPago
{
    public const string Recibido = "Recibido";   // guardado en Integración, pendiente de entregar al Core
    public const string Aplicado = "Aplicado";   // el Core ya lo registró
    public const string Rechazado = "Rechazado"; // el Core lo rechazó por una regla de negocio
}

public static class EstadosAcuse
{
    public const string Recibido = "Recibido";
    public const string Duplicado = "Duplicado"; // ya había llegado: no se registró otra vez
}

/// <summary>
/// Cobro hecho en ventanilla. <see cref="TransaccionId"/> lo genera la Caja y hace que
/// reenviar el mismo cobro (por ejemplo, tras una caída) nunca lo registre dos veces.
/// </summary>
public sealed record CobroCajaDto(
    Guid TransaccionId,
    string Caja,
    string Cajero,
    string NumeroComprobante,
    string TasaCodigo,
    string Concepto,
    int Cantidad,
    decimal MontoUnitario,
    decimal Total,
    string MedioPago,
    string Documento,
    string NombrePagador,
    string? NumeroOrden,
    string? Referencia,
    DateTime FechaCobro);

/// <summary>Pago con tarjeta desde el portal; Integración lo autoriza con la pasarela.</summary>
public sealed record PagoEnLineaDto(
    Guid TransaccionId,
    string NumeroOrden,
    string TasaCodigo,
    string Concepto,
    int Cantidad,
    decimal MontoUnitario,
    decimal Total,
    string Documento,
    string NombrePagador,
    string Titular,
    string NumeroTarjeta);

/// <summary>Respuesta de Integración al recibir un pago (de la Caja o del portal).</summary>
public sealed record AcuseCobroDto(
    Guid TransaccionId,
    string Estado,                   // Recibido | Duplicado
    long PagoId,
    DateTime FechaRecepcion,
    string? ReferenciaPasarela = null);

public sealed record PagoRecibidoDto(
    long PagoId,
    Guid TransaccionId,
    string ClienteCodigo,
    string Origen,
    string? Caja,
    string? Cajero,
    string NumeroComprobante,
    string TasaCodigo,
    string Concepto,
    int Cantidad,
    decimal MontoUnitario,
    decimal Total,
    string MedioPago,
    string Documento,
    string NombrePagador,
    string? NumeroOrden,
    string? Referencia,
    DateTime FechaCobro,
    DateTime FechaRecepcion,
    string Estado,
    DateTime? FechaAplicacion,
    string? Observacion,
    Guid? MensajeGuid,
    string? EstadoMensaje);

/// <summary>Evento de la Caja para el Core (apertura, cierre, anulación...). Se entrega por la cola.</summary>
public sealed record EventoDto(Guid MensajeId, Guid RecursoId, string Tipo, string? DatosJson);
