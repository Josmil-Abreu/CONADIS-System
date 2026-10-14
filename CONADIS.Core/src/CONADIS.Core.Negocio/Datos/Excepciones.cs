namespace CONADIS.Core.Negocio.Datos;

/// <summary>
/// La base de datos no responde (servidor apagado, red caída o tiempo de espera agotado).
/// Quien la recibe decide cómo degradarse: por ejemplo, la API responde 503 y
/// Integración guarda el mensaje en su cola para reintentarlo.
/// </summary>
public sealed class BaseDatosNoDisponibleException(string mensaje, Exception? interna = null)
    : Exception(mensaje, interna);

/// <summary>
/// Un Stored Procedure rechazó la operación por una regla de negocio
/// (THROW 50001..59999 con un mensaje pensado para el usuario).
/// El mensaje se puede mostrar tal cual en pantalla.
/// </summary>
public sealed class ReglaNegocioException(int codigo, string mensaje, Exception? interna = null)
    : Exception(mensaje, interna)
{
    public int Codigo { get; } = codigo;
}

/// <summary>Cualquier otro error de base de datos. El detalle técnico va al log, nunca a la pantalla (RINV-10).</summary>
public sealed class ErrorBaseDatosException(string mensaje, Exception? interna = null)
    : Exception(mensaje, interna);