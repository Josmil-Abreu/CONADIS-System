using CONADIS.Core.Api.Contratos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Servicios;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Core.Api.Controladores;

/// <summary>
/// Endpoints que usa el servicio de Windows de Integración (Paso 10) para sus tareas en segundo plano:
/// revalidar identidades con la JCE, informar el resultado del MSP, enviar notificaciones y vencer carnets.
/// </summary>
[Route("api/v1/procesos")]
public sealed class ProcesosController(ServicioExpedientes expedientes, ServicioNotificaciones notificaciones,
    ServicioCarnets carnets) : ControladorBase
{
    /// <summary>Personas con identidad pendiente de la JCE. RequiereAnalista = true si pasaron más de las horas indicadas.</summary>
    [HttpGet("identidad-pendiente")]
    public Task<List<PersonaIdentidadPendiente>> IdentidadPendiente([FromQuery] int horas = 24, CancellationToken ct = default)
        => expedientes.ListarIdentidadPendienteAsync(horas, ct);

    /// <summary>Resultado de una revalidación con la JCE.</summary>
    [HttpPost("validacion-jce")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ValidacionJce(PeticionValidacionJce p, CancellationToken ct)
    {
        await expedientes.RegistrarValidacionJceAsync(p.Cedula, p.EsValida, p.Nombres, p.Apellidos, p.FechaNacimiento, p.Sexo,
            Contexto(), ct);
        return NoContent();
    }

    /// <summary>Resultado de la validación del diagnóstico con el MSP.</summary>
    [HttpPost("validacion-msp")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ValidacionMsp(PeticionValidacionMsp p, CancellationToken ct)
    {
        await expedientes.RegistrarValidacionMspAsync(p.NumeroTramite, p.Estado, Contexto(), ct);
        return NoContent();
    }

    /// <summary>Notificaciones en cola para enviar por correo o SMS.</summary>
    [HttpGet("notificaciones")]
    public Task<List<NotificacionPendiente>> Notificaciones([FromQuery] int maximo = 50, CancellationToken ct = default)
        => notificaciones.PendientesAsync(maximo, ct);

    /// <summary>Resultado del envío. Tras 5 fallos la notificación queda como Fallida.</summary>
    [HttpPost("notificaciones/{notificacionId:long}/resultado")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResultadoNotificacion(long notificacionId, PeticionResultadoNotificacion p, CancellationToken ct)
    {
        await notificaciones.MarcarResultadoAsync(notificacionId, p.Exito, ct);
        return NoContent();
    }

    /// <summary>Proceso diario: marca como vencidos los carnets cuya fecha pasó.</summary>
    [HttpPost("vencimientos")]
    public async Task<RespuestaVencimientos> Vencimientos(CancellationToken ct)
        => new(await carnets.MarcarVencidosAsync(Contexto(), ct));
}