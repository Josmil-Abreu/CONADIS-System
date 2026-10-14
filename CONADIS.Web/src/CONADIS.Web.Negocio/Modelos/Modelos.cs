namespace CONADIS.Web.Negocio.Modelos;

// ------------------------------------------------------------------ cuentas del portal

/// <summary>Datos del ciudadano que se guardan en la sesión (cookie) del portal.</summary>
public sealed record CuentaSesion(int CuentaId, string Cedula, string NombreCompleto, string Correo, string EstadoIdentidad,
    bool CorreoVerificado, bool AltoContraste, int TamanoTexto);

public sealed record ResultadoLoginCiudadano(bool Exito, string? Mensaje, CuentaSesion? Cuenta)
{
    public static ResultadoLoginCiudadano Correcto(CuentaSesion c) => new(true, null, c);
    public static ResultadoLoginCiudadano Fallido(string m) => new(false, m, null);
}

/// <summary>Formulario "Crear cuenta".</summary>
public sealed record DatosRegistro(string Cedula, string Correo, string? Telefono, string Contrasena, string ConfirmacionContrasena,
    bool AceptaTerminos);

/// <summary>Resultado del registro. EnlaceVerificacion solo se muestra en desarrollo (en producción va por correo).</summary>
public sealed record ResultadoRegistro(int CuentaId, string NombreCompleto, string EstadoIdentidad, string TokenVerificacion);

public sealed record PerfilCuenta(int CuentaId, string Cedula, string Correo, string? Telefono, string NombreCompleto,
    string EstadoIdentidad, bool CorreoVerificado, DateTimeOffset? UltimoAcceso, DateTimeOffset FechaCreacion,
    bool AltoContraste, int TamanoTexto);

public sealed record EventoSeguridad(DateTimeOffset Fecha, string Evento, string? Ip, string? Detalle);

/// <summary>Formulario de varios pasos guardado para continuar después.</summary>
public sealed record Borrador(int Paso, string Datos, string ClaveIdempotencia, DateTimeOffset FechaActualizacion);

public static class TiposBorrador
{
    public const string Certificacion = "Certificacion";
    public const string Renovacion = "Renovacion";
    public const string Duplicado = "Duplicado";
    public const string AyudaTecnica = "AyudaTecnica";
    public const string Accesibilidad = "Accesibilidad";
}

// ------------------------------------------------------------------ lo que devuelve Integración (mismos datos que el Core)

public sealed record CentroValoracion(int CentroId, string Nombre, string Provincia, int CapacidadDiaria, bool Activo);

public sealed record CupoDisponible(DateOnly Fecha, int CapacidadDiaria, int CuposLibres);

public sealed record Tasa(int TasaId, string Codigo, string Descripcion, decimal Monto, decimal ItbisPorcentaje, string Resolucion, bool Activa);

public sealed record TramiteResumen(string Numero, string Tipo, string Estado, DateTimeOffset Actualizado, string Persona);

public sealed record HitoTramite(string Estado, DateTimeOffset Fecha, string? Comentario);

public sealed record TramiteCiudadano(string Numero, string? NumeroExpediente, string Cedula, string NombreCompleto,
    string Estado, DateTimeOffset FechaRegistro, DateTimeOffset FechaActualizacion, string? TipoDiscapacidad,
    string? GradoDiscapacidad, string? NumeroCarnet, string? CentroValoracion, DateTimeOffset? FechaCita,
    string? Observaciones, IReadOnlyList<HitoTramite> Historial);

public sealed record CarnetCiudadano(string NumeroCarnet, string NumeroExpediente, string Cedula, string NombreCompleto,
    string? TipoDiscapacidad, string? GradoDiscapacidad, DateOnly FechaEmision, DateOnly? FechaVencimiento,
    string Estado, string CodigoVerificacion, string NumeroCertificado);

public sealed record AvisoPortal(string Asunto, string Mensaje, DateTimeOffset Fecha);

public sealed record VerificacionPublica(string Resultado, string? TitularParcial, DateOnly? FechaEmision, DateOnly? ValidoHasta);

public sealed record SolicitudRegistrada(string NumeroTramite, string NumeroExpediente, DateTimeOffset FechaRegistro, string? NumeroOrden);

public sealed record ServicioRegistrado(string NumeroTramite, DateTimeOffset FechaRegistro, string? NumeroOrden);

public sealed record OrdenCobro(string NumeroOrden, string TasaCodigo, string Concepto, string Documento, string NombrePagador,
    string? Referencia, int Cantidad, decimal MontoUnitario, decimal Total, string Estado, DateTimeOffset FechaCreacion,
    DateTimeOffset? FechaPago, string? NumeroComprobante);

public sealed record PagoEnLinea(Guid TransaccionId, string Estado, string CodigoAutorizacion, string NumeroComprobante);

public sealed record IdentidadJce(string Estado, string? Nombres, string? Apellidos, string? Motivo);

/// <summary>Solicitud de certificación, renovación o duplicado (la misma forma que espera el Core).</summary>
public sealed record SolicitudPortal
{
    public required string Tipo { get; init; }                  // Certificacion | Renovacion | Duplicado
    public string? Cedula { get; init; }                        // de la persona con discapacidad (vacía si es menor sin cédula)
    public string? ActaNacimiento { get; init; }
    public required string Nombres { get; init; }
    public required string Apellidos { get; init; }
    public DateOnly? FechaNacimiento { get; init; }
    public int? Sexo { get; init; }
    public string? Telefono { get; init; }
    public string? Correo { get; init; }
    public string CanalNotificacion { get; init; } = "Correo";
    public string? Provincia { get; init; }
    public string? Municipio { get; init; }
    public string? Sector { get; init; }
    public string? Direccion { get; init; }
    public string? CedulaTutor { get; init; }
    public string? VinculoTutor { get; init; }
    public string? Establecimiento { get; init; }
    public string? NumeroCertificadoMedico { get; init; }
    public DateOnly? FechaCertificadoMedico { get; init; }
    public string? CodigoCie10 { get; init; }
    public int? CentroPreferidoId { get; init; }
    public DateTimeOffset? FechaCitaPreferida { get; init; }
    public string? Apoyos { get; init; }
    public bool VisitaDomiciliaria { get; init; }
    public string? MotivoDuplicado { get; init; }
    public bool ConsentimientoDatos { get; init; }
}

/// <summary>Ayuda técnica (cédula) o certificación de accesibilidad (RNC).</summary>
public sealed record SolicitudServicioPortal(string Tipo, string Documento, string NombreSolicitante, string Correo, string Resumen, string Detalle);
