using System.Security.Claims;
using CONADIS.Web.Infraestructura;
using CONADIS.Web.Models.ViewModels;
using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Modelos;
using CONADIS.Web.Negocio.Servicios;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>Inicio de sesión, registro y recuperación de contraseña (cuentas en CONADIS_WebDB).</summary>
[AllowAnonymous]
public class CuentaController(ServicioCuentas cuentas, IWebHostEnvironment entorno) : ControladorPortal
{
    // GET /Cuenta/IniciarSesion
    [HttpGet]
    public IActionResult IniciarSesion(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Tramites");

        return View(new IniciarSesionVM { ReturnUrl = returnUrl });
    }

    // POST /Cuenta/IniciarSesion
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarSesion(IniciarSesionVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);

        // Bloqueo tras varios intentos fallidos, hash PBKDF2 y mensaje genérico: todo en ServicioCuentas.
        var r = await cuentas.IniciarSesionAsync(vm.Cedula.Replace("-", ""), vm.Contrasena, Ip, ct);
        if (!r.Exito || r.Cuenta is null)
        {
            ModelState.AddModelError(string.Empty, r.Mensaje ?? "No se pudo iniciar sesión.");
            return View(vm);
        }

        await FirmarAsync(r.Cuenta);

        if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl))
            return LocalRedirect(vm.ReturnUrl);

        return RedirectToAction("Index", "Tramites");
    }

    // GET /Cuenta/Registro?tipo=Titular|Tutor|Empresa
    [HttpGet]
    public IActionResult Registro(TipoCuenta tipo = TipoCuenta.Titular)
        => View(new CrearCuentaVM { TipoCuenta = tipo });

    // POST /Cuenta/Registro
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Registro(CrearCuentaVM vm, CancellationToken ct)
    {
        if (vm.TipoCuenta == TipoCuenta.Empresa)
            ModelState.AddModelError(nameof(vm.Rnc),
                "Las cuentas de empresa todavía no se crean en línea. Solicite la certificación de accesibilidad en cualquier sede del CONADIS.");
        if (vm.TipoCuenta != TipoCuenta.Empresa && string.IsNullOrWhiteSpace(vm.Cedula))
            ModelState.AddModelError(nameof(vm.Cedula), "Escribe tu cédula.");

        if (!ModelState.IsValid) return View(vm);

        try
        {
            // Valida la cédula con la JCE (vía Integración), la fortaleza de la contraseña y que no exista otra cuenta.
            var cedula = vm.Cedula!.Replace("-", "");
            var registro = await cuentas.RegistrarAsync(
                new DatosRegistro(cedula, vm.Email, null, vm.Contrasena, vm.ConfirmarContrasena, AceptaTerminos: true), Ip, ct);

            var login = await cuentas.IniciarSesionAsync(cedula, vm.Contrasena, Ip, ct);
            if (login.Cuenta is not null) await FirmarAsync(login.Cuenta);

            TempData["Mensaje"] = registro.EstadoIdentidad == "Validada"
                ? $"Bienvenido(a), {registro.NombreCompleto}. Tu cuenta fue creada."
                : "Tu cuenta fue creada. Validaremos tu identidad con la JCE y te avisaremos.";

            // En desarrollo no se envían correos: se muestra el enlace de verificación para probarlo.
            if (entorno.IsDevelopment())
                TempData["Aviso"] = $"(Desarrollo) Enlace de verificación del correo: {Url.Action(nameof(VerificarCorreo), null, new { token = registro.TokenVerificacion }, Request.Scheme)}";

            return RedirectToAction("Index", "Tramites");
        }
        catch (ReglaNegocioException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
    }

    // GET /Cuenta/VerificarCorreo?token=...
    [HttpGet]
    public async Task<IActionResult> VerificarCorreo(string token, CancellationToken ct)
    {
        await cuentas.VerificarCorreoAsync(token, ct);
        TempData["Mensaje"] = "Tu correo quedó verificado.";
        return RedirectToAction(nameof(IniciarSesion));
    }

    // GET /Cuenta/OlvideContrasena
    [HttpGet]
    public IActionResult OlvideContrasena() => View(new OlvideContrasenaVM());

    // POST /Cuenta/OlvideContrasena
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OlvideContrasena(OlvideContrasenaVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);

        var token = await cuentas.SolicitarRecuperacionAsync(vm.Email, ct);

        // Por seguridad, siempre se muestra el mismo mensaje exista o no el correo.
        TempData["Mensaje"] = "Si el correo está registrado, te enviamos un enlace para cambiar tu contraseña.";
        if (token is not null && entorno.IsDevelopment())
            TempData["Aviso"] = $"(Desarrollo) Código de recuperación: {token}";
        return RedirectToAction(nameof(IniciarSesion));
    }

    // POST /Cuenta/CerrarSesion  (POST para evitar cierres de sesión por enlaces externos)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CerrarSesion()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        return RedirectToAction("Index", "Home");
    }

    // GET /Cuenta/AccesoDenegado
    [HttpGet]
    public IActionResult AccesoDenegado() => View();

    private Task FirmarAsync(CuentaSesion cuenta)
    {
        var identidad = new ClaimsIdentity(SesionCiudadano.Claims(cuenta), CookieAuthenticationDefaults.AuthenticationScheme);
        return HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identidad));
    }
}
