using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Servicios;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Core.Api.Controladores;

/// <summary>Solicitudes que llegan desde el portal (RF-WEB-01) y de servicios (RF-WEB-03).</summary>
[Route("api/v1")]
public sealed class SolicitudesController(ServicioExpedientes expedientes, ServicioSolicitudesServicio servicios) : ControladorBase
{
    /// <summary>
    /// Registra una solicitud de certificación, renovación o duplicado hecha en el portal.
    /// Integración ya consultó la JCE y el MSP y envía sus resultados en EstadoIdentidad y EstadoDiagnostico.
    /// </summary>
    /// <response code="201">Solicitud registrada: devuelve el número de trámite y de expediente.</response>
    /// <response code="422">Regla de negocio (ej.: ya tiene una solicitud activa).</response>
    [HttpPost("solicitudes")]
    [ProducesResponseType<SolicitudRegistrada>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SolicitudRegistrada>> Registrar(NuevaSolicitud solicitud, CancellationToken ct)
    {
        // Lo que llega por la API siempre es del canal Web (las presenciales se registran en la aplicación interna)
        var registrada = await expedientes.RegistrarSolicitudAsync(solicitud with { Canal = "Web" }, Contexto(), ct);
        return StatusCode(StatusCodes.Status201Created, registrada);
    }

    /// <summary>Solicitud de ayuda técnica o de certificación de accesibilidad.</summary>
    [HttpPost("servicios")]
    [ProducesResponseType<ServicioRegistrado>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ServicioRegistrado>> RegistrarServicio(NuevaSolicitudServicio solicitud, CancellationToken ct)
    {
        var registrado = await servicios.RegistrarAsync(solicitud, Contexto(), ct);
        return StatusCode(StatusCodes.Status201Created, registrado);
    }
}