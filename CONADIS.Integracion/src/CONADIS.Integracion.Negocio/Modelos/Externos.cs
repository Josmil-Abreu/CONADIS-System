namespace CONADIS.Integracion.Negocio.Modelos;

// Modelos de las respuestas de los servicios externos (JCE, MSP, DGII, firma, pasarela).

public static class EstadosIdentidad
{
    public const string Validada = "Validada";
    public const string Invalida = "Invalida";
    public const string Pendiente = "Pendiente";
}

/// <summary>Lo que devuelve la JCE: si la cédula es válida y los datos oficiales (que prevalecen, RF-INT-01 paso 3).</summary>
public sealed record RespuestaJce(bool Valida, string? Nombres, string? Apellidos, DateOnly? FechaNacimiento, int? Sexo, string? Motivo);

/// <summary>Resultado de validar una identidad: Validada | Invalida | Pendiente (JCE no disponible).</summary>
public sealed record ResultadoIdentidad(string Estado, string? Nombres, string? Apellidos, DateOnly? FechaNacimiento,
    int? Sexo, string? Motivo, bool DesdeCache)
{
    public static ResultadoIdentidad Pendiente(string motivo) => new(EstadosIdentidad.Pendiente, null, null, null, null, motivo, false);
}

public static class EstadosDiagnostico
{
    public const string Validado = "Validado";
    public const string NoCoincide = "NoCoincide";
    public const string NoEncontrado = "NoEncontrado";
    public const string ValidacionManual = "ValidacionManual";
}

/// <summary>Datos del certificado médico de origen que se validan con el MSP/SNS (RF-INT-02).</summary>
public sealed record ConsultaDiagnostico(string Cedula, string? Establecimiento, string? NumeroCertificado, string? CodigoCie10);

/// <summary>Consulta de RNC en la DGII.</summary>
public sealed record RespuestaRnc(bool Existe, bool Activo, string? RazonSocial);

/// <summary>Comprobante fiscal electrónico (e-CF) que la Caja envía a la DGII a través de Integración.</summary>
public sealed record ComprobanteFiscal(string Encf, string TipoEcf, string RncEmisor, string? DocumentoComprador,
    decimal Total, decimal Itbis, DateTimeOffset FechaEmision);

/// <summary>Respuesta de la DGII a un e-CF.</summary>
public sealed record RespuestaEcf(string Estado, string? CodigoSeguridad, string? TrackId, string? Mensaje);

public static class EstadosEcf
{
    public const string Aceptado = "Aceptado";
    public const string Rechazado = "Rechazado";
}

/// <summary>Firma digital ES256 (JWS compacto) con su huella y sello de tiempo (RF-INT-03).</summary>
public sealed record FirmaDocumento(string Firma, string Algoritmo, string HuellaSha256, DateTimeOffset SelloTiempo, string IdCertificado);

/// <summary>Cobro en línea con la pasarela de pagos (tarjeta tokenizada; nunca el número de la tarjeta).</summary>
public sealed record CobroEnLinea(string NumeroOrden, decimal Monto, string TokenTarjeta, string Documento, string NombrePagador);

public sealed record RespuestaPasarela(bool Aprobado, string? CodigoAutorizacion, string? Referencia, string? Motivo);

/// <summary>Notificación al ciudadano que el Core dejó en su cola.</summary>
public sealed record NotificacionCore(long NotificacionId, string Canal, string Destinatario, string Asunto, string Mensaje, int Intentos);

/// <summary>Persona con identidad pendiente de la JCE (viene del Core).</summary>
public sealed record IdentidadPendienteCore(int PersonaId, string Cedula, string Nombres, string Apellidos,
    string? NumeroExpediente, DateTimeOffset FechaCreacion, bool RequiereAnalista);

/// <summary>Verificación institucional que devuelve el Core (atributos mínimos).</summary>
public sealed record VerificacionInstitucional(bool TieneCertificacionVigente, string? TipoDiscapacidad,
    string? GradoDiscapacidad, DateOnly? FechaVencimiento);

/// <summary>Orden de cobro del Core (lo necesario para cobrarla en línea).</summary>
public sealed record OrdenCore(string NumeroOrden, string TasaCodigo, string Concepto, string Documento, string NombrePagador,
    int Cantidad, decimal Total, string Estado);
