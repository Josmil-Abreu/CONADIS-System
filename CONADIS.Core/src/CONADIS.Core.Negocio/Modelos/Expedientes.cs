namespace CONADIS.Core.Negocio.Modelos;

/// <summary>Todo lo que llega para registrar una solicitud (RF-CORE-01), presencial o desde el portal.</summary>
public sealed record NuevaSolicitud
{
    public required string Canal { get; init; }                 // Web | Presencial
    public required string Tipo { get; init; }                  // Certificacion | Renovacion | Duplicado
    public string? CedulaCuenta { get; init; }                  // quien la presenta (titular o tutor)
    public string? Cedula { get; init; }                        // de la persona con discapacidad
    public string? ActaNacimiento { get; init; }                // menores sin cédula
    public required string Nombres { get; init; }
    public required string Apellidos { get; init; }
    public DateOnly? FechaNacimiento { get; init; }
    public int? Sexo { get; init; }                             // 1 = Masculino, 2 = Femenino
    public required string EstadoIdentidad { get; init; }       // Validada | Pendiente (resultado de la JCE)
    public string? Telefono { get; init; }
    public string? Correo { get; init; }
    public string CanalNotificacion { get; init; } = "Correo";  // Correo | SMS | Portal
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
    public string EstadoDiagnostico { get; init; } = "ValidacionManual"; // resultado del MSP
    public int? CentroPreferidoId { get; init; }
    public DateTimeOffset? FechaCitaPreferida { get; init; }
    public string? Apoyos { get; init; }
    public bool VisitaDomiciliaria { get; init; }
    public string? MotivoDuplicado { get; init; }
    public bool ConsentimientoDatos { get; init; }
}

public sealed record SolicitudRegistrada(string NumeroTramite, string NumeroExpediente, DateTimeOffset FechaRegistro, string? NumeroOrden);

public sealed record ExpedienteResumen(int ExpedienteId, string NumeroExpediente, string? Cedula, string NombreCompleto,
    string Estado, string? TipoDiscapacidad, string? GradoDiscapacidad, string? Centro, string EstadoIdentidad,
    DateTimeOffset FechaActualizacion);

public sealed record ExpedienteCabecera(int ExpedienteId, string NumeroExpediente, string Estado, string? TipoDiscapacidad,
    string? GradoDiscapacidad, DateOnly? FechaVencimiento, int? CentroId, string? Centro, DateTimeOffset FechaCreacion,
    DateTimeOffset FechaActualizacion, DateTimeOffset? FechaRechazo, int PersonaId, string? Cedula, string? ActaNacimiento,
    string Nombres, string Apellidos, DateOnly? FechaNacimiento, int? Sexo, string? Telefono, string? Correo,
    string CanalNotificacion, string? Provincia, string? Municipio, string? Sector, string? Direccion,
    string EstadoIdentidad, DateTimeOffset? FechaValidacionJce);

public sealed record SolicitudExpediente(int SolicitudId, string NumeroTramite, string Tipo, string Canal, string Estado,
    string? Establecimiento, string? NumeroCertificadoMedico, string? CodigoCie10, string EstadoDiagnostico, string? Apoyos,
    bool VisitaDomiciliaria, string? MotivoDuplicado, DateTimeOffset? FechaCitaPreferida, string? CentroPreferido,
    DateTimeOffset FechaRegistro, string UsuarioRegistro);

public sealed record CambioEstado(string? EstadoAnterior, string EstadoNuevo, string Unidad, string Usuario,
    string? Comentario, DateTimeOffset Fecha);

public sealed record CitaExpediente(int CitaId, string Centro, DateTimeOffset FechaHora, bool Domiciliaria, string Estado);

public sealed record EvaluacionResumen(int EvaluacionId, DateTimeOffset FechaEvaluacion, string Estado, decimal? PuntajeWhodas,
    string? GradoPropuesto, string? TipoPropuesto, string? VersionBaremo, string? Dictamen);

public sealed record CarnetExpediente(int CarnetId, string NumeroCarnet, string NumeroCertificado, DateOnly? FechaEmision,
    DateOnly? FechaVencimiento, string Estado, string? MotivoAnulacion);

public sealed record ApelacionExpediente(int ApelacionId, string Motivo, string Estado, string? Resolucion,
    DateTimeOffset FechaRegistro, DateTimeOffset? FechaResolucion);

public sealed record OrdenExpediente(string NumeroOrden, string Concepto, decimal Total, string Estado,
    DateTimeOffset FechaCreacion, DateTimeOffset? FechaPago, string? NumeroComprobante);

/// <summary>Vista consolidada del Expediente Único (lo que necesita el comité y el analista).</summary>
public sealed record ExpedienteCompleto(
    ExpedienteCabecera Cabecera,
    IReadOnlyList<SolicitudExpediente> Solicitudes,
    IReadOnlyList<CambioEstado> Historial,
    IReadOnlyList<CitaExpediente> Citas,
    IReadOnlyList<EvaluacionResumen> Evaluaciones,
    IReadOnlyList<CarnetExpediente> Carnets,
    IReadOnlyList<ApelacionExpediente> Apelaciones,
    IReadOnlyList<OrdenExpediente> Ordenes);

/// <summary>Lo que ve el ciudadano de un trámite (RF-WEB-02). Sin datos internos.</summary>
public sealed record TramiteCiudadano(string Numero, string? NumeroExpediente, string Cedula, string NombreCompleto,
    string Estado, DateTimeOffset FechaRegistro, DateTimeOffset FechaActualizacion, string? TipoDiscapacidad,
    string? GradoDiscapacidad, string? NumeroCarnet, string? CentroValoracion, DateTimeOffset? FechaCita,
    string? Observaciones, IReadOnlyList<HitoTramite> Historial);

public sealed record HitoTramite(string Estado, DateTimeOffset Fecha, string? Comentario);

public sealed record TramiteResumen(string Numero, string Tipo, string Estado, DateTimeOffset Actualizado, string Persona);

public sealed record PersonaIdentidadPendiente(int PersonaId, string Cedula, string Nombres, string Apellidos,
    string? NumeroExpediente, DateTimeOffset FechaCreacion, bool RequiereAnalista);

public sealed record CupoDisponible(DateOnly Fecha, int CapacidadDiaria, int CuposLibres);

public sealed record CitaAgenda(int CitaId, DateTimeOffset FechaHora, bool Domiciliaria, string Estado, int ExpedienteId,
    string NumeroExpediente, string NombreCompleto);