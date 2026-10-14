using CONADIS.Core.Negocio.Modelos;

namespace CONADIS.Core.Negocio.Seguridad;

/// <summary>El usuario no tiene el rol necesario para la operación (RBAC, RNF-SEG-05).</summary>
public sealed class AccesoDenegadoException(string mensaje) : Exception(mensaje);

/// <summary>
/// Control de acceso por roles dentro de la lógica.
/// Las pantallas y la API también lo comprueban, pero los servicios lo vuelven a exigir:
/// así ninguna interfaz futura puede saltarse la regla por error (defensa en profundidad).
/// </summary>
public static class Autorizacion
{
	public static void Exigir(UsuarioSesion usuario, params string[] rolesPermitidos)
	{
		ArgumentNullException.ThrowIfNull(usuario);
		if (!rolesPermitidos.Contains(usuario.Rol))
			throw new AccesoDenegadoException(
				$"Tu rol ({usuario.Rol}) no tiene permiso para esta operación. Roles permitidos: {string.Join(", ", rolesPermitidos)}.");
	}
}