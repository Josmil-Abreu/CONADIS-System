using System.Security.Claims;
using CONADIS.Caja.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CONADIS.Caja.Controllers;
[ApiController,Route("api/v1/caja"),Authorize(Roles="Cajero,SupervisorCaja")]
public sealed class CajaApiController(CajaService service,IConfiguration config):ControllerBase
{
    // Autenticación de sesión + X-CSRF-TOKEN. No admite identidad/sucursal aportadas en el JSON.
    [HttpPost("operaciones")]
    public async Task<IActionResult> Operar([FromBody]CajaCommand command,[FromHeader(Name="Idempotency-Key")]string clave,CancellationToken ct)
    {
        var id=User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub");var branch=User.FindFirstValue("sucursal");
        if(id==null||branch==null)return Forbid();
        var actor=new Actor(id,User.IsInRole("SupervisorCaja")?"SupervisorCaja":"Cajero",branch);
        var r=await service.Ejecutar(actor,clave,command,config.GetValue<decimal>("Caja:Tolerancia"),ct);
        return r.Ok?Ok(new{id=r.Value}):Problem(title:r.Code,detail:r.Error,statusCode:r.Code=="PERMISO"?403:409);
    }
}
