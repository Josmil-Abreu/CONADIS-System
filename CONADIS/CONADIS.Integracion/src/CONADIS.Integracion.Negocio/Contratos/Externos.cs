namespace CONADIS.Integracion.Negocio.Contratos;

/* ------------------------------- JCE (cédulas) ------------------------------ */

/// <summary>Respuesta de la JCE al validar una cédula.</summary>
public sealed record ValidacionJceDto(
    string Cedula,
    bool EsValida,
    string? Nombres,
    string? Apellidos,
    DateOnly? FechaNacimiento,
    int? Sexo,                       // 1 = Masculino, 2 = Femenino
    string? Mensaje);

/* ----------------------------- MSP (diagnósticos) --------------------------- */

public sealed record ValidacionMspSolicitudDto(string Cedula, string CodigoCie10);

/// <summary>Respuesta del MSP al validar un diagnóstico CIE-10.</summary>
public sealed record ValidacionMspDto(string CodigoCie10, bool EsValido, string? Descripcion, string? Mensaje);

/* --------------------------- DGII (comprobantes e-CF) ----------------------- */

public sealed record LineaFacturaDto(string Descripcion, int Cantidad, decimal PrecioUnitario, decimal Itbis);

/// <summary>Comprobante fiscal electrónico que la Caja manda a firmar y a enviar a la DGII.</summary>
public sealed record FacturaFiscalDto(
    Guid FacturaId,
    string Encf,                     // E310000000001
    string TipoEcf,                  // E31, E32, E34...
    string RncEmisor,
    string? DocumentoComprador,      // cédula o RNC; null en consumidor final
    string? NombreComprador,
    DateTime FechaEmision,
    decimal Subtotal,
    decimal Itbis,
    decimal Total,
    IReadOnlyList<LineaFacturaDto> Lineas);

public sealed record DocumentoFiscalDto(string Xml, string CodigoSeguridad, string QrUrl, bool EsPrueba);

public static class EstadosDgii
{
    public const string Aceptado = "Aceptado";
    public const string AceptadoCondicional = "AceptadoCondicional";
    public const string Rechazado = "Rechazado";
    public const string EnProceso = "EnProceso";
    /// <summary>La DGII no respondió: el envío quedó en la cola de Integración (contingencia).</summary>
    public const string EnCola = "EnCola";
}

public sealed record RespuestaFiscalDto(string Encf, string Estado, string? TrackId, string? Mensaje);

/* ------------------------------- Firma digital ------------------------------ */

public sealed record SolicitudFirmaDto(string Usuario, string Referencia, string Contenido);

public sealed record FirmaDto(string Referencia, string Firma, string Algoritmo, DateTime FechaFirma, bool EsPrueba);

/* ----------------------------- Pasarela de pagos ---------------------------- */

public sealed record SolicitudAutorizacionDto(Guid TransaccionId, decimal Monto, string Titular, string NumeroTarjeta);

public sealed record AutorizacionPagoDto(Guid TransaccionId, bool Aprobado, string? Referencia, string? Mensaje);
