using System.Data;
using CONADIS.Integracion.Negocio.Datos;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Datos de los reportes de Integración. Cada método devuelve el DataTable de un
/// procedimiento ppRpt_*, listo para usarse como DataSet de un reporte RDLC.
/// </summary>
public sealed class ServicioReportes(EjecutorSp sp)
{
    /// <summary>Catálogo de reportes (lo usará la API para listarlos y la pantalla para elegir).</summary>
    public static readonly IReadOnlyList<ReporteInfo> Catalogo =
    [
        new("MensajesPorEstado",  "Estado de la cola de mensajes",      RequiereFechas: true,  AdmiteOrigen: false),
        new("MensajesMuertos",    "Mensajes que requieren revisión",    RequiereFechas: false, AdmiteOrigen: false),
        new("PagosRecibidos",     "Pagos recibidos por día y medio",    RequiereFechas: true,  AdmiteOrigen: true),
        new("Disponibilidad",     "Disponibilidad de los servicios",    RequiereFechas: true,  AdmiteOrigen: false),
        new("ErroresPorServicio", "Errores por servicio y operación",   RequiereFechas: true,  AdmiteOrigen: false),
        new("UsoPorCliente",      "Uso de la API por módulo cliente",   RequiereFechas: true,  AdmiteOrigen: false)
    ];

    public sealed record ReporteInfo(string Nombre, string Titulo, bool RequiereFechas, bool AdmiteOrigen);

    /// <summary>Ejecuta un reporte por su nombre del catálogo.</summary>
    public Task<DataTable> ObtenerAsync(string nombre, DateTime? desde, DateTime? hasta, string? origen = null, CancellationToken ct = default)
    {
        var info = Catalogo.FirstOrDefault(r => r.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase))
                   ?? throw new RecursoNoEncontradoException($"No existe el reporte '{nombre}'.");

        if (!info.RequiereFechas) return MensajesMuertosAsync(ct);
        if (desde is null || hasta is null)
            throw new SolicitudInvalidaException("Este reporte necesita fecha inicial y final.");

        return info.Nombre switch
        {
            "MensajesPorEstado" => MensajesPorEstadoAsync(desde.Value, hasta.Value, ct),
            "PagosRecibidos" => PagosRecibidosAsync(desde.Value, hasta.Value, origen, ct),
            "Disponibilidad" => DisponibilidadAsync(desde.Value, hasta.Value, ct),
            "ErroresPorServicio" => ErroresPorServicioAsync(desde.Value, hasta.Value, ct),
            _ => UsoPorClienteAsync(desde.Value, hasta.Value, ct)
        };
    }

    public Task<DataTable> MensajesPorEstadoAsync(DateTime desde, DateTime hasta, CancellationToken ct = default)
        => ConRango("snd.ppRpt_MensajesPorEstado", desde, hasta, ct);

    public Task<DataTable> MensajesMuertosAsync(CancellationToken ct = default)
        => sp.ConsultarAsync("snd.ppRpt_MensajesMuertos", ct);

    public Task<DataTable> PagosRecibidosAsync(DateTime desde, DateTime hasta, string? origen = null, CancellationToken ct = default)
    {
        Validaciones.Rango(desde, hasta);
        if (origen is not null && origen is not ("CAJA" or "WEB"))
            throw new SolicitudInvalidaException("El origen debe ser CAJA o WEB.");
        return sp.ConsultarAsync("snd.ppRpt_PagosRecibidos", ct,
            Parametro.Fecha("@Desde", desde), Parametro.Fecha("@Hasta", hasta), Parametro.Texto("@Origen", origen, 10));
    }

    public Task<DataTable> DisponibilidadAsync(DateTime desde, DateTime hasta, CancellationToken ct = default)
        => ConRango("snd.ppRpt_Disponibilidad", desde, hasta, ct);

    public Task<DataTable> ErroresPorServicioAsync(DateTime desde, DateTime hasta, CancellationToken ct = default)
        => ConRango("snd.ppRpt_ErroresPorServicio", desde, hasta, ct);

    public Task<DataTable> UsoPorClienteAsync(DateTime desde, DateTime hasta, CancellationToken ct = default)
        => ConRango("snd.ppRpt_UsoPorCliente", desde, hasta, ct);

    private Task<DataTable> ConRango(string procedimiento, DateTime desde, DateTime hasta, CancellationToken ct)
    {
        Validaciones.Rango(desde, hasta);
        return sp.ConsultarAsync(procedimiento, ct, Parametro.Fecha("@Desde", desde), Parametro.Fecha("@Hasta", hasta));
    }
}
