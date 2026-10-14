using CONADIS.Web.Models.ViewModels;
using CONADIS.Web.Negocio.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>
/// Verificación pública de carnets y certificados.
/// El QR del carnet apunta a /verificar/{codigo}. Solo muestra estado, nombre enmascarado y fechas.
/// </summary>
[AllowAnonymous]
public class VerificacionController(ServicioPortal portal) : Controller
{
    // GET /verificar           → formulario vacío
    // GET /verificar/{codigo}  → llega desde el QR y muestra el resultado directo
    [HttpGet("verificar/{codigo?}")]
    [HttpGet("Verificacion/Index/{codigo?}")]
    public async Task<IActionResult> Index(string? codigo, CancellationToken ct)
    {
        var vm = new VerificacionVM { Codigo = codigo };
        if (string.IsNullOrWhiteSpace(codigo)) return View(vm);

        var r = await portal.VerificarCarnetAsync(codigo.Trim(), ct);
        vm.Resultado = Enum.TryParse<ResultadoVerificacion>(r.Resultado, ignoreCase: true, out var res) && res != ResultadoVerificacion.NoConsultado
            ? res : ResultadoVerificacion.NoEncontrado;
        if (vm.Resultado == ResultadoVerificacion.NoEncontrado) return View(vm);

        vm.TipoDocumento = "Carnet de persona con discapacidad";
        vm.TitularParcial = r.TitularParcial;
        vm.FechaEmision = r.FechaEmision;
        vm.ValidoHasta = r.ValidoHasta;
        vm.FirmaValida = true;   // el código solo existe si el Core emitió y firmó el carnet
        return View(vm);
    }

    // POST /verificar  → el usuario escribió el código a mano
    [HttpPost("verificar")]
    [ValidateAntiForgeryToken]
    public IActionResult Verificar(VerificacionVM vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Codigo))
        {
            ModelState.AddModelError(nameof(vm.Codigo), "Escribe el código de verificación.");
            return View(nameof(Index), vm);
        }

        // PRG: redirige a la URL con el código, igual que si viniera del QR
        return RedirectToAction(nameof(Index), new { codigo = vm.Codigo.Trim() });
    }
}
