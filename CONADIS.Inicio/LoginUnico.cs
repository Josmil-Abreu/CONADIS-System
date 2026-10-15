using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CajaDatos = CONADIS.Caja.Negocio.Datos;
using CajaSeguridad = CONADIS.Caja.Negocio.Seguridad;
using CajaServicios = CONADIS.Caja.Negocio.Servicios;
using CoreDatos = CONADIS.Core.Negocio.Datos;
using CoreSeguridad = CONADIS.Core.Negocio.Seguridad;
using CoreServicios = CONADIS.Core.Negocio.Servicios;
using WebDatos = CONADIS.Web.Negocio.Datos;
using WebExternos = CONADIS.Web.Negocio.Externos;
using WebSeguridad = CONADIS.Web.Negocio.Seguridad;
using WebServicios = CONADIS.Web.Negocio.Servicios;

namespace CONADIS.Inicio;

public enum Modulo { Caja, Core, Portal }

/// <summary>Resultado del login único: a qué módulo va la persona y con qué pase.</summary>
public sealed record Acceso(Modulo Modulo, string Nombre, string Rol, string Pase);

public sealed record ResultadoInicio(bool Exito, string? Mensaje, Acceso? Acceso);

/// <summary>
/// Login único. Decide el módulo según el tipo de usuario:
///  - Cédula (11 dígitos) o correo → ciudadano → Portal (cuentas de CONADIS_WebDB).
///  - Cualquier otro usuario → primero personal del Core (CONADIS_CoreDB) y si no, usuarios de Caja (CONADIS_CajaDB).
/// La contraseña la verifica la lógica de cada módulo (PBKDF2, bloqueo por intentos, bitácora), igual que en su
/// propia pantalla de login. Si es correcta, se emite un pase firmado para ese módulo.
/// </summary>
public sealed class LoginUnico(IConfiguration config, ILoggerFactory logs)
{
    private const string MensajeGenerico = "Usuario o contraseña incorrectos.";
    private readonly ILogger _log = logs.CreateLogger<LoginUnico>();

    private string Clave => config["Inicio:ClaveTraspaso"] ?? "";

    public static bool EsCiudadano(string usuario)
    {
        var u = usuario.Trim();
        var digitos = new string(u.Where(char.IsDigit).ToArray());
        return u.Contains('@') || (digitos.Length == 11 && u.All(c => char.IsDigit(c) || c == '-'));
    }

    public async Task<ResultadoInicio> EntrarAsync(string? usuario, string? contrasena, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrEmpty(contrasena))
            return new(false, "Escriba su usuario (o cédula) y su contraseña.", null);
        usuario = usuario.Trim();

