using System.Data;
using System.Net.Mail;
using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>Gestión de usuarios internos y roles (CRUD del Core con control de acceso). Solo Administrador.</summary>
public sealed class ServicioUsuarios(EjecutorSp db)
{
    public Task<List<Rol>> ListarRolesAsync(CancellationToken ct = default)
        => db.ConsultarListaAsync("seg.ppRol_Listar",
            f => new Rol(f.Entero("RolId"), f.Texto("Nombre"), f.Texto("Descripcion")), ct: ct);

    public Task<List<UsuarioResumen>> ListarAsync(string? texto = null, CancellationToken ct = default)
        => db.ConsultarListaAsync("seg.ppUsuario_Listar", f => new UsuarioResumen(
                f.Entero("UsuarioId"), f.Texto("Username"), f.Texto("NombreCompleto"), f.Texto("Correo"),
                f.Texto("Rol"), f.TextoONulo("Disciplina"), f.TextoONulo("Centro"), f.Booleano("Activo"),
                f.FechaHoraONula("BloqueadoHasta"), f.FechaHoraONula("UltimoAcceso")),
            [Parametro.Texto("@Texto", string.IsNullOrWhiteSpace(texto) ? null : texto.Trim(), 100)], ct);

    public Task<UsuarioDetalle?> ObtenerAsync(int usuarioId, CancellationToken ct = default)
        => db.ConsultarUnoAsync("seg.ppUsuario_Obtener", f => new UsuarioDetalle(
                f.Entero("UsuarioId"), f.Texto("Username"), f.Texto("NombreCompleto"), f.Texto("Correo"),
                f.Entero("RolId"), f.Texto("Rol"), f.TextoONulo("Disciplina"), f.EnteroONulo("CentroId"),
                f.Booleano("Activo"), f.FechaHoraONula("BloqueadoHasta")),
            [Parametro.Entero("@UsuarioId", usuarioId)], ct);

    /// <summary>Crea (UsuarioId = null) o modifica un usuario. Devuelve su Id.</summary>
    public async Task<int> GuardarAsync(DatosUsuario datos, ContextoAccion contexto, CancellationToken ct = default)
    {
        Validar(datos);

        string? hash = null;
        if (datos.UsuarioId is null)
        {
            if (HasherContrasena.ValidarFortaleza(datos.Contrasena) is { } error)
                throw new ReglaNegocioException(0, error);
            hash = HasherContrasena.Hashear(datos.Contrasena!);
        }

        var tabla = await db.ConsultarAsync("seg.ppUsuario_Guardar",
        [
            Parametro.Entero("@UsuarioId", datos.UsuarioId),
            Parametro.Texto("@Username", datos.Username.Trim(), 50),
            Parametro.TextoAnsi("@PasswordHash", hash, 200),
            Parametro.Texto("@NombreCompleto", datos.NombreCompleto.Trim(), 150),
            Parametro.Texto("@Correo", datos.Correo.Trim(), 150),
            Parametro.Entero("@RolId", datos.RolId),
            Parametro.Texto("@Disciplina", datos.Disciplina, 20),
            Parametro.Entero("@CentroId", datos.CentroId),
            Parametro.Booleano("@Activo", datos.Activo),
            Parametro.Texto("@UsuarioAccion", contexto.Usuario, 100)
        ], ct);

        return tabla.Rows[0].Entero("UsuarioId");
    }

    public Task CambiarContrasenaAsync(int usuarioId, string nuevaContrasena, ContextoAccion contexto, CancellationToken ct = default)
    {
        if (HasherContrasena.ValidarFortaleza(nuevaContrasena) is { } error)
            throw new ReglaNegocioException(0, error);

        return db.EjecutarAsync("seg.ppUsuario_CambiarContrasena",
        [
            Parametro.Entero("@UsuarioId", usuarioId),
            Parametro.TextoAnsi("@PasswordHash", HasherContrasena.Hashear(nuevaContrasena), 200),
            Parametro.Texto("@UsuarioAccion", contexto.Usuario, 100)
        ], ct);
    }

    public Task DesbloquearAsync(int usuarioId, ContextoAccion contexto, CancellationToken ct = default)
        => db.EjecutarAsync("seg.ppUsuario_Desbloquear",
            [Parametro.Entero("@UsuarioId", usuarioId), Parametro.Texto("@UsuarioAccion", contexto.Usuario, 100)], ct);

    private static void Validar(DatosUsuario d)
    {
        if (string.IsNullOrWhiteSpace(d.Username) || d.Username.Trim().Length < 3 || d.Username.Contains(' '))
            throw new ReglaNegocioException(0, "El nombre de usuario debe tener al menos 3 caracteres y no llevar espacios.");
        if (string.IsNullOrWhiteSpace(d.NombreCompleto))
            throw new ReglaNegocioException(0, "Escribe el nombre completo.");
        if (!MailAddress.TryCreate(d.Correo?.Trim(), out _))
            throw new ReglaNegocioException(0, "El correo no es válido.");
        if (d.Disciplina is not null && !Valores.Disciplinas.Contains(d.Disciplina))
            throw new ReglaNegocioException(0, "La disciplina debe ser Medica, Psicologica o Social.");
    }
}