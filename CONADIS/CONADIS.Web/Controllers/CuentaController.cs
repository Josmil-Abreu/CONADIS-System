using System.Security.Claims;
using CONADIS.Web.Extensions;
using CONADIS.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>Inicio de sesión, registro y recuperación de contraseña.</summary>
[AllowAnonymous]
public class CuentaController : Controller
{
    // TODO (capa Application): cuando exista IAutenticacionService / IUsuarioService,
    // inyectarlo aquí y reemplazar las partes marcadas como SIMULADO.

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
    public async Task<IActionResult> IniciarSesion(IniciarSesionVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var cedula = vm.Cedula.Replace("-", "");

        // SIMULADO: acepta cualquier cédula válida. Reemplazar por la validación real
        // (usuario + hash de contraseña + bloqueo tras 5 intentos fallidos por 15 min).
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, cedula),
            new(ClaimTypes.Name, "Ciudadano " + cedula[^4..]),
            new(ClaimsExtensions.ClaimCedula, cedula),
            new(ClaimTypes.Role, "Ciudadano")
        };

        await FirmarAsync(claims);

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
    public async Task<IActionResult> Registro(CrearCuentaVM vm)
    {
        // Validaciones que dependen del tipo de cuenta
        if (vm.TipoCuenta == TipoCuenta.Empresa && string.IsNullOrWhiteSpace(vm.Rnc))
            ModelState.AddModelError(nameof(vm.Rnc), "Escribe el RNC de la empresa.");
        if (vm.TipoCuenta != TipoCuenta.Empresa && string.IsNullOrWhiteSpace(vm.Cedula))
            ModelState.AddModelError(nameof(vm.Cedula), "Escribe tu cédula.");

        if (!ModelState.IsValid) return View(vm);

        // SIMULADO: aquí iría IUsuarioService.RegistrarAsync(...) (y validación del RNC con la DGII).
        var claims = new List<Claim> { new(ClaimTypes.Email, vm.Email) };

        if (vm.TipoCuenta == TipoCuenta.Empresa)
        {
            claims.Add(new(ClaimTypes.NameIdentifier, vm.Rnc!));
            claims.Add(new(ClaimTypes.Name, "Empresa " + vm.Rnc));
            claims.Add(new(ClaimsExtensions.ClaimRnc, vm.Rnc!));
            claims.Add(new(ClaimTypes.Role, "Empresa"));
            await FirmarAsync(claims);
            return RedirectToAction("Index", "Certificaciones", new { area = "Empresas" });
        }

        var cedula = vm.Cedula!.Replace("-", "");
        claims.Add(new(ClaimTypes.NameIdentifier, cedula));
        claims.Add(new(ClaimTypes.Name, "Ciudadano " + cedula[^4..]));
        claims.Add(new(ClaimsExtensions.ClaimCedula, cedula));
        claims.Add(new(ClaimTypes.Role, vm.TipoCuenta == TipoCuenta.Tutor ? "Tutor" : "Ciudadano"));
        await FirmarAsync(claims);

        TempData["Mensaje"] = "Tu cuenta fue creada.";
        return RedirectToAction("Index", "Tramites");
    }

    // GET /Cuenta/OlvideContrasena
    [HttpGet]
    public IActionResult OlvideContrasena() => View(new OlvideContrasenaVM());

    // POST /Cuenta/OlvideContrasena
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult OlvideContrasena(OlvideContrasenaVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        // TODO: IUsuarioService.EnviarEnlaceRecuperacionAsync(vm.Email)
        // Por seguridad, siempre se muestra el mismo mensaje exista o no el correo.
        TempData["Mensaje"] = "Si el correo está registrado, te enviamos un enlace para cambiar tu contraseña.";
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

    private Task FirmarAsync(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }
}
