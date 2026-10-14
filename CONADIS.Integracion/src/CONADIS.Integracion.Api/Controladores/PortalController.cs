using System.Text.Json.Nodes;
using CONADIS.Integracion.Api.Infraestructura;
using CONADIS.Integracion.Api.Seguridad;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Integracion.Api.Controladores;

public sealed record PeticionPagoEnLinea(string NumeroOrden, string TokenTarjeta);
public sealed record RespuestaPagoEnLinea(Guid TransaccionId, string Estado, string CodigoAutorizacion, string NumeroComprobante);
public sealed record RespuestaIdentidad(string Estado, string? Nombres, string? Apellidos, string? Motivo);

/// <summary>
/// Servicios para el Portal Ciudadano (CONADIS.Web). Todo lo que el ciudadano hace en el portal pasa por aquí.
/// La cédula de las rutas es la del ciudadano que inició sesión en el portal (la Web la toma de su sesión).
/// </summary>
[Route("api/v1/portal")]
[Authorize(Policy = Politicas.Portal)]
public sealed class PortalController(PuenteCore core, ServicioPortal portal, ServicioIdentidad identidad, ServicioPagos pagos) : ControladorBase
{
    // ------------------------------------------------------------------ catálogos

    /// <summary>Centros de valoración activos.</summary>
    [HttpGet("catalogos/centros")]
    public async Task<IActionResult> Centros(CancellationToken ct)
        => Reenviar(await core.GetAsync("api/v1/catalogos/centros", Contexto(), "Centros", ct));

    /// <summary>Días con cupo en un centro.</summary>
    [HttpGet("catalogos/centros/{centroId:int}/disponibilidad")]
    public async Task<IActionResult> Disponibilidad(int centroId, [FromQuery] DateOnly? desde, [FromQuery] int dias = 15, CancellationToken ct = default)
        => Reenviar(await core.GetAsync($"api/v1/catalogos/centros/{centroId}/disponibilidad?dias={dias}" +
                                        (desde is null ? "" : $"&desde={desde:yyyy-MM-dd}"), Contexto(), "Disponibilidad", ct));

    /// <summary>Tasas vigentes.</summary>
    [HttpGet("catalogos/tasas")]
    public async Task<IActionResult> Tasas(CancellationToken ct)
        => Reenviar(await core.GetAsync("api/v1/catalogos/tasas", Contexto(), "Tasas", ct));

    // ------------------------------------------------------------------ identidad (registro de cuentas del portal)

    /// <summary>Valida una cédula con la JCE (al crear la cuenta del portal). Devuelve solo estado y nombre.</summary>
    [HttpGet("identidad/{cedula}")]
    public async Task<RespuestaIdentidad> Identidad(string cedula, CancellationToken ct)
    {
        var r = await identidad.ValidarAsync(cedula, Contexto("Registro de cuenta en el portal"), ct);
        return new RespuestaIdentidad(r.Estado, r.Nombres, r.Apellidos, r.Motivo);
    }

    // ------------------------------------------------------------------ solicitudes (CU-01, RF-WEB-01, RF-WEB-03)

    /// <summary>
    /// Registra una solicitud de certificación, renovación o duplicado. Integración valida antes la
    /// identidad (JCE) y el diagnóstico (MSP) y se la entrega al Core ya enriquecida.
    /// Acepta Idempotency-Key: si el ciudadano pulsa "Enviar" dos veces no se crean dos solicitudes.
    /// </summary>
    [HttpPost("solicitudes")]
    [Idempotente]
    public async Task<IActionResult> RegistrarSolicitud([FromBody] JsonObject solicitud, CancellationToken ct)
        => Reenviar(await portal.RegistrarSolicitudAsync(solicitud, Contexto("Registro de solicitud"), ct));

    /// <summary>Ayuda técnica o certificación de accesibilidad (esta valida el RNC con la DGII).</summary>
    [HttpPost("servicios")]
    [Idempotente]
    public async Task<IActionResult> RegistrarServicio([FromBody] JsonObject solicitud, CancellationToken ct)
        => Reenviar(await portal.RegistrarServicioAsync(solicitud, Contexto("Solicitud de servicio"), ct));

    // ------------------------------------------------------------------ consultas del ciudadano (RF-WEB-02)

    [HttpGet("ciudadanos/{cedula}/tramites")]
    public async Task<IActionResult> Tramites(string cedula, CancellationToken ct)
        => Reenviar(await core.GetAsync($"api/v1/ciudadanos/{PuenteCore.Segmento(cedula)}/tramites", Contexto(), "MisTramites", ct));

    [HttpGet("ciudadanos/{cedula}/tramites/{numero}")]
    public async Task<IActionResult> Tramite(string cedula, string numero, CancellationToken ct)
        => Reenviar(await core.GetAsync($"api/v1/ciudadanos/{PuenteCore.Segmento(cedula)}/tramites/{PuenteCore.Segmento(numero)}",
            Contexto(), "DetalleTramite", ct));

    [HttpPut("ciudadanos/{cedula}/tramites/{numero}/cita")]
    public async Task<IActionResult> ReprogramarCita(string cedula, string numero, [FromBody] JsonObject cuerpo, CancellationToken ct)
        => Reenviar(await core.PutAsync($"api/v1/ciudadanos/{PuenteCore.Segmento(cedula)}/tramites/{PuenteCore.Segmento(numero)}/cita",
            cuerpo.ToJsonString(), Contexto("Reprogramación de cita"), "ReprogramarCita", ct));

    [HttpPost("ciudadanos/{cedula}/tramites/{numero}/apelacion")]
    [Idempotente]
    public async Task<IActionResult> Apelar(string cedula, string numero, [FromBody] JsonObject cuerpo, CancellationToken ct)
        => Reenviar(await core.PostAsync($"api/v1/ciudadanos/{PuenteCore.Segmento(cedula)}/tramites/{PuenteCore.Segmento(numero)}/apelacion",
            cuerpo.ToJsonString(), Contexto("Apelación"), "Apelar", ct: ct));

    [HttpGet("ciudadanos/{cedula}/carnet")]
    public async Task<IActionResult> Carnet(string cedula, CancellationToken ct)
        => Reenviar(await core.GetAsync($"api/v1/ciudadanos/{PuenteCore.Segmento(cedula)}/carnet", Contexto(), "MiCarnet", ct));

    [HttpGet("ciudadanos/{cedula}/avisos")]
    public async Task<IActionResult> Avisos(string cedula, [FromQuery] int maximo = 10, CancellationToken ct = default)
        => Reenviar(await core.GetAsync($"api/v1/ciudadanos/{PuenteCore.Segmento(cedula)}/avisos?maximo={maximo}", Contexto(), "Avisos", ct));

    // ------------------------------------------------------------------ pagos en línea

    [HttpPost("ordenes")]
    [Idempotente]
    public async Task<IActionResult> CrearOrden([FromBody] JsonObject orden, CancellationToken ct)
        => Reenviar(await core.PostAsync("api/v1/ordenes", orden.ToJsonString(), Contexto("Orden de cobro"), "CrearOrden", ct: ct));

    [HttpGet("ordenes/{numeroOrden}")]
    public async Task<IActionResult> Orden(string numeroOrden, CancellationToken ct)
        => Reenviar(await core.GetAsync($"api/v1/ordenes/{PuenteCore.Segmento(numeroOrden)}", Contexto(), "ObtenerOrden", ct));

    /// <summary>
    /// Paga una orden con tarjeta (token de la pasarela, nunca el número de la tarjeta).
    /// Use Idempotency-Key: reintentar sin ella podría cobrar dos veces.
    /// </summary>
    [HttpPost("pagos/en-linea")]
    [Idempotente]
    [ProducesResponseType<RespuestaPagoEnLinea>(StatusCodes.Status201Created)]
    public async Task<IActionResult> PagarEnLinea(PeticionPagoEnLinea p, CancellationToken ct)
    {
        var (pago, autorizacion) = await pagos.PagarEnLineaAsync(p.NumeroOrden, p.TokenTarjeta, Cliente, Contexto("Pago en línea"), ct);
        return StatusCode(StatusCodes.Status201Created,
            new RespuestaPagoEnLinea(pago.Pago.TransaccionId, pago.Pago.Estado, autorizacion, pago.Pago.NumeroComprobante));
    }
}
