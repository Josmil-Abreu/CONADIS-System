using CONADIS.Integracion.Api.Seguridad;
using CONADIS.Integracion.Negocio.Reportes;
using CONADIS.Integracion.Negocio.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Integracion.Api.Controladores;

/// <summary>
/// Reportes RDLC de Integración (PDF o Excel). Solo para clientes con el ámbito "reportes"
/// (el sistema interno del Core). El encabezado X-Usuario-Final indica la persona que lo pidió.
/// </summary>
[Route("api/v1/reportes")]
[Authorize(Policy = Politicas.Reportes)]
public sealed class ReportesController(ServicioReportesIntegracion reportes) : ControladorBase
{
    /// <summary>GET api/v1/reportes/{transacciones|pagos|servicios}?desde=2026-10-01&amp;hasta=2026-10-14&amp;formato=Pdf</summary>
    [HttpGet("{reporte}")]
    [Produces("application/pdf", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task<IActionResult> Periodo(string reporte, [FromQuery] DateOnly desde, [FromQuery] DateOnly hasta,
        [FromQuery] FormatoReporte formato = FormatoReporte.Pdf, [FromQuery] string? servicio = null, CancellationToken ct = default)
    {
        var a = await reportes.PeriodoAsync(reporte, desde, hasta, formato, Solicitante(), servicio, ct);
        return File(a.Contenido, a.TipoContenido, a.NombreArchivo);
    }

    /// <summary>GET api/v1/reportes/transacciones/{id}: comprobante de una transacción de la bitácora.</summary>
    [HttpGet("transacciones/{id:long}")]
    [Produces("application/pdf", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task<IActionResult> Comprobante(long id, [FromQuery] FormatoReporte formato = FormatoReporte.Pdf, CancellationToken ct = default)
    {
        var a = await reportes.ComprobanteAsync(id, formato, Solicitante(), ct);
        return File(a.Contenido, a.TipoContenido, a.NombreArchivo);
    }

    private string Solicitante()
    {
        var c = Contexto();
        return c.Usuario is null ? c.Cliente : $"{c.Usuario} (vía {c.Cliente})";
    }
}
