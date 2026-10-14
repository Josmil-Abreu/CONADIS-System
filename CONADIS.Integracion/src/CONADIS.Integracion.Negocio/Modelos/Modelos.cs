namespace CONADIS.Integracion.Negocio.Modelos;

/// <summary>Quién hace la operación y desde dónde (para la bitácora y los logs).</summary>
public sealed record ContextoLlamada(
    string Cliente,                 // sistema que llama: Web, Caja, Core, institución, ServicioWindows
    string? Usuario = null,         // persona real detrás, si se conoce (ciudadano:xxx, cajero:yyy)
    string? Proposito = null,
    string? CorrelacionId = null,
    string? Ip = null)
{
    public static ContextoLlamada Sistema(string proceso, string? correlacion = null)
        => new("ServicioWindows", proceso, $"Proceso automático: {proceso}", correlacion);
}

/// <summary>Ámbitos (permisos) de los sistemas cliente, tal como están en seg.ClienteApi.Ambitos.</summary>
public static class Ambitos
{
    public const string Portal = "portal";
    public const string Caja = "caja";
    public const string Firma = "firma";
    public const string Verificar = "discapacidad.verificar";
}

/// <summary>Sistema cliente autenticado con su API key.</summary>
public sealed record ClienteAutenticado(int ClienteId, string Nombre, string Tipo, IReadOnlyList<string> Ambitos,
    bool VerTipoGrado, DateOnly? ConvenioHasta, bool ConvenioVigente)
{
    public bool TieneAmbito(string ambito) => Ambitos.Contains(ambito, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Servicios externos con los que habla Integración (mismos valores que los CHECK de SQL).</summary>
public static class ServiciosExternos
{
    public const string Jce = "JCE";
    public const string Msp = "MSP";
    public const string Dgii = "DGII";
    public const string Firma = "Firma";
    public const string Pasarela = "Pasarela";
    public const string Core = "Core";
    public const string Verificacion = "Verificacion";
    public const string Correo = "Correo";
    public const string Sms = "SMS";
}

// ------------------------------------------------------------------ cola de mensajes

public sealed record MensajeCola(long MensajeId, string Tipo, string Destino, string Carga, int Intentos, string? CorrelacionId);

public sealed record ResultadoEncolar(long MensajeId, bool Encolado);

public sealed record ResultadoFallo(string Estado, int Intentos, DateTimeOffset ProximoIntento);

public sealed record MensajeResumen(long MensajeId, string Tipo, string Destino, string Estado, int Intentos, int MaxIntentos,
    DateTimeOffset ProximoIntento, string? BloqueadoPor, string? UltimoError, string? CorrelacionId,
    DateTimeOffset FechaCreacion, DateTimeOffset? FechaCompletado);

public sealed record ResumenCola(string Destino, string Estado, int Cantidad, DateTimeOffset MasAntiguo);

public static class TiposMensaje
{
    /// <summary>Entregar al Core un pago recibido de la Caja o del portal.</summary>
    public const string CoreRegistrarPago = "Core.RegistrarPago";
}

public static class EstadosMensaje
{
    public const string Pendiente = "Pendiente";
    public const string EnProceso = "EnProceso";
    public const string Completado = "Completado";
    public const string Fallido = "Fallido";
}

// ------------------------------------------------------------------ pagos

public sealed record PagoRecibido(Guid TransaccionId, string Origen, string? NumeroOrden, string NumeroComprobante,
    decimal Total, string Estado, string? Detalle, DateTimeOffset FechaRecepcion, DateTimeOffset? FechaConfirmacion);

public sealed record ResultadoRecepcionPago(PagoRecibido Pago, bool EsNuevo);

public static class EstadosPagoRecibido
{
    public const string Recibido = "Recibido";
    public const string Aplicado = "Aplicado";
    public const string Duplicado = "Duplicado";
    public const string Rechazado = "Rechazado";
}

/// <summary>
/// Pago que llega de la Caja o del portal. Tiene la misma forma que el NuevoPago del Core,
/// porque Integración se lo reenvía tal cual (por la cola) a POST /api/v1/pagos del Core.
/// </summary>
public sealed record PagoEntrante(
    Guid TransaccionId,
    string Origen,
    string NumeroComprobante,
    string TasaCodigo,
    int Cantidad,
    decimal Total,
    string MedioPago,
    string Documento,
    string NombrePagador,
    string? NumeroOrden,
    string? Referencia,
    string? Caja,
    string? Cajero,
    DateTimeOffset FechaPago);

// ------------------------------------------------------------------ idempotencia

public sealed record ResultadoIdempotencia(string Resultado, int? CodigoHttp, string? Respuesta);

public static class ResultadosIdempotencia
{
    public const string Nueva = "Nueva";
    public const string Repetida = "Repetida";
    public const string EnProceso = "EnProceso";
    public const string Conflicto = "Conflicto";
}

// ------------------------------------------------------------------ bitácora, caché, circuito, alertas

/// <summary>Una fila de la bitácora de interoperabilidad.</summary>
public sealed record EntradaBitacora(
    string Servicio,
    string Operacion,
    string Direccion,
    bool Exito,
    string Resultado,
    ContextoLlamada Contexto,
    string? ReferenciaHash = null,
    string? CodigoRespuesta = null,
    int? LatenciaMs = null,
    bool DesdeCache = false,
    string? Detalle = null);

public sealed record RegistroBitacora(long BitacoraId, DateTimeOffset Fecha, string Servicio, string Operacion, string Direccion,
    string? Cliente, string? Usuario, string? Proposito, bool Exito, string Resultado, string? CodigoRespuesta,
    int? LatenciaMs, bool DesdeCache, string? CorrelacionId, string? Detalle);

public sealed record EstadisticaServicio(string Servicio, int Total, int Exitosas, int Fallidas, int DesdeCache,
    decimal PorcentajeExito, decimal? LatenciaPromedioMs, int? LatenciaMaximaMs, decimal? LatenciaP95Ms);

public sealed record EntradaCache(string Resultado, string? Datos, DateTimeOffset FechaConsulta, DateTimeOffset VenceEn);

public sealed record EstadoCircuito(bool Permitido, string Estado, DateTimeOffset? AbiertoHasta);

public sealed record CircuitoDetalle(string Servicio, string Estado, int FallosConsecutivos, DateTimeOffset? AbiertoHasta,
    DateTimeOffset UltimoCambio, string? UltimoError);

public sealed record Alerta(long AlertaId, DateTimeOffset Fecha, string Severidad, string Servicio, string Mensaje);

public static class Severidades
{
    public const string Alta = "Alta";
    public const string Media = "Media";
    public const string Baja = "Baja";
}

public sealed record ParametroIntegracion(string Clave, string Valor, string Descripcion, DateTimeOffset FechaCambio, string UsuarioCambio);

// ------------------------------------------------------------------ operadores

public sealed record OperadorSesion(int OperadorId, string Username, string NombreCompleto, string Rol);

public sealed record ResultadoLoginOperador(bool Exito, string? Mensaje, OperadorSesion? Operador)
{
    public static ResultadoLoginOperador Correcto(OperadorSesion o) => new(true, null, o);
    public static ResultadoLoginOperador Fallido(string mensaje) => new(false, mensaje, null);
}

public static class RolesOperador
{
    public const string Administrador = "Administrador";
    public const string Monitor = "Monitor";
}
