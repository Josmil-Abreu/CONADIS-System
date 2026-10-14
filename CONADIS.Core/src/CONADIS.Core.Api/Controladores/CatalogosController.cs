using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Servicios;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Core.Api.Controladores;

/// <summary>Catálogos que necesita el portal: centros de valoración, cupos y tasas.</summary>
[Route("api/v1/catalogos")]
public sealed class CatalogosController(ServicioCatalogos catalogos, ServicioCitas citas) : ControladorBase
{
    /// <summary>Centros de valoración activos.</summary>
    [HttpGet("centros")]
    public Task<List<CentroValoracion>> Centros(CancellationToken ct)
        => catalogos.ListarCentrosAsync(soloActivos: true, ct);

    /// <summary>Días con cupo en un centro (sin fines de semana). Por defecto, desde mañana y 15 días.</summary>
    [HttpGet("centros/{centroId:int}/disponibilidad")]
    public Task<List<CupoDisponible>> Disponibilidad(int centroId, [FromQuery] DateOnly? desde, [FromQuery] int dias = 15,
        CancellationToken ct = default)
        => citas.DisponibilidadAsync(centroId, desde ?? DateOnly.FromDateTime(DateTime.Today.AddDays(1)), dias, ct);

    /// <summary>Tasas vigentes (el precio sale siempre de aquí, RINV-07).</summary>
    [HttpGet("tasas")]
    public Task<List<Tasa>> Tasas(CancellationToken ct)
        => catalogos.ListarTasasAsync(soloActivas: true, ct);
}