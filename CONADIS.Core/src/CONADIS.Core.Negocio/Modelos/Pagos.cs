namespace CONADIS.Core.Negocio.Modelos;

public sealed record OrdenCobro(string NumeroOrden, string TasaCodigo, string Concepto, string Documento,
    string NombrePagador, string? Referencia, int Cantidad, decimal MontoUnitario, decimal Total, string Estado,
    DateTimeOffset FechaCreacion, DateTimeOffset? FechaPago, string? NumeroComprobante);

/// <summary>Pago que llega desde la Caja o el portal a través de Integración.</summary>
public sealed record NuevoPago(
    Guid TransaccionId,            // lo genera quien cobra: reenviar el mismo pago no lo duplica
    string Origen,                 // CAJA | WEB
    string NumeroComprobante,
    string TasaCodigo,
    int Cantidad,
    decimal Total,
    string MedioPago,              // Efectivo | Tarjeta | Transferencia | EnLinea
    string Documento,
    string NombrePagador,
    string? NumeroOrden,
    string? Referencia,
    string? Caja,
    string? Cajero,
    DateTimeOffset FechaPago);

public static class EstadosPago
{
    public const string Aplicado = "Aplicado";
    public const string Duplicado = "Duplicado";
}

public sealed record NuevaSolicitudServicio(string Tipo, string Documento, string NombreSolicitante, string Correo,
    string Resumen, string Detalle);

public static class TiposServicio
{
    public const string AyudaTecnica = "AYUDA_TECNICA";
    public const string CertificacionAccesibilidad = "CERT_ACCESIBILIDAD";
}

public sealed record ServicioRegistrado(string NumeroTramite, DateTimeOffset FechaRegistro, string? NumeroOrden);

public sealed record SolicitudServicio(int ServicioId, string NumeroTramite, string Tipo, string Documento,
    string NombreSolicitante, string Correo, string Resumen, string Detalle, string Unidad, string Estado,
    string? NumeroOrden, string? EstadoOrden, DateTimeOffset FechaRegistro, DateTimeOffset FechaActualizacion);

public sealed record NotificacionPendiente(long NotificacionId, string Canal, string Destinatario, string Asunto,
    string Mensaje, int Intentos);

public sealed record AvisoPortal(string Asunto, string Mensaje, DateTimeOffset Fecha);