namespace CONADIS.Integracion.Negocio.Datos;

/// <summary>
/// La base de datos no responde (servidor apagado, red caída o tiempo de espera agotado).
/// </summary>
public sealed class BaseDatosNoDisponibleException(string mensaje, Exception? interna = null)
    : Exception(mensaje, interna);

/// <summary>
/// Un procedimiento almacenado rechazó la operación por una regla de negocio
/// (los SP lanzan THROW 50001..59999 con un mensaje pensado para el usuario).
/// </summary>
public sealed class ReglaNegocioException(int codigo, string mensaje, Exception? interna = null)
    : Exception(mensaje, interna)
{
    public int Codigo { get; } = codigo;
}

/// <summary>Cualquier otro error de base de datos. Su detalle técnico va al log, no a la pantalla.</summary>
public sealed class ErrorBaseDatosException(string mensaje, Exception? interna = null)
    : Exception(mensaje, interna);
