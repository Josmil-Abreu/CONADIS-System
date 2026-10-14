namespace CONADIS.Integracion.Negocio.Contratos;

/// <summary>Cliente autenticado con su API key (WEB, CAJA).</summary>
public sealed record ClienteApi(int ClienteId, string Codigo, string Nombre);

/// <summary>Cuerpo de cualquier respuesta de error de la API de Integración.</summary>
/// <param name="Codigo">Código estable para programas (ver <see cref="CodigosError"/>).</param>
/// <param name="Mensaje">Texto pensado para mostrarse al usuario final.</param>
public sealed record ErrorApiDto(string Codigo, string Mensaje);

public static class CodigosError
{
    public const string Validacion = "VALIDACION";
    public const string NoEncontrado = "NO_ENCONTRADO";
    public const string ReglaNegocio = "REGLA_NEGOCIO";
    public const string NoDisponible = "NO_DISPONIBLE";
    public const string NoAutorizado = "NO_AUTORIZADO";
    public const string Conflicto = "CONFLICTO";
    public const string ErrorInterno = "ERROR_INTERNO";
}

/// <summary>
/// Respuesta de una consulta a un servicio externo. Si el servicio estaba caído y se
/// respondió con la última copia guardada, <see cref="DesdeCache"/> = true y
/// <see cref="Vencido"/> dice si esa copia ya había pasado su vigencia.
/// </summary>
public sealed record ResultadoConsulta<T>(T Valor, bool DesdeCache, bool Vencido, DateTime ObtenidoEn);

/// <summary>Estado de salud de Integración (GET /api/salud).</summary>
public sealed record SaludDto(string Modulo, bool BaseDatosDisponible, IReadOnlyList<ServicioDto> Servicios, DateTime FechaServidor);

/// <summary>Códigos de los servicios de destino (tabla snd.Servicio).</summary>
public static class CodigosServicio
{
    public const string Core = "CORE";
    public const string Jce = "JCE";
    public const string Msp = "MSP";
    public const string Dgii = "DGII";
    public const string Firma = "FIRMA";
    public const string Pasarela = "PASARELA";

    public static readonly string[] Todos = [Core, Jce, Msp, Dgii, Firma, Pasarela];
}

/// <summary>Servicio de destino y el estado de su cortacircuito.</summary>
public sealed record ServicioDto(
    string Codigo,
    string Nombre,
    string? UrlBase,
    bool EsSimulado,
    string EstadoCircuito,           // Cerrado | Abierto | SemiAbierto
    int FallosConsecutivos,
    int UmbralFallos,
    int SegundosApertura,
    DateTime? AbiertoHasta,
    DateTime? UltimoExito,
    DateTime? UltimoFallo,
    bool Activo);

public sealed record ParametroDto(string Clave, string Valor, string Descripcion);

public sealed record ClienteResumenDto(int ClienteId, string Codigo, string Nombre, bool Activo, DateTime FechaCreacion, DateTime? UltimoUso);

/// <summary>Resultado de dar de alta o rotar un cliente. La clave en claro solo se ve esta única vez.</summary>
public sealed record ClienteRegistradoDto(int ClienteId, string Codigo, string ApiKey);

public sealed record LogDto(int Id, DateTime Fecha, string Hilo, string Nivel, string Logger, string Mensaje, string? Excepcion);

public sealed record BitacoraDto(
    long BitacoraId, DateTime Fecha, Guid? CorrelacionId, string? ClienteCodigo, string? ServicioCodigo,
    string Direccion, string Operacion, int? CodigoResultado, bool Exitoso, int DuracionMs, string? Detalle);

public static class Direcciones
{
    public const string Entrante = "Entrante";
    public const string Saliente = "Saliente";
}
