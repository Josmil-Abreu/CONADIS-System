using CONADIS.Caja.Negocio.Datos;
using CONADIS.Caja.Negocio.Modelos;
using CONADIS.Caja.Negocio.Seguridad;
using Microsoft.Extensions.Logging;

namespace CONADIS.Caja.Negocio.Servicios;

/// <summary>Administración de cajeros y supervisores. Solo el rol Administrador (lo valida también el SP).</summary>
public sealed class ServicioUsuariosCaja(EjecutorSp db, ILogger<ServicioUsuariosCaja> log)
{
    public static readonly string[] Roles = [RolesCaja.Cajero, RolesCaja.Supervisor, RolesCaja.Administrador];

    private static void ExigirAdmin(UsuarioCaja admin)
    {
        if (admin.Rol != RolesCaja.Administrador)
            throw new ReglaNegocioException(58030, "Solo el administrador de caja puede gestionar usuarios.");
    }

    /// <summary>Mínimo 8 caracteres, con letras y números.</summary>
    public static void ValidarContrasena(string? c)
    {
        if (string.IsNullOrEmpty(c) || c.Length < 8 || !c.Any(char.IsLetter) || !c.Any(char.IsDigit))
            throw new ReglaNegocioException(58038, "La contraseña debe tener al menos 8 caracteres, con letras y números.");
    }

    public Task<List<UsuarioCajaDetalle>> ListarAsync(UsuarioCaja admin, CancellationToken ct = default)
    {
        ExigirAdmin(admin);
        return db.ConsultarListaAsync("seg.ppUsuario_Listar", f => new UsuarioCajaDetalle(f.Entero("UsuarioId"), f.Texto("Username"),
            f.Texto("NombreCompleto"), f.Texto("Rol"), f.Booleano("Activo"), f.FechaHoraONula("BloqueadoHasta"), f.FechaHoraONula("UltimoAcceso")), ct: ct);
    }

    public async Task<int> GuardarAsync(UsuarioCaja admin, DatosUsuarioCaja d, CancellationToken ct = default)
    {
        ExigirAdmin(admin);
        if (!Roles.Contains(d.Rol)) throw new ReglaNegocioException(58031, "Rol no válido.");
        string? hash = null;
        if (d.UsuarioId is null || !string.IsNullOrEmpty(d.Contrasena))
        {
            ValidarContrasena(d.Contrasena);
            hash = HasherContrasena.Hashear(d.Contrasena!);
        }
        var f = (await db.ConsultarAsync("seg.ppUsuario_Guardar",
        [
            Parametro.Entero("@AdminId", admin.UsuarioId), Parametro.Entero("@UsuarioId", d.UsuarioId),
            Parametro.Texto("@Username", d.Username?.Trim(), 50), Parametro.Texto("@NombreCompleto", d.NombreCompleto?.Trim(), 150),
            Parametro.Texto("@Rol", d.Rol, 20), Parametro.Booleano("@Activo", d.Activo), Parametro.TextoAnsi("@PasswordHash", hash, 200)
        ], ct)).Rows[0];
        var id = f.Entero("UsuarioId");
        log.LogInformation("Usuario de caja {Usuario} guardado por {Admin}", d.Username, admin.Username);
        return id;
    }

    public async Task DesbloquearAsync(UsuarioCaja admin, int usuarioId, CancellationToken ct = default)
    {
        ExigirAdmin(admin);
        await db.EjecutarAsync("seg.ppUsuario_Desbloquear",
            [Parametro.Entero("@AdminId", admin.UsuarioId), Parametro.Entero("@UsuarioId", usuarioId)], ct);
    }
}
