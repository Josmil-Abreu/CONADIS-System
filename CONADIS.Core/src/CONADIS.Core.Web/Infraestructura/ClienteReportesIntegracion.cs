using System.Net;

namespace CONADIS.Core.Web.Infraestructura;

/// <summary>Archivo devuelto por Integración (PDF o Excel).</summary>
public sealed record ArchivoRemoto(byte[] Contenido, string TipoContenido, string NombreArchivo);

/// <summary>
/// Pide a Integración sus reportes RDLC (api/v1/reportes) con la API key del Core (ámbito "reportes").
/// El Core no lee la base de Integración: cada módulo es dueño de sus datos.
/// </summary>
public sealed class ClienteReportesIntegracion(HttpClient http)
{
    public Task<ArchivoRemoto> PeriodoAsync(string reporte, DateOnly desde, DateOnly hasta, string formato, string usuario, CancellationToken ct)
        => DescargarAsync($"api/v1/reportes/{Uri.EscapeDataString(reporte)}?desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}&formato={formato}", usuario, ct);

    public Task<ArchivoRemoto> ComprobanteAsync(long id, string formato, string usuario, CancellationToken ct)
        => DescargarAsync($"api/v1/reportes/transacciones/{id}?formato={formato}", usuario, ct);

    private async Task<ArchivoRemoto> DescargarAsync(string ruta, string usuario, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        peticion.Headers.TryAddWithoutValidation("X-Usuario-Final", $"personal:{usuario}");
        HttpResponseMessage r;
        try
        {
            r = await http.SendAsync(peticion, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new CONADIS.Core.Negocio.Datos.ReglaNegocioException(0, "Integración no está disponible en este momento. Intente más tarde.", ex);
        }

        using (r)
        {
            if (r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new CONADIS.Core.Negocio.Datos.ReglaNegocioException(0,
                    "Integración rechazó la clave del Core para reportes (ejecute 08_Procedimientos_Reportes.sql de Integración).");
            if (!r.IsSuccessStatusCode)
                throw new CONADIS.Core.Negocio.Datos.ReglaNegocioException(0, $"Integración no pudo generar el reporte ({(int)r.StatusCode}).");
            var nombre = r.Content.Headers.ContentDisposition?.FileNameStar ?? r.Content.Headers.ContentDisposition?.FileName ?? "reporte";
            return new ArchivoRemoto(await r.Content.ReadAsByteArrayAsync(ct),
                r.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", nombre.Trim('"'));
        }
    }
}
