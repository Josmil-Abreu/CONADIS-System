using CONADIS.Core.Api.Contratos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Servicios;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Core.Api.Controladores;

/// <summary>
/// Lo que el ciudadano consulta o hace desde el portal (RF-WEB-02). La cédula es la del ciudadano
/// que inició sesión en el portal; Integración la toma de su sesión, nunca de lo que escriba el usuario.
/// </summary>
[Route("api/v1/ciudadanos/{cedula}")]
public sealed class CiudadanosController(ServicioExpedientes expedientes, ServicioCitas citas, ServicioDictamen dictamen,
    ServicioCarnets carnets, ServicioNotificaciones notificaciones) : ControladorBase
{
    /// <summary>"Mis trámites": los de la cédula y los de las personas que representa.</summary>
    [HttpGet("tramites")]
    public Task<List<TramiteResumen>> Tramites(string cedula, CancellationToken ct)
        => expedientes.ListarTramitesPorCedulaAsync(cedula, ct);

    /// <summary>Detalle de un trámite. 404 si no existe o no pertenece a la cédula (no se distingue, para no revelar números).</summary>
    [HttpGet("tramites/{numero}")]
    [ProducesResponseType<TramiteCiudadano>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TramiteCiudadano>> Tramite(string cedula, string numero, CancellationToken ct)
    {
        var tramite = await expedientes.ObtenerTramiteCiudadanoAsync(numero, cedula, ct);
        return tramite is null ? NotFound() : Ok(tramite);
    }

    /// <summary>Reprogramar la cita de valoración.</summary>
    [HttpPut("tramites/{numero}/cita")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ReprogramarCita(string cedula, string numero, PeticionReprogramarCita peticion, CancellationToken ct)
    {
        await citas.ReprogramarCiudadanoAsync(numero, cedula, peticion.NuevaFecha, ct);
        return NoContent();
    }

    /// <summary>Apelar un rechazo dentro del plazo.</summary>
    [HttpPost("tramites/{numero}/apelacion")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Apelar(string cedula, string numero, PeticionApelacion peticion, CancellationToken ct)
    {
        await dictamen.RegistrarApelacionAsync(numero, cedula, peticion.Motivo, ct);
        return NoContent();
    }

    /// <summary>"Mi carnet digital". 404 si no tiene carnet vigente.</summary>
    [HttpGet("carnet")]
    [ProducesResponseType<CarnetCiudadano>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CarnetCiudadano>> Carnet(string cedula, CancellationToken ct)
    {
        var carnet = await carnets.ObtenerPorCedulaAsync(cedula, ct);
        return carnet is null ? NotFound() : Ok(carnet);
    }

    /// <summary>Avisos que el ciudadano ve dentro del portal.</summary>
    [HttpGet("avisos")]
    public Task<List<AvisoPortal>> Avisos(string cedula, [FromQuery] int maximo = 10, CancellationToken ct = default)
        => notificaciones.ListarPortalAsync(cedula, maximo, ct);
}