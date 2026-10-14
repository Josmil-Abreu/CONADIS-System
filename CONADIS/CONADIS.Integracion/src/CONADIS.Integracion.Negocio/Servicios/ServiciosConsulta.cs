using log4net;
using CONADIS.Integracion.Negocio.Conectores;
using CONADIS.Integracion.Negocio.Contratos;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Patrón común de las consultas a servicios externos:
///   servicio disponible -> responde y se guarda en caché
///   servicio caído      -> se responde con la caché (marcada DesdeCache/Vencido)
///   caído y sin caché   -> ServicioExternoNoDisponibleException
/// </summary>
public abstract class ServicioConsultaBase(ServicioResiliencia resiliencia, ServicioCache cache)
{
    protected async Task<ResultadoConsulta<T>> ConsultarAsync<T>(
        string servicio, string operacion, string claveCache,
        Func<CancellationToken, Task<T>> llamada, Func<T, bool> guardarEnCache, CancellationToken ct)
    {
        try
        {
            var valor = await resiliencia.EjecutarAsync(servicio, operacion, llamada, ct);
            if (guardarEnCache(valor)) await cache.GuardarAsync(claveCache, servicio, valor, ct: ct);
            return new ResultadoConsulta<T>(valor, DesdeCache: false, Vencido: false, DateTime.UtcNow);
        }
        catch (ServicioExternoNoDisponibleException)
        {
            var guardado = await cache.ObtenerAsync<T>(claveCache, permitirVencido: true, ct);
            if (guardado is null) throw;

            LogManager.GetLogger(GetType()).Info(
                $"{servicio} no disponible: se respondió {operacion} desde la caché ({(guardado.Vencido ? "vencida" : "vigente")}).");
            return new ResultadoConsulta<T>(guardado.Valor, DesdeCache: true, guardado.Vencido, guardado.Actualizado);
        }
    }
}

/// <summary>Validación de cédulas contra la JCE.</summary>
public sealed class ServicioIdentidad(ServicioResiliencia resiliencia, ServicioCache cache, IConectorJce jce)
    : ServicioConsultaBase(resiliencia, cache)
{
    public Task<ResultadoConsulta<ValidacionJceDto>> ValidarCedulaAsync(string? cedula, CancellationToken ct = default)
    {
        var limpia = Validaciones.Cedula(cedula);
        return ConsultarAsync(CodigosServicio.Jce, "JCE.ValidarCedula", $"jce:cedula:{limpia}",
            c => jce.ValidarCedulaAsync(limpia, c),
            v => v.EsValida,    // solo se guardan las cédulas encontradas
            ct);
    }
}

/// <summary>Validación de diagnósticos CIE-10 contra el MSP.</summary>
public sealed class ServicioDiagnostico(ServicioResiliencia resiliencia, ServicioCache cache, IConectorMsp msp)
    : ServicioConsultaBase(resiliencia, cache)
{
    public Task<ResultadoConsulta<ValidacionMspDto>> ValidarAsync(ValidacionMspSolicitudDto solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var cedula = Validaciones.Cedula(solicitud.Cedula);
        var codigo = Validaciones.Requerido(solicitud.CodigoCie10, "código CIE-10", 10).ToUpperInvariant();

        return ConsultarAsync(CodigosServicio.Msp, "MSP.ValidarDiagnostico", $"msp:cie10:{cedula}:{codigo}",
            c => msp.ValidarDiagnosticoAsync(cedula, codigo, c),
            v => v.EsValido,
            ct);
    }
}

/// <summary>Firma digital (cierres de caja, actas, carnets).</summary>
public sealed class ServicioFirma(ServicioResiliencia resiliencia, IConectorFirma firma)
{
    public Task<FirmaDto> FirmarAsync(SolicitudFirmaDto solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var s = new SolicitudFirmaDto(
            Validaciones.Requerido(solicitud.Usuario, "usuario", 100),
            Validaciones.Requerido(solicitud.Referencia, "referencia", 100),
            Validaciones.Requerido(solicitud.Contenido, "contenido", 1_000_000));

        // Firmar no se cachea ni se encola: si el servicio no está, quien llama decide.
        return resiliencia.EjecutarAsync(CodigosServicio.Firma, "Firma.Firmar", c => firma.FirmarAsync(s, c), ct);
    }
}