        try
        {
            if (EsCiudadano(usuario)) return await CiudadanoAsync(usuario, contrasena, ct);

            var core = await PersonalCoreAsync(usuario, contrasena, ct);
            if (core.Exito) return core;
            var caja = await CajaAsync(usuario, contrasena, ct);
            if (caja.Exito) return caja;

            // Si alguno dio un motivo concreto (bloqueo, cuenta inactiva), se muestra ese; si no, el genérico.
            var motivo = new[] { core.Mensaje, caja.Mensaje }.FirstOrDefault(m => m is not null && m != MensajeGenerico
                                                                               && !m.StartsWith("Usuario o contraseña"));
            return new(false, motivo ?? MensajeGenerico, null);
        }
        catch (Exception ex) when (ex is CoreDatos.BaseDatosNoDisponibleException or WebDatos.BaseDatosNoDisponibleException
                                       or CajaDatos.BaseDatosNoDisponibleException)
        {
            _log.LogError(ex, "Base de datos no disponible en el login único");
            return new(false, "No se pudo conectar con la base de datos. Verifique que SQL Server (LocalDB) esté disponible.", null);
        }
    }

    private async Task<ResultadoInicio> PersonalCoreAsync(string usuario, string contrasena, CancellationToken ct)
    {
        var db = new CoreDatos.EjecutorSp(config.GetConnectionString("CoreDB"));
        var r = await new CoreServicios.ServicioAutenticacion(db).IniciarSesionAsync(usuario, contrasena, "CONADIS.Inicio", ct);
        if (!r.Exito || r.Usuario is null) return new(false, r.Mensaje, null);
        return new(true, null, new Acceso(Modulo.Core, r.Usuario.NombreCompleto, r.Usuario.Rol,
            CoreSeguridad.PaseInicio.Emitir("Core", r.Usuario.Username, Clave)));
    }

    private async Task<ResultadoInicio> CajaAsync(string usuario, string contrasena, CancellationToken ct)
    {
        var db = new CajaDatos.EjecutorSp(config.GetConnectionString("CajaDB"));
        var servicio = new CajaServicios.ServicioAutenticacionCaja(db, logs.CreateLogger<CajaServicios.ServicioAutenticacionCaja>());
        var r = await servicio.IniciarSesionAsync(usuario, contrasena, ct);
        if (!r.Exito || r.Usuario is null) return new(false, r.Mensaje, null);
        return new(true, null, new Acceso(Modulo.Caja, r.Usuario.NombreCompleto, r.Usuario.Rol,
            CajaSeguridad.PaseInicio.Emitir("Caja", r.Usuario.Username, Clave)));
    }

    private async Task<ResultadoInicio> CiudadanoAsync(string usuario, string contrasena, CancellationToken ct)
    {
        var db = new WebDatos.EjecutorSp(config.GetConnectionString("WebDB"));
        // El login del ciudadano no llama a Integración; el cliente solo hace falta para construir el servicio.
        using var http = new HttpClient();
        var r = await new WebServicios.ServicioCuentas(db, new WebExternos.ClienteSnd(http)).IniciarSesionAsync(usuario, contrasena, "CONADIS.Inicio", ct);
        if (!r.Exito || r.Cuenta is null) return new(false, r.Mensaje, null);
        return new(true, null, new Acceso(Modulo.Portal, r.Cuenta.NombreCompleto, "Ciudadano",
            WebSeguridad.PaseInicio.Emitir("Web", r.Cuenta.Cedula, Clave)));
    }

    /// <summary>Abre el módulo: el navegador para Core y Portal, el programa de Caja para los cajeros.</summary>
    public void Abrir(Acceso a)
    {
        switch (a.Modulo)
        {
            case Modulo.Core:
                AbrirNavegador($"{Url("Inicio:UrlCore", "https://localhost:7103/")}Cuenta/Entrar?pase={Uri.EscapeDataString(a.Pase)}");
                break;
            case Modulo.Portal:
                AbrirNavegador($"{Url("Inicio:UrlPortal", "https://localhost:7122/")}Cuenta/Entrar?pase={Uri.EscapeDataString(a.Pase)}");
                break;
            case Modulo.Caja:
                var ruta = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, config["Inicio:RutaCaja"] ?? "CONADIS.Caja.App.exe"));
                if (!File.Exists(ruta))
                    throw new FileNotFoundException(
                        $"No se encontró la aplicación de Caja en '{ruta}'. Compile CONADIS.Caja.App o corrija Inicio:RutaCaja en appsettings.json.");
                var psi = new ProcessStartInfo(ruta) { WorkingDirectory = Path.GetDirectoryName(ruta)!, UseShellExecute = false };
                psi.ArgumentList.Add("--pase");
                psi.ArgumentList.Add(a.Pase);
                Process.Start(psi);
                break;
        }
        _log.LogInformation("Login único: {Nombre} ({Rol}) → {Modulo}", a.Nombre, a.Rol, a.Modulo);
    }

    /// <summary>Abre el registro de cuentas del Portal (ciudadanos que todavía no tienen usuario).</summary>
    public void AbrirRegistro() => AbrirNavegador($"{Url("Inicio:UrlPortal", "https://localhost:7122/")}Cuenta/Registro");

    private string Url(string clave, string predeterminada)
    {
        var u = config[clave] ?? predeterminada;
        return u.EndsWith('/') ? u : u + "/";
    }

    private static void AbrirNavegador(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
