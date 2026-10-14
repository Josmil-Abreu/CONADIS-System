namespace CONADIS.Core.Negocio.Modelos;

public sealed record EvaluacionDetalle(
    int EvaluacionId, int ExpedienteId, string NumeroExpediente, string NombreCompleto, int? CentroId,
    DateTimeOffset FechaEvaluacion, PuntajesWhodas? Whodas, decimal? PuntajeWhodas, string? GradoPropuesto,
    string? TipoPropuesto, string? VersionBaremo, string Estado, string? Observaciones,
    byte[] RowVersion,                       // se devuelve al guardar el WHODAS (concurrencia optimista)
    string? Dictamen,
    IReadOnlyList<Codificacion> Codificaciones,
    IReadOnlyList<FirmaDisciplina> Firmas);

/// <summary>Puntajes WHODAS 2.0 por dominio (0 a 100).</summary>
public sealed record PuntajesWhodas(decimal Cognicion, decimal Movilidad, decimal Autocuidado,
    decimal Relaciones, decimal Actividades, decimal Participacion);

public sealed record Codificacion(string Codigo, string Componente, string Descripcion, string Disciplina,
    int Calificador1, int? Calificador2, bool EsFacilitador, string? Observacion, string Evaluador);

public sealed record NuevaCodificacion(string Codigo, int Calificador1, int? Calificador2 = null,
    bool EsFacilitador = false, string? Observacion = null);

public sealed record FirmaDisciplina(string Disciplina, string Evaluador, DateTimeOffset FechaFirma);

public sealed record ResultadoWhodas(decimal PuntajeWhodas, string? GradoPropuesto, string VersionBaremo);

public sealed record DictamenPendiente(int EvaluacionId, int ExpedienteId, string NumeroExpediente, string NombreCompleto,
    decimal? PuntajeWhodas, string? GradoPropuesto, string? TipoPropuesto, DateTimeOffset FechaEvaluacion, int Votos);

public sealed record VotoEmitido(string Miembro, string Voto, string? Grado, string? Tipo, string? Motivo, DateTimeOffset FechaVoto);

/// <summary>Voto de un miembro del comité. Grado y tipo nulos = acepta los propuestos.</summary>
public sealed record NuevoVoto(int EvaluacionId, string Voto, string? Grado = null, string? Tipo = null,
    string? Motivo = null, bool Permanente = false, DateOnly? FechaVencimiento = null);

/// <summary>Resultado = Aprobado | Rechazado | PendienteQuorum.</summary>
public sealed record ResultadoVoto(string Resultado, int VotosCoincidentes, int Quorum);

public sealed record ApelacionPendiente(int ApelacionId, int ExpedienteId, string NumeroExpediente, string NombreCompleto,
    string Motivo, DateTimeOffset FechaRegistro);

public sealed record CarnetEmitido(string NumeroCarnet, string NumeroCertificado, DateOnly FechaEmision,
    DateOnly? FechaVencimiento, string CodigoVerificacion);

public sealed record CarnetCiudadano(string NumeroCarnet, string NumeroExpediente, string Cedula, string NombreCompleto,
    string? TipoDiscapacidad, string? GradoDiscapacidad, DateOnly FechaEmision, DateOnly? FechaVencimiento,
    string Estado, string CodigoVerificacion, string NumeroCertificado);

/// <summary>Verificación pública por QR (RINV-02): solo estado, nombre enmascarado y fechas.</summary>
public sealed record VerificacionPublica(string Resultado, string? TitularParcial, DateOnly? FechaEmision, DateOnly? ValidoHasta);

/// <summary>Verificación institucional (RF-INT-03): atributos mínimos autorizados.</summary>
public sealed record VerificacionInstitucional(bool TieneCertificacionVigente, string? TipoDiscapacidad,
    string? GradoDiscapacidad, DateOnly? FechaVencimiento);