using CONADIS.Web.Extensions;
using CONADIS.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Areas.Empresas.Controllers;

/// <summary>Portal de Empresas: certificación de accesibilidad.</summary>
[Area("Empresas")]
[Authorize(Roles = "Empresa")]
public class CertificacionesController : Controller
{
    // TODO (capa Application): ICertificacionAccesibilidadService y validación del RNC con la DGII.

    private const decimal TasaCertificacion = 5000m; // SIMULADO: vendría del catálogo de tasas vigente

    // GET /Empresas/Certificaciones  → listado de certificaciones de la empresa
    [HttpGet]
    public IActionResult Index()
    {
        // TODO: listar las solicitudes de la empresa (User.Rnc()).
        return View();
    }

    // GET /Empresas/Certificaciones/Solicitar
    [HttpGet]
    public IActionResult Solicitar() => View(NuevoVM());

    // POST /Empresas/Certificaciones/Solicitar
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public IActionResult Solicitar(CertificacionAccesibilidadVM vm)
    {
        // Datos de la empresa siempre desde el servidor, nunca desde el formulario
        var baseVm = NuevoVM();
        vm.RazonSocial = baseVm.RazonSocial;
        vm.Rnc = baseVm.Rnc;
        vm.RncActivo = baseVm.RncActivo;
        vm.MontoTasa = baseVm.MontoTasa;

        if (vm.Planos.Count == 0)
            ModelState.AddModelError(nameof(vm.Planos), "Sube los planos o la memoria descriptiva.");
        foreach (var p in vm.Planos)
        {
            if (!p.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError(nameof(vm.Planos), $"{p.FileName}: solo PDF.");
            if (p.Length > 50 * 1024 * 1024)
                ModelState.AddModelError(nameof(vm.Planos), $"{p.FileName}: supera los 50 MB.");
        }

        if (!ModelState.IsValid) return View(vm);

        // TODO: _certificaciones.SolicitarAsync(...) → devuelve número de solicitud y orden de pago
        var numeroOrden = $"ORD-{DateTime.Now:yyyyMMddHHmmss}";

        if (vm.Accion == "ventanilla")
        {
            TempData["Mensaje"] = $"Solicitud registrada. Paga en ventanilla con la referencia {numeroOrden}.";
            return RedirectToAction(nameof(Index));
        }

        // Pagos está fuera del área: area = "" para salir de /Empresas
        return RedirectToAction("Index", "Pagos", new { area = "", id = numeroOrden });
    }

    private CertificacionAccesibilidadVM NuevoVM() => new()
    {
        RazonSocial = User.Identity?.Name ?? string.Empty,
        Rnc = User.Rnc() ?? string.Empty,
        RncActivo = true, // SIMULADO: consultar DGII
        MontoTasa = TasaCertificacion
    };
}
