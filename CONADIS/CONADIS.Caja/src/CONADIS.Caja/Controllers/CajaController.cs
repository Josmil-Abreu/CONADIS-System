using System.Security.Claims;
using System.Globalization;
using System.Text;
using CONADIS.Caja.Application;
using CONADIS.Caja.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace CONADIS.Caja.Controllers;
[Authorize(Roles="Cajero,SupervisorCaja")]
public sealed class CajaController(ICajaStore db,CajaService service,IConfiguration config,IHttpClientFactory clients):Controller
{
    Actor Actor=>new(User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub")??throw new UnauthorizedAccessException(),User.IsInRole("SupervisorCaja")?"SupervisorCaja":"Cajero",User.FindFirstValue("sucursal")??throw new UnauthorizedAccessException());
    public IActionResult Index(string vista="cobros")
    {
        var a=Actor;ViewBag.Actor=a;ViewBag.Vista=vista;ViewBag.Hoy=service.Hoy;ViewBag.Demo=config.GetValue<bool>("Caja:Demo");
        var sessions=db.Query<SesionCaja>().Where(x=>x.Sucursal==a.Sucursal && (a.Rol=="SupervisorCaja"||x.Cajero==a.Id)).ToList();
        ViewBag.Sesiones=sessions;ViewBag.Tasas=db.Query<Tasa>().ToList().Where(x=>x.Vigente(service.Hoy)).ToList();
        ViewBag.Ordenes=db.Query<OrdenCobro>().Where(x=>x.Sucursal==a.Sucursal&&!x.Pagada).ToList();
        var ids=sessions.Select(x=>x.Id).ToArray();
        ViewBag.Facturas=vista=="arqueo"?new List<FacturaCaja>():db.Query<FacturaCaja>().Where(x=>ids.Contains(x.SesionCajaId)).ToList().OrderByDescending(x=>x.Fecha).Take(100).ToList();
        var facturaIds=db.Query<FacturaCaja>().Where(x=>ids.Contains(x.SesionCajaId)).Select(x=>x.Id).ToArray();
        ViewBag.Notas=db.Query<NotaCredito>().Where(x=>facturaIds.Contains(x.FacturaId)).ToList();
        ViewBag.Secuencias=a.Rol=="SupervisorCaja"?db.Query<SecuenciaENcf>().ToList():new List<SecuenciaENcf>();return View();
    }
    [HttpPost] public async Task<IActionResult> Operar(CajaCommand command,string clave,CancellationToken ct)
    {
        if(!ModelState.IsValid){TempData["Error"]="Revise los importes y fechas del formulario.";return RedirectToAction(nameof(Index));}
        try{
            var result=await service.Ejecutar(Actor,clave,command,config.GetValue<decimal>("Caja:Tolerancia"),ct);
            if(!result.Ok)TempData["Error"]=result.Error;
            else {TempData["Mensaje"]="Operación registrada correctamente.";if(command.Accion is "cobrar" or "aprobar-nota")return RedirectToAction(nameof(Comprobante),new{id=result.Value});}
        }
        catch(DbUpdateException){TempData["Error"]="Otro usuario modificó los datos. Revise el estado antes de reintentar.";}
        catch(InvalidOperationException){TempData["Error"]="El servicio requerido no está disponible. Consulte al responsable de integración.";}
        return RedirectToAction(nameof(Index),new{vista=command.Accion is "arquear" or "cerrar" or "forzar-cierre"?"arqueo":"cobros"});
    }
    public IActionResult Comprobante(Guid id)
    {
        var f=service.FindFactura(Actor,id);if(f==null)return NotFound();return View(f);
    }
    public async Task<IActionResult> Reporte(string tipo,Guid? id,DateOnly? desde,DateOnly? hasta,CancellationToken ct)
    {
        var a=Actor;string name;string parameters;
        if(tipo is "comprobante" or "termico" && id.HasValue && service.FindFactura(a,id.Value)!=null){name=tipo=="termico"?"Comprobante80mm":"Comprobante";parameters="&FacturaId="+id;}
        else if(tipo=="cierre" && id.HasValue && db.Query<SesionCaja>().Any(x=>x.Id==id&&x.Sucursal==a.Sucursal&&x.Estado==EstadoSesion.Cerrada&&(a.Rol=="SupervisorCaja"||x.Cajero==a.Id))){name="CierreCaja";parameters="&SesionId="+id;}
        else if(tipo is "ingresos" or "pendientes" && a.Rol=="SupervisorCaja" && desde.HasValue && hasta.HasValue && desde<=hasta && hasta.Value.DayNumber-desde.Value.DayNumber<=366){name=tipo=="ingresos"?"IngresosPorConcepto":"PendientesDGII";parameters="&Desde="+desde.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"&Hasta="+hasta.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
        else return BadRequest("Revise el reporte y los filtros solicitados.");
        if(!Uri.TryCreate(config["Ssrs:ReportServer"],UriKind.Absolute,out var server)||server.Scheme!="https"){TempData["Error"]="El servidor de reportes SSRS aún no está configurado.";return RedirectToAction(nameof(Index),new{vista="reportes"});}
        var folder=config["Ssrs:Folder"]??"/CONADIS/Caja";
        var url=server.AbsoluteUri.TrimEnd('/')+"?"+Uri.EscapeDataString(folder+"/"+name)+"&rs:Command=Render&rs:Format=PDF&Sucursal="+Uri.EscapeDataString(a.Sucursal)+parameters;
        using var response=await clients.CreateClient("ssrs").GetAsync(url,ct);
        if(!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType!="application/pdf")return Problem("No fue posible generar el reporte. Consulte al administrador de reportes.",statusCode:502);
        return File(await response.Content.ReadAsByteArrayAsync(ct),"application/pdf",name+".pdf");
    }
    public IActionResult Contabilidad(Guid id)
    {
        var a=Actor;if(a.Rol!="SupervisorCaja")return Forbid();
        if(!db.Query<SesionCaja>().Any(x=>x.Id==id&&x.Sucursal==a.Sucursal&&x.Estado==EstadoSesion.Cerrada))return NotFound();
        var rows=db.Query<FacturaCaja>().Where(x=>x.SesionCajaId==id).ToList();
        var csv=new StringBuilder("Cierre,Cuenta,Moneda,Subtotal,Impuesto,Total\r\n");
        foreach(var group in rows.GroupBy(x=>x.Cuenta))
        {
            decimal Sum(Func<FacturaCaja,decimal> f)=>group.Sum(x=>(x.Tipo==TipoECF.E34?-1:1)*f(x));
            csv.AppendLine(FormattableString.Invariant($"{id},{group.Key},DOP,{Sum(x=>x.Subtotal):0.00},{Sum(x=>x.Itbis):0.00},{Sum(x=>x.Total):0.00}"));
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()),"text/csv","Ingresos-"+id+".csv");
    }
    [AllowAnonymous] public IActionResult Error()=>Problem("No se pudo completar la operación. Consulte el estado antes de reintentar.",statusCode:500);
    [AllowAnonymous] public IActionResult Denegado()=>StatusCode(403,"No tiene permiso para acceder a esta función.");
}
