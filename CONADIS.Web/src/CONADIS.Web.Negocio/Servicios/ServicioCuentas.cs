using System.Data;
using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Externos;
using CONADIS.Web.Negocio.Modelos;
using CONADIS.Web.Negocio.Seguridad;
using log4net;

namespace CONADIS.Web.Negocio.Servicios;

/// <summary>
/// Cuentas del Portal Ciudadano (RNF-SEG-03/04):
///  - Registro: cédula válida, identidad confirmada con la JCE (vía Integración), contraseña de 12+ caracteres.
///  - Inicio de sesión con cédula o correo; 5 intentos fallidos bloquean 15 minutos; mensajes que no revelan
///    si la cuenta existe.
///  - Verificación de correo y recuperación de contraseña con enlaces de un solo uso.
/// </summary>
public sealed class ServicioCuentas(EjecutorSp db, ClienteSnd snd)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioCuentas));
    private const string MensajeGenerico = "Cédula, correo o contraseña incorrectos.";
    public const int LongitudMinimaContrasena = 12;

    public async Task<ResultadoRegistro> RegistrarAsync(DatosRegistro d, string? ip, CancellationToken ct = default)
    {
        var cedula = ValidadorCedula.Normalizar(d.Cedula);
        if (!ValidadorCedula.EsValida(cedula)) throw new ReglaNegocioException(0, "La cédula no es válida. Revise los 11 dígitos.");
        if (!ValidacionesPortal.CorreoValido(d.Correo)) throw new ReglaNegocioException(0, "El correo electrónico no es válido.");
        if (!string.IsNullOrWhiteSpace(d.Telefono) && !ValidacionesPortal.TelefonoValido(d.Telefono))
            throw new ReglaNegocioException(0, "El teléfono debe tener 10 dígitos y empezar por 809, 829 u 849.");
        if (HasherContrasena.ValidarFortaleza(d.Contrasena, LongitudMinimaContrasena) is { } error) throw new ReglaNegocioException(0, error);
        if (d.Contrasena != d.ConfirmacionContrasena) throw new ReglaNegocioException(0, "Las contraseñas no coinciden.");
        if (!d.AceptaTerminos) throw new ReglaNegocioException(0, "Debe aceptar los términos de uso y la política de privacidad.");

        // Identidad con la JCE (a través de Integración). Si la JCE no responde, la cuenta queda "Pendiente".
        // Sin conexión con Integración la cuenta se crea igual ("Pendiente") y la validación queda en la bandeja de salida.
        IdentidadJce identidad;
        var sinConexion = false;
        try
        {
            identidad = await snd.GetAsync<IdentidadJce>($"api/v1/portal/identidad/{cedula}", new ContextoPortal(cedula, ip), ct)
                        ?? new IdentidadJce("Pendiente", null, null, "Sin respuesta");
        }
        catch (SndNoDisponibleException)
        {
            identidad = new IdentidadJce("Pendiente", null, null, "Sin conexión");
            sinConexion = true;
        }
        if (identidad.Estado == "Invalida")
            throw new ReglaNegocioException(0, $"La Junta Central Electoral no valida esta cédula: {identidad.Motivo}");

        var nombre = identidad.Estado == "Validada" ? $"{identidad.Nombres} {identidad.Apellidos}".Trim() : "Ciudadano (identidad en validación)";
        var estado = identidad.Estado == "Validada" ? "Validada" : "Pendiente";
        var (token, hash) = TokenSeguro.Generar();

        var tabla = await db.ConsultarAsync("web.ppCuenta_Crear",
        [
            Parametro.TextoAnsi("@Cedula", cedula, 11), Parametro.Texto("@Correo", d.Correo.Trim(), 150),
            Parametro.Texto("@Telefono", string.IsNullOrWhiteSpace(d.Telefono) ? null : d.Telefono.Trim(), 20),
            Parametro.Texto("@NombreCompleto", nombre, 200), Parametro.TextoAnsi("@PasswordHash", HasherContrasena.Hashear(d.Contrasena), 200),
            Parametro.Texto("@EstadoIdentidad", estado, 20), Parametro.TextoAnsi("@TokenVerificacion", hash, 64),
            Parametro.TextoAnsi("@Ip", ip, 45)
        ], ct);

        var cuentaId = tabla.Rows[0].Entero("CuentaId");
        if (estado == "Pendiente")
            await new BandejaSalida(db, snd).EncolarAsync(cuentaId, new PeticionPortal("ValidarIdentidad", "GET",
                $"api/v1/portal/identidad/{cedula}", new { }, $"identidad-{cuentaId}", "Validación de identidad con la JCE"), ip, ct);

        Log.Info($"Cuenta del portal creada (identidad {estado}{(sinConexion ? ", sin conexión: se validará después" : "")}).");
        return new ResultadoRegistro(cuentaId, nombre, estado, token);
    }

    public async Task<ResultadoLoginCiudadano> IniciarSesionAsync(string? usuario, string? contrasena, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrEmpty(contrasena))
            return ResultadoLoginCiudadano.Fallido("Escriba su cédula (o correo) y su contraseña.");

        var tabla = await db.ConsultarAsync("web.ppCuenta_ObtenerParaLogin", [Parametro.Texto("@Usuario", usuario.Trim(), 150)], ct);
        if (tabla.Rows.Count == 0)
        {
            HasherContrasena.SimularVerificacion(contrasena);
            await RegistrarEventoAsync(null, "AccesoUsuarioInexistente", ip, null, ct);
            return ResultadoLoginCiudadano.Fallido(MensajeGenerico);
        }

        var f = tabla.Rows[0];
        var cuentaId = f.Entero("CuentaId");
        if (!f.Booleano("Activa")) return ResultadoLoginCiudadano.Fallido("La cuenta está desactivada. Contacte a CONADIS.");
        if (f.FechaHoraONula("BloqueadoHasta") is { } hasta && hasta > DateTimeOffset.Now)
            return ResultadoLoginCiudadano.Fallido($"Por seguridad, la cuenta está bloqueada hasta las {hasta.ToLocalTime():HH:mm}.");

        if (!HasherContrasena.Verificar(contrasena, f.Texto("PasswordHash")))
        {
            var r = await db.ConsultarAsync("web.ppCuenta_RegistrarIntentoFallido",
                [Parametro.Entero("@CuentaId", cuentaId), Parametro.TextoAnsi("@Ip", ip, 45)], ct);
            var bloqueo = r.Rows.Count > 0 ? r.Rows[0].FechaHoraONula("BloqueadoHasta") : null;
            return bloqueo > DateTimeOffset.Now
                ? ResultadoLoginCiudadano.Fallido($"Demasiados intentos. La cuenta quedó bloqueada hasta las {bloqueo.Value.ToLocalTime():HH:mm}.")
                : ResultadoLoginCiudadano.Fallido(MensajeGenerico);
        }

        await db.EjecutarAsync("web.ppCuenta_RegistrarAccesoExitoso", [Parametro.Entero("@CuentaId", cuentaId), Parametro.TextoAnsi("@Ip", ip, 45)], ct);
        return ResultadoLoginCiudadano.Correcto(new CuentaSesion(cuentaId, f.Texto("Cedula"), f.Texto("NombreCompleto"), f.Texto("Correo"),
            f.Texto("EstadoIdentidad"), f.Booleano("CorreoVerificado"), f.Booleano("AltoContraste"), f.Entero("TamanoTexto")));
    }

    /// <summary>Sesión a partir de un pase de CONADIS.Inicio (la contraseña ya se verificó allí). Null si no es válido.</summary>
    public async Task<CuentaSesion?> IniciarConPaseAsync(string? pase, string claveTraspaso, string? ip, CancellationToken ct = default)
    {
        var cedula = PaseInicio.Validar(pase, "Web", claveTraspaso);
        if (cedula is null) return null;

        var tabla = await db.ConsultarAsync("web.ppCuenta_ObtenerParaLogin", [Parametro.Texto("@Usuario", cedula, 150)], ct);
        if (tabla.Rows.Count == 0) return null;
        var f = tabla.Rows[0];
        if (!f.Booleano("Activa") || f.FechaHoraONula("BloqueadoHasta") > DateTimeOffset.Now) return null;

        var cuentaId = f.Entero("CuentaId");
        await db.EjecutarAsync("web.ppCuenta_RegistrarAccesoExitoso", [Parametro.Entero("@CuentaId", cuentaId), Parametro.TextoAnsi("@Ip", ip, 45)], ct);
        return new CuentaSesion(cuentaId, f.Texto("Cedula"), f.Texto("NombreCompleto"), f.Texto("Correo"),
            f.Texto("EstadoIdentidad"), f.Booleano("CorreoVerificado"), f.Booleano("AltoContraste"), f.Entero("TamanoTexto"));
    }

    public Task<PerfilCuenta?> ObtenerPerfilAsync(int cuentaId, CancellationToken ct = default)
        => db.ConsultarUnoAsync("web.ppCuenta_Obtener", f => new PerfilCuenta(f.Entero("CuentaId"), f.Texto("Cedula"), f.Texto("Correo"),
                f.TextoONulo("Telefono"), f.Texto("NombreCompleto"), f.Texto("EstadoIdentidad"), f.Booleano("CorreoVerificado"),
                f.FechaHoraONula("UltimoAcceso"), f.FechaHora("FechaCreacion"), f.Booleano("AltoContraste"), f.Entero("TamanoTexto")),
            [Parametro.Entero("@CuentaId", cuentaId)], ct);

    public async Task VerificarCorreoAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ReglaNegocioException(0, "El enlace de verificación no es válido.");
        await db.ConsultarAsync("web.ppCuenta_VerificarCorreo", [Parametro.TextoAnsi("@TokenHash", TokenSeguro.Hashear(token.Trim()), 64)], ct);
    }

    public async Task ActualizarContactoAsync(int cuentaId, string correo, string? telefono, CancellationToken ct = default)
    {
        if (!ValidacionesPortal.CorreoValido(correo)) throw new ReglaNegocioException(0, "El correo electrónico no es válido.");
        if (!string.IsNullOrWhiteSpace(telefono) && !ValidacionesPortal.TelefonoValido(telefono))
            throw new ReglaNegocioException(0, "El teléfono debe tener 10 dígitos y empezar por 809, 829 u 849.");
        await db.EjecutarAsync("web.ppCuenta_ActualizarContacto",
        [
            Parametro.Entero("@CuentaId", cuentaId), Parametro.Texto("@Correo", correo.Trim(), 150),
            Parametro.Texto("@Telefono", string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim(), 20)
        ], ct);
    }

    public async Task CambiarContrasenaAsync(int cuentaId, string actual, string nueva, string confirmacion, string? ip, CancellationToken ct = default)
    {
        var tabla = await db.ConsultarAsync("web.ppCuenta_ObtenerHash", [Parametro.Entero("@CuentaId", cuentaId)], ct);
        if (tabla.Rows.Count == 0 || !HasherContrasena.Verificar(actual, tabla.Rows[0].Texto("PasswordHash")))
            throw new ReglaNegocioException(0, "La contraseña actual no es correcta.");
        ValidarNueva(nueva, confirmacion);
        if (actual == nueva) throw new ReglaNegocioException(0, "La nueva contraseña debe ser distinta de la actual.");

        await db.EjecutarAsync("web.ppCuenta_CambiarContrasena",
            [Parametro.Entero("@CuentaId", cuentaId), Parametro.TextoAnsi("@PasswordHash", HasherContrasena.Hashear(nueva), 200), Parametro.TextoAnsi("@Ip", ip, 45)], ct);
    }

    /// <summary>
    /// "Olvidé mi contraseña". Devuelve el token si el correo existe (para enviarlo por correo), o null.
    /// La pantalla debe mostrar SIEMPRE el mismo mensaje, exista o no el correo.
    /// </summary>
    public async Task<string?> SolicitarRecuperacionAsync(string correo, CancellationToken ct = default)
    {
        if (!ValidacionesPortal.CorreoValido(correo)) return null;
        var (token, hash) = TokenSeguro.Generar();
        var tabla = await db.ConsultarAsync("web.ppRecuperacion_Crear",
            [Parametro.Texto("@Correo", correo.Trim(), 150), Parametro.TextoAnsi("@TokenHash", hash, 64)], ct);
        return tabla.Rows.Count == 0 ? null : token;
    }

    public async Task RestablecerContrasenaAsync(string token, string nueva, string confirmacion, string? ip, CancellationToken ct = default)
    {
        ValidarNueva(nueva, confirmacion);
        await db.EjecutarAsync("web.ppRecuperacion_Usar",
        [
            Parametro.TextoAnsi("@TokenHash", TokenSeguro.Hashear(token?.Trim() ?? string.Empty), 64),
            Parametro.TextoAnsi("@PasswordHash", HasherContrasena.Hashear(nueva), 200), Parametro.TextoAnsi("@Ip", ip, 45)
        ], ct);
    }

    /// <summary>RIU-03: alto contraste y tamaño de texto (100 % a 200 %) recordados por cuenta.</summary>
    public Task GuardarPreferenciasAsync(int cuentaId, bool altoContraste, int tamanoTexto, CancellationToken ct = default)
        => db.EjecutarAsync("web.ppPreferencia_Guardar",
            [Parametro.Entero("@CuentaId", cuentaId), Parametro.Booleano("@AltoContraste", altoContraste), Parametro.Entero("@TamanoTexto", tamanoTexto)], ct);

    public Task<List<EventoSeguridad>> EventosAsync(int cuentaId, CancellationToken ct = default)
        => db.ConsultarListaAsync("web.ppEvento_ListarPorCuenta", f => new EventoSeguridad(f.FechaHora("Fecha"), f.Texto("Evento"),
            f.TextoONulo("Ip"), f.TextoONulo("Detalle")), [Parametro.Entero("@CuentaId", cuentaId)], ct);

    private static void ValidarNueva(string nueva, string confirmacion)
    {
        if (HasherContrasena.ValidarFortaleza(nueva, LongitudMinimaContrasena) is { } error) throw new ReglaNegocioException(0, error);
        if (nueva != confirmacion) throw new ReglaNegocioException(0, "Las contraseñas no coinciden.");
    }

    private Task RegistrarEventoAsync(int? cuentaId, string evento, string? ip, string? detalle, CancellationToken ct)
        => db.EjecutarAsync("web.ppEvento_Registrar",
        [
            Parametro.Entero("@CuentaId", cuentaId), Parametro.Texto("@Evento", evento, 50),
            Parametro.TextoAnsi("@Ip", ip, 45), Parametro.Texto("@Detalle", detalle, 300)
        ], ct);
}
