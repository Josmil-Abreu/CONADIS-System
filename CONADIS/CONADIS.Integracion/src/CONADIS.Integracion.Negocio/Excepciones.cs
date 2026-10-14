namespace CONADIS.Integracion.Negocio;

/// <summary>Los datos recibidos no son válidos (cédula mal formada, monto negativo...). La API responde 400.</summary>
public sealed class SolicitudInvalidaException(string mensaje) : Exception(mensaje);

/// <summary>Lo que se buscó no existe. La API responde 404.</summary>
public sealed class RecursoNoEncontradoException(string mensaje) : Exception(mensaje);

/// <summary>
/// Un servicio de destino (Core, JCE, MSP, DGII, firma, pasarela) no respondió, respondió con
/// un error técnico o tiene el cortacircuito abierto. Es la señal para usar la caché o la cola.
/// La API responde 503.
/// </summary>
public sealed class ServicioExternoNoDisponibleException(string servicio, string mensaje, Exception? interna = null)
    : Exception(mensaje, interna)
{
    public string Servicio { get; } = servicio;
}

/// <summary>
/// El servicio de destino SÍ respondió, pero rechazó la operación por una regla suya
/// (tarjeta declinada, comprobante rechazado por la DGII...). Reintentar no sirve.
/// La API responde 422.
/// </summary>
public sealed class RechazoServicioExternoException(string servicio, string mensaje)
    : Exception(mensaje)
{
    public string Servicio { get; } = servicio;
}
