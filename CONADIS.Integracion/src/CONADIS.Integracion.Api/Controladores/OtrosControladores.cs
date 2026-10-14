using CONADIS.Integracion.Api.Seguridad;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Externos;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Repositorios;
using CONADIS.Integracion.Negocio.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Integracion.Api.Controladores;

public sealed record RespuestaRecepcionPago(Guid TransaccionId, string Estado, bool EsNuevo, DateTimeOffset FechaRecepcion, string? Detalle);
public sealed record PeticionFirma(string Contenido);
public sealed record RespuestaSalud(string Estado, bool BaseDatos, bool Core, IReadOnlyList<CircuitoDetalle> Circuitos, DateTimeOffset Hora);

/// <summary>
/// Servicios para el Módulo de Caja. Los cobros se aceptan aunque el Core esté caído:
/// quedan "Recibido" y el servicio de Windows los entrega al Core en cuanto vuelva (outbox).
/// </summary>
[Route("api/v1/caja")]
[Authorize(Policy = Politicas.Caja)]
public sealed class CajaController(PuenteCore core, ServicioPagos pagos, ServicioDgii dgii) : ControladorBase
{
    /// <summary>Tasas vigentes (la Caja las guarda para poder cobrar fuera de línea).</summary>
    [HttpGet("tasas")]
    public async Task<IActionResult> Tasas(CancellationToken ct)
        => Reenviar(await core.GetAsync("api/v1/catalogos/tasas", Contexto(), "Tasas", ct));

    /// <summary>Consulta una orden de cobro para cobrar el monto exacto.</summary>
    [HttpGet("ordenes/{numeroOrden}")]
    public async Task<IActionResult> Orden(string numeroOrden, CancellationToken ct)
        => Reenviar(await core.GetAsync($"api/v1/ordenes/{PuenteCore.Segmento(numeroOrden)}", Contexto(), "ObtenerOrden", ct));

    /// <summary>
    /// Registra un cobro. Responde 202 (aceptado): el pago queda guardado y se entrega al Core por la cola.
    /// Es idempotente por TransaccionId: reenviar el mismo cobro devuelve EsNuevo = false.
    /// </summary>
    [HttpPost("pagos")]
    [ProducesResponseType<RespuestaRecepcionPago>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RegistrarPago(PagoEntrante pago, CancellationToken ct)
    {
        var r = await pagos.RecibirAsync(pago, Cliente, Contexto("Cobro en caja"), ct);
        return Accepted(new RespuestaRecepcionPago(r.Pago.TransaccionId, r.Pago.Estado, r.EsNuevo, r.Pago.FechaRecepcion, r.Pago.Detalle));
    }

    /// <summary>Estado de un cobro enviado: Recibido (en cola), Aplicado, Duplicado o Rechazado por el Core.</summary>
    [HttpGet("pagos/{transaccionId:guid}")]
    [ProducesResponseType<RespuestaRecepcionPago>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EstadoPago(Guid transaccionId, CancellationToken ct)
    {
        var p = await pagos.ObtenerAsync(transaccionId, Cliente, ct);
        return p is null ? NotFound() : Ok(new RespuestaRecepcionPago(p.TransaccionId, p.Estado, false, p.FechaRecepcion, p.Detalle));
    }

    /// <summary>Consulta un RNC en la DGII (factura de crédito fiscal, CU-02 paso 4).</summary>
    [HttpGet("rnc/{rnc}")]
    public Task<RespuestaRnc> Rnc(string rnc, CancellationToken ct)
        => dgii.ConsultarRncAsync(rnc, Contexto("Factura de crédito fiscal"), ct);

    /// <summary>
    /// Envía un e-CF a la DGII. Si la DGII no responde, devuelve 503: la Caja deja el comprobante
    /// "en contingencia" y lo reenvía más tarde (el cobro ya está hecho).
    /// </summary>
    [HttpPost("ecf")]
    public Task<RespuestaEcf> EnviarEcf(ComprobanteFiscal comprobante, CancellationToken ct)
        => dgii.EnviarComprobanteAsync(comprobante, Contexto("Envío de e-CF"), ct);
}

/// <summary>Firma digital para el Core (RF-INT-03). Contrato: { "contenido": "..." } -> { "firma": "...", ... }.</summary>
[Route("api/v1/firma")]
[Authorize(Policy = Politicas.Firma)]
public sealed class FirmaController(ServicioFirmaDigital firma) : ControladorBase
{
    [HttpPost]
    public Task<FirmaDocumento> Firmar(PeticionFirma p, CancellationToken ct)
        => firma.FirmarAsync(p.Contenido, Contexto("Firma de documento oficial"), ct);
}

/// <summary>Verificación de carnets y de la condición de discapacidad.</summary>
[Route("api/v1/verificacion")]
public sealed class VerificacionController(PuenteCore core, ServicioVerificacion verificacion) : ControladorBase
{
    /// <summary>Verificación pública por QR (la consulta el portal). Solo estado, nombre enmascarado y fechas.</summary>
    [HttpGet("carnets/{codigo}")]
    [Authorize(Policy = Politicas.Portal)]
    public async Task<IActionResult> Carnet(string codigo, CancellationToken ct)
        => Reenviar(await core.GetAsync($"api/v1/verificacion/carnets/{PuenteCore.Segmento(codigo)}", Contexto("Verificación por QR"),
            "VerificarCarnet", ct));

    /// <summary>
    /// Para instituciones con convenio (NORTIC A4): ¿tiene certificación vigente? Exige el ámbito
    /// discapacidad.verificar, convenio vigente y el propósito de la consulta. Cada consulta queda en la bitácora.
    /// </summary>
    [HttpGet("condicion/{cedula}")]
    [Authorize]
    public Task<VerificacionInstitucional> Condicion(string cedula, [FromQuery] string? proposito, CancellationToken ct)
        => verificacion.VerificarAsync(Cliente, cedula, proposito, Contexto(proposito), ct);
}

/// <summary>Estado del SND: base de datos, Core y cortacircuitos. No pide API key.</summary>
[Route("api/v1/salud")]
[AllowAnonymous]
public sealed class SaludController(EjecutorSp db, ClienteCore core, RepositorioCircuito circuitos) : ControladorBase
{
    [HttpGet]
    public async Task<ActionResult<RespuestaSalud>> Obtener(CancellationToken ct)
    {
        var baseDatos = await db.ProbarConexionAsync(ct);
        var coreOk = await core.EstaDisponibleAsync(ct);
        var lista = baseDatos ? await circuitos.ListarAsync(ct) : [];
        var estado = baseDatos && coreOk ? "Disponible" : baseDatos ? "Degradado" : "NoDisponible";
        var respuesta = new RespuestaSalud(estado, baseDatos, coreOk, lista, DateTimeOffset.Now);
        return baseDatos ? Ok(respuesta) : StatusCode(StatusCodes.Status503ServiceUnavailable, respuesta);
    }
}
