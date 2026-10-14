namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>El sistema cliente no tiene el ámbito o el convenio vigente para la operación (HTTP 403).</summary>
public sealed class AccesoDenegadoException(string mensaje) : Exception(mensaje);

/// <summary>
/// Un servicio externo (o el Core) no respondió tras los reintentos, o su cortacircuito está abierto (HTTP 503).
/// Quien llama puede reintentar más tarde; los procesos diferidos ya quedan en la cola.
/// </summary>
public sealed class ServicioNoDisponibleException(string servicio, string mensaje) : Exception(mensaje)
{
    public string Servicio { get; } = servicio;
}
