namespace CONADIS.Core.Negocio.Modelos;

/// <summary>Datos del usuario autenticado que se guardan en la sesión (cookie) de la aplicación interna.</summary>
public sealed record UsuarioSesion(
    int UsuarioId,
    string Username,
    string NombreCompleto,
    string Correo,
    string Rol,
    string? Disciplina,
    int? CentroId);

public sealed record ResultadoLogin(bool Exito, string? Mensaje, UsuarioSesion? Usuario)
{
    public static ResultadoLogin Correcto(UsuarioSesion usuario) => new(true, null, usuario);
    public static ResultadoLogin Fallido(string mensaje) => new(false, mensaje, null);
}

public sealed record Rol(int RolId, string Nombre, string Descripcion);

public sealed record UsuarioResumen(
    int UsuarioId,
    string Username,
    string NombreCompleto,
    string Correo,
    string Rol,
    string? Disciplina,
    string? Centro,
    bool Activo,
    DateTimeOffset? BloqueadoHasta,
    DateTimeOffset? UltimoAcceso);

public sealed record UsuarioDetalle(
    int UsuarioId,
    string Username,
    string NombreCompleto,
    string Correo,
    int RolId,
    string Rol,
    string? Disciplina,
    int? CentroId,
    bool Activo,
    DateTimeOffset? BloqueadoHasta);

/// <summary>Datos para crear (UsuarioId = null) o editar un usuario. La contraseña solo se usa al crear.</summary>
public sealed record DatosUsuario(
    int? UsuarioId,
    string Username,
    string? Contrasena,
    string NombreCompleto,
    string Correo,
    int RolId,
    string? Disciplina,
    int? CentroId,
    bool Activo);