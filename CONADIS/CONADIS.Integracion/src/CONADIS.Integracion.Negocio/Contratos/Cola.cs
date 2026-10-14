namespace CONADIS.Integracion.Negocio.Contratos;

public static class EstadosMensaje
{
    public const string Pendiente = "Pendiente";
    public const string EnProceso = "EnProceso";
    public const string Entregado = "Entregado";
    public const string Muerto = "Muerto";
}

/// <summary>Tipos de mensaje que entiende el procesador de la cola.</summary>
public static class TiposMensaje
{
    public const string PagoRegistrar = "Pago.Registrar";
    public const string EcfEnviar = "Ecf.Enviar";
    public const string EventoCaja = "Evento.Caja";
}

/// <summary>Mensaje tomado de la cola por un trabajador (fila de snd.ppMensaje_TomarLote).</summary>
public sealed record MensajeColaDto(
    long MensajeId,
    Guid MensajeGuid,
    string ServicioCodigo,
    string Tipo,
    string Payload,
    int Intentos,
    int MaxIntentos,
    string? ClaveIdempotencia,
    Guid? CorrelacionId,
    DateTime FechaCreacion);

public sealed record MensajeEncoladoDto(long MensajeId, Guid MensajeGuid, bool EraDuplicado);

public sealed record MensajeDetalleDto(
    long MensajeId,
    Guid MensajeGuid,
    string ServicioCodigo,
    string Tipo,
    string Estado,
    int Intentos,
    int MaxIntentos,
    DateTime ProximoIntento,
    string? UltimoError,
    string? RespuestaJson,
    Guid? CorrelacionId,
    DateTime FechaCreacion,
    DateTime? FechaEntrega);

public sealed record MensajeResumenDto(
    long MensajeId,
    Guid MensajeGuid,
    string ServicioCodigo,
    string? ClienteCodigo,
    string Tipo,
    string Estado,
    int Intentos,
    int MaxIntentos,
    DateTime ProximoIntento,
    string? UltimoError,
    DateTime FechaCreacion,
    DateTime? FechaEntrega);

/// <summary>Lo que pasó en una vuelta del procesador de la cola.</summary>
public sealed record ResumenLoteDto(int Tomados, int Entregados, int Reintentar, int Muertos)
{
    public static readonly ResumenLoteDto Vacio = new(0, 0, 0, 0);
}

public sealed record ResumenMantenimientoDto(int MensajesBorrados, int ClavesIdempotenciaBorradas, int EntradasCacheBorradas);

/* ------------------------------- Idempotencia ------------------------------- */

public static class ResultadosIdempotencia
{
    /// <summary>Primera vez: procesar y luego llamar a Completar.</summary>
    public const string Nuevo = "Nuevo";
    /// <summary>Ya se procesó: devolver la respuesta guardada sin procesar otra vez.</summary>
    public const string Repetido = "Repetido";
    /// <summary>La primera petición aún no termina: responder 409.</summary>
    public const string EnProceso = "EnProceso";
    /// <summary>Misma clave con otro cuerpo: error del cliente, responder 422.</summary>
    public const string Conflicto = "Conflicto";
}

public sealed record ResultadoIdempotencia(string Resultado, int? CodigoHttp, string? RespuestaJson);
