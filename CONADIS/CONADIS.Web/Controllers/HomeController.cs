using System.Diagnostics;
using CONADIS.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>Páginas públicas e informativas del portal.</summary>
public class HomeController : Controller
{
    // GET /  → Pantalla de inicio
    public IActionResult Index() => View();

    // GET /Home/PreguntasFrecuentes
    public IActionResult PreguntasFrecuentes() => View();

    // GET /Home/Accesibilidad  → declaración de accesibilidad
    public IActionResult Accesibilidad() => View();

    // GET /Home/Privacidad  → aviso de privacidad (Ley 172-13), enlazado desde el paso 5
    public IActionResult Privacidad() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
