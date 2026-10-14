using CONADIS.Web.Infraestructura;
using CONADIS.Web.Models.ViewModels;
using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Modelos;
using CONADIS.Web.Negocio.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Web.Controllers;

/// <summary>Solicitud de ayudas técnicas (requiere carnet vigente).</summary>
[Authorize]
public class AyudasTecnicasController(ServicioPortal portal, ServicioBorradores borradores) : ControladorPortal
{
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
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new SolicitudAyudaVM
        {
            NombreTitular = Cuenta.NombreCompleto,
            CarnetVigente = await TieneCarnetVigenteAsync(ct)
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
    public async Task<IActionResult> Index(SolicitudAyudaVM vm, CancellationToken ct, string accion = "enviar")
    {
        vm.NombreTitular = Cuenta.NombreCompleto;
        vm.CarnetVigente = await TieneCarnetVigenteAsync(ct);
        if (!vm.CarnetVigente)
        {
            TempData["Aviso"] = "Necesitas un carnet vigente para solicitar ayudas técnicas.";
            return RedirectToAction("Index", "Tramites");
        }

        if (accion == "borrador")
        {
            // Los archivos no se guardan en el borrador: solo los datos del formulario.
            await borradores.GuardarAsync(Cuenta.CuentaId, TiposBorrador.AyudaTecnica, 1, new
            {
                vm.Categoria, vm.Producto, vm.Justificacion, vm.SituacionLaboral, vm.IngresoMensualHogar, vm.RecibioAyudaSimilar, vm.FormaEntrega
            }, ct);
            TempData["Mensaje"] = "Guardamos tu borrador.";
            return RedirectToAction("Index", "Tramites");
        }

        if (vm.PrescripcionMedica is null)
            ModelState.AddModelError(nameof(vm.PrescripcionMedica), "La prescripción médica es obligatoria.");
        if (string.IsNullOrWhiteSpace(vm.Justificacion) && vm.AudioJustificacion is not null)
            ModelState.Remove(nameof(vm.Justificacion)); // el audio reemplaza al texto

        if (!ModelState.IsValid) return View(vm);

        var detalle = $"""
            Categoría: {vm.Categoria}
            Producto: {vm.Producto}
            Justificación: {(string.IsNullOrWhiteSpace(vm.Justificacion) ? "(en audio adjunto)" : vm.Justificacion)}
            Situación laboral: {vm.SituacionLaboral}
            Ingreso mensual del hogar: {vm.IngresoMensualHogar}
            Recibió ayuda similar: {(vm.RecibioAyudaSimilar ? "Sí" : "No")}
            Entrega: {vm.FormaEntrega}
            Prescripción médica: {vm.PrescripcionMedica?.FileName}
            """;
        try
        {
            var r = await portal.EnviarServicioAsync(Cuenta,
                new SolicitudServicioPortal("AYUDA_TECNICA", Cuenta.Cedula, Cuenta.NombreCompleto, Cuenta.Correo, $"Ayuda técnica: {vm.Producto}", detalle),
                ClaveIdempotencia("ayuda", $"{vm.Categoria}-{vm.Producto}-{DateTime.Today:yyyyMMdd}"), Ip, ct);
            await borradores.EliminarAsync(Cuenta.CuentaId, TiposBorrador.AyudaTecnica, ct);
            return RedirectToAction(nameof(Confirmacion), new { id = r.NumeroTramite });
        }
        catch (ReglaNegocioException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
    }

    // GET /AyudasTecnicas/Confirmacion/TRA-2026-0001190
    [HttpGet]
    public IActionResult Confirmacion(string id)
    {
        ViewBag.NumeroTramite = id;
        return View();
    }

    private async Task<bool> TieneCarnetVigenteAsync(CancellationToken ct)
        => (await portal.MiCarnetAsync(Cuenta, Ip, ct))?.Estado == "Vigente";
}
