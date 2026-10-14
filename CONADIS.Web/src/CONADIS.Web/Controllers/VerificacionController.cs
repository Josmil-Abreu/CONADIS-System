using CONADIS.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>
/// Verificación pública de carnets y certificados.
/// El QR del carnet apunta a /verificar/{codigo}.
/// </summary>
[AllowAnonymous]
public class VerificacionController : Controller
{
    // TODO (capa Application): crear IVerificacionService con un método
    // VerificarAsync(string codigo) que busque el Carnet por TokenVerificacion
    // y devuelva estado + datos públicos. Reemplazar BuscarSimulado().

    // GET /verificar           → formulario vacío
    // GET /verificar/{codigo}  → llega desde el QR y muestra el resultado directo
    [HttpGet("verificar/{codigo?}")]
    [HttpGet("Verificacion/Index/{codigo?}")]
    public IActionResult Index(string? codigo)
    {
        var vm = new VerificacionVM { Codigo = codigo };
        if (!string.IsNullOrWhiteSpace(codigo))
            vm = BuscarSimulado(codigo.Trim().ToUpperInvariant());

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

    // SIMULADO: devuelve resultados de ejemplo según el código para poder probar las 4 vistas.
    private static VerificacionVM BuscarSimulado(string codigo)
    {
        var vm = new VerificacionVM { Codigo = codigo };

        if (codigo.EndsWith("VEN")) vm.Resultado = ResultadoVerificacion.Vencido;
        else if (codigo.EndsWith("ANU")) vm.Resultado = ResultadoVerificacion.Anulado;
        else if (codigo.StartsWith("CNDS-")) vm.Resultado = ResultadoVerificacion.Vigente;
        else { vm.Resultado = ResultadoVerificacion.NoEncontrado; return vm; }

        vm.TipoDocumento = "Carnet de persona con discapacidad";
        vm.TitularParcial = "J*** P*** G***";
        vm.FechaEmision = new DateOnly(2026, 1, 15);
        vm.ValidoHasta = vm.Resultado == ResultadoVerificacion.Vencido
            ? new DateOnly(2026, 6, 30)
            : new DateOnly(2031, 1, 15);
        vm.FirmaValida = true;
        return vm;
    }
}
