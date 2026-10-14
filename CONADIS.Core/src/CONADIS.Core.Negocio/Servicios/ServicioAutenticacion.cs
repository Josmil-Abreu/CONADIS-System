using System.Data;
using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;
using log4net;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>
/// Inicio de sesión del personal interno (RNF-SEG-04):
///  - La contraseña se verifica en C# con PBKDF2 (el SP solo entrega el hash).
///  - 5 intentos fallidos bloquean la cuenta 15 minutos (valores en cat.Parametro).
///  - El mensaje de error no revela si el usuario existe.
/// </summary>
public sealed class ServicioAutenticacion(EjecutorSp db)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioAutenticacion));
    private const string MensajeGenerico = "Usuario o contraseña incorrectos.";

    public async Task<ResultadoLogin> IniciarSesionAsync(string? usuario, string? contrasena, string? ip,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrEmpty(contrasena))
            return ResultadoLogin.Fallido("Escribe tu usuario y tu contraseña.");

        usuario = usuario.Trim();

        var tabla = await db.ConsultarAsync("seg.ppUsuario_ObtenerParaLogin",
            [Parametro.Texto("@Username", usuario, 50)], ct);

        if (tabla.Rows.Count == 0)
        {
            HasherContrasena.SimularVerificacion(contrasena); // mismo tiempo de respuesta que un usuario real
            Log.Warn($"Intento de acceso con usuario inexistente '{usuario}' desde {ip}.");
            return ResultadoLogin.Fallido(MensajeGenerico);
        }

        var fila = tabla.Rows[0];
        var usuarioId = fila.Entero("UsuarioId");

        if (!fila.Booleano("Activo"))
        {
            Log.Warn($"Intento de acceso con la cuenta desactivada '{usuario}' desde {ip}.");
            return ResultadoLogin.Fallido("Tu cuenta está desactivada. Contacta al administrador del sistema.");
        }

        var bloqueadoHasta = fila.FechaHoraONula("BloqueadoHasta");
        if (bloqueadoHasta > DateTimeOffset.Now)
            return ResultadoLogin.Fallido(MensajeBloqueo(bloqueadoHasta.Value));

        if (!HasherContrasena.Verificar(contrasena, fila.Texto("PasswordHash")))
        {
            var resultado = await db.ConsultarAsync("seg.ppUsuario_RegistrarIntentoFallido",
                [Parametro.Entero("@UsuarioId", usuarioId), Parametro.TextoAnsi("@Ip", ip, 45)], ct);

            var nuevoBloqueo = resultado.Rows.Count > 0 ? resultado.Rows[0].FechaHoraONula("BloqueadoHasta") : null;
            Log.Warn($"Contraseña incorrecta para '{usuario}' desde {ip}.");

            return nuevoBloqueo > DateTimeOffset.Now
                ? ResultadoLogin.Fallido(MensajeBloqueo(nuevoBloqueo.Value))
                : ResultadoLogin.Fallido(MensajeGenerico);
        }

        await db.EjecutarAsync("seg.ppUsuario_RegistrarAccesoExitoso",
            [Parametro.Entero("@UsuarioId", usuarioId), Parametro.TextoAnsi("@Ip", ip, 45)], ct);

        Log.Info($"Inicio de sesión de '{usuario}' ({fila.Texto("Rol")}) desde {ip}.");

        return ResultadoLogin.Correcto(new UsuarioSesion(
            usuarioId,
            fila.Texto("Username"),
            fila.Texto("NombreCompleto"),
            fila.Texto("Correo"),
            fila.Texto("Rol"),
            fila.TextoONulo("Disciplina"),
            fila.EnteroONulo("CentroId")));
    }

    /// <summary>
    /// Sesión a partir de un pase de CONADIS.Inicio (la contraseña ya se verificó allí).
    /// Igual que el login, rechaza cuentas desactivadas o bloqueadas. Null si el pase no es válido.
    /// </summary>
    public async Task<UsuarioSesion?> IniciarConPaseAsync(string? pase, string claveTraspaso, string? ip, CancellationToken ct = default)
    {
        var usuario = PaseInicio.Validar(pase, "Core", claveTraspaso);
        if (usuario is null)
        {
            Log.Warn($"Pase de inicio inválido o vencido desde {ip}.");
            return null;
        }

        var tabla = await db.ConsultarAsync("seg.ppUsuario_ObtenerParaLogin", [Parametro.Texto("@Username", usuario, 50)], ct);
        if (tabla.Rows.Count == 0) return null;
        var fila = tabla.Rows[0];
        if (!fila.Booleano("Activo") || fila.FechaHoraONula("BloqueadoHasta") > DateTimeOffset.Now) return null;

        await db.EjecutarAsync("seg.ppUsuario_RegistrarAccesoExitoso",
            [Parametro.Entero("@UsuarioId", fila.Entero("UsuarioId")), Parametro.TextoAnsi("@Ip", ip, 45)], ct);
        Log.Info($"Inicio de sesión de '{usuario}' con pase de CONADIS.Inicio desde {ip}.");
        return new UsuarioSesion(fila.Entero("UsuarioId"), fila.Texto("Username"), fila.Texto("NombreCompleto"), fila.Texto("Correo"),
            fila.Texto("Rol"), fila.TextoONulo("Disciplina"), fila.EnteroONulo("CentroId"));
    }

    private static string MensajeBloqueo(DateTimeOffset hasta)
    {
        var minutos = Math.Max(1, (int)Math.Ceiling((hasta - DateTimeOffset.Now).TotalMinutes));
        return $"Tu cuenta está bloqueada por varios intentos fallidos. Intenta de nuevo en {minutos} minuto(s).";
    }
}