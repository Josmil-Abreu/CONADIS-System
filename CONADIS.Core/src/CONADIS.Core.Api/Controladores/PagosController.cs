using CONADIS.Core.Api.Contratos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Servicios;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Core.Api.Controladores;

/// <summary>Órdenes de cobro y pagos que llegan desde la Caja o el portal (siempre a través de Integración).</summary>
[Route("api/v1")]
public sealed class PagosController(ServicioPagos pagos) : ControladorBase
{
    /// <summary>Crea una orden de cobro. El monto lo calcula el Core con la tasa del catálogo.</summary>
    [HttpPost("ordenes")]
    [ProducesResponseType<OrdenCobro>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OrdenCobro>> CrearOrden(PeticionOrden p, CancellationToken ct)
    {
        var orden = await pagos.CrearOrdenAsync(p.TasaCodigo, p.Cantidad, p.Documento, p.NombrePagador, p.Referencia, Contexto(), ct);
        return StatusCode(StatusCodes.Status201Created, orden);
    }

    /// <summary>Consulta una orden (la Caja la usa para cobrar el monto exacto).</summary>
    [HttpGet("ordenes/{numeroOrden}")]
    [ProducesResponseType<OrdenCobro>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrdenCobro>> ObtenerOrden(string numeroOrden, CancellationToken ct)
    {
        var orden = await pagos.ObtenerOrdenAsync(numeroOrden, ct);
        return orden is null ? NotFound() : Ok(orden);
    }

    /// <summary>
    /// Registra un pago. Es idempotente: si el mismo TransaccionId llega dos veces (la Caja reenvía
    /// tras una caída), responde 200 con estado "Duplicado" y no lo cobra de nuevo (RIC-05).
    /// </summary>
    [HttpPost("pagos")]
    [ProducesResponseType<RespuestaPago>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RespuestaPago>> RegistrarPago(NuevoPago pago, CancellationToken ct)
        => Ok(new RespuestaPago(await pagos.RegistrarPagoAsync(pago, ct)));
}