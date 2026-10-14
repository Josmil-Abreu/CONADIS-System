using CONADIS.Core.Api.Contratos;
using CONADIS.Core.Negocio.Datos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Core.Api.Controladores;

/// <summary>Estado del servicio. Es el único endpoint que no pide API key.</summary>
[Route("api/v1/salud")]
[AllowAnonymous]
public sealed class SaludController(EjecutorSp db) : ControladorBase
{
    /// <summary>200 si la API y la base de datos responden; 503 si la base no responde.</summary>
    [HttpGet]
    [ProducesResponseType<RespuestaSalud>(StatusCodes.Status200OK)]
    [ProducesResponseType<RespuestaSalud>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RespuestaSalud>> Obtener(CancellationToken ct)
    {
        var baseDatos = await db.ProbarConexionAsync(ct);
        var respuesta = new RespuestaSalud(baseDatos ? "Disponible" : "Degradado", baseDatos, DateTimeOffset.Now);
        return baseDatos ? Ok(respuesta) : StatusCode(StatusCodes.Status503ServiceUnavailable, respuesta);
    }
}