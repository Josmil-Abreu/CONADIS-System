using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CONADIS.Caja.Controllers;
public sealed class CuentaController(IWebHostEnvironment env,IConfiguration config):Controller
{
    [AllowAnonymous] public IActionResult Index()=>env.IsDevelopment()&&config.GetValue<bool>("Caja:Demo")?View():Challenge();
    [HttpPost,AllowAnonymous] public async Task<IActionResult> Demo(string perfil)
    {
        if(!env.IsDevelopment()||!config.GetValue<bool>("Caja:Demo"))return NotFound();
        if(!new[]{"cajero-1","cajero-2","supervisor-1"}.Contains(perfil))return BadRequest();
        var role=perfil.StartsWith("supervisor",StringComparison.Ordinal)?"SupervisorCaja":"Cajero";
        var claims=new[]{new Claim(ClaimTypes.NameIdentifier,perfil),new Claim(ClaimTypes.Name,perfil),new Claim(ClaimTypes.Role,role),new Claim("sucursal","SEDE-DEMO")};
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,new ClaimsPrincipal(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme)));return RedirectToAction("Index","Caja");
    }
    [HttpPost,Authorize] public async Task<IActionResult> Salir(){await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);return RedirectToAction("Index");}
}
