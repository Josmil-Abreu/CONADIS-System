using CONADIS.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>Solicitud de ayudas técnicas (requiere carnet vigente).</summary>
[Authorize]
public class AyudasTecnicasController : Controller
{
    // TODO (capa Application): crear IAyudaTecnicaService y reemplazar los SIMULADO.

    private static readonly Dictionary<CategoriaAyuda, string[]> Productos = new()
    {
        [CategoriaAyuda.Movilidad] = ["Silla de ruedas", "Andador", "Muletas", "Bastón"],
        [CategoriaAyuda.Audicion] = ["Audífono", "Amplificador de sonido"],
        [CategoriaAyuda.Vision] = ["Bastón guía", "Lupa electrónica", "Lector de pantalla"],
        [CategoriaAyuda.Comunicacion] = ["Tablero de comunicación", "Software de comunicación"],
        [CategoriaAyuda.Otra] = ["Otro producto"]
    };

    // GET /AyudasTecnicas  → formulario "Solicitar una ayuda técnica"
    [HttpGet]
    public IActionResult Index()
    {
        var vm = new SolicitudAyudaVM
        {
            NombreTitular = User.Identity?.Name,
            CarnetVigente = TieneCarnetVigente()
        };

        if (!vm.CarnetVigente)
        {
            TempData["Aviso"] = "Necesitas un carnet vigente para solicitar ayudas técnicas.";
            return RedirectToAction("Index", "Tramites");
        }

        return View(vm);
    }

    // GET /AyudasTecnicas/ListaProductos?categoria=1  → llena el select "Producto" (AJAX)
    [HttpGet]
    public IActionResult ListaProductos(CategoriaAyuda categoria)
        => Json(Productos.TryGetValue(categoria, out var lista) ? lista : Array.Empty<string>());

    // POST /AyudasTecnicas  → "Revisar y enviar" o "Guardar borrador"
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public IActionResult Index(SolicitudAyudaVM vm, string accion = "enviar")
    {
        vm.NombreTitular = User.Identity?.Name;
        vm.CarnetVigente = TieneCarnetVigente();

        if (accion == "borrador")
        {
            // TODO: IAyudaTecnicaService.GuardarBorradorAsync(...)
            TempData["Mensaje"] = "Guardamos tu borrador.";
            return RedirectToAction("Index", "Tramites");
        }

        if (vm.PrescripcionMedica is null)
            ModelState.AddModelError(nameof(vm.PrescripcionMedica), "La prescripción médica es obligatoria.");
        if (string.IsNullOrWhiteSpace(vm.Justificacion) && vm.AudioJustificacion is not null)
            ModelState.Remove(nameof(vm.Justificacion)); // el audio reemplaza al texto

        if (!ModelState.IsValid) return View(vm);

        // TODO: IAyudaTecnicaService.SolicitarAsync(...) → devuelve el número de trámite
        var numero = $"TRA-{DateTime.Now:yyyy}-{Random.Shared.Next(1000000, 9999999)}";
        return RedirectToAction(nameof(Confirmacion), new { id = numero });
    }

    // GET /AyudasTecnicas/Confirmacion/TRA-2026-0001190
    [HttpGet]
    public IActionResult Confirmacion(string id)
    {
        ViewBag.NumeroTramite = id;
        return View();
    }

    // SIMULADO: siempre true para poder probar la pantalla.
    // TODO: consultar el estado real del carnet del usuario (ICarnetService).
    private bool TieneCarnetVigente() => true;
}
