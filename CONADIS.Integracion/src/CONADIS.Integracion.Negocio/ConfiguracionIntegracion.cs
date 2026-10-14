using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Externos;
using CONADIS.Integracion.Negocio.Repositorios;
using CONADIS.Integracion.Negocio.Resiliencia;
using CONADIS.Integracion.Negocio.Seguridad;
using CONADIS.Integracion.Negocio.Servicios;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CONADIS.Integracion.Negocio;

/// <summary>
/// Registra todos los componentes de Integración en el contenedor de dependencias.
/// La usan la API (Paso 9) y el servicio de Windows (Paso 10), así ambos se configuran igual.
/// Secciones de appsettings.json que lee: ConnectionStrings:IntegracionDB, Core, Seguridad, Firma, Simulacion.
/// </summary>
public static class ConfiguracionIntegracion
{
    public static IServiceCollection AgregarIntegracion(this IServiceCollection servicios, IConfiguration config, string carpetaBase)
    {
        // ---- Base de datos y repositorios (sin estado: una instancia para toda la aplicación)
        servicios.AddSingleton(_ => new EjecutorSp(config.GetConnectionString("IntegracionDB")));
        servicios.AddSingleton<RepositorioBitacora>().AddSingleton<IRepositorioBitacora>(sp => sp.GetRequiredService<RepositorioBitacora>());
        servicios.AddSingleton<RepositorioCircuito>().AddSingleton<IRepositorioCircuito>(sp => sp.GetRequiredService<RepositorioCircuito>());
        servicios.AddSingleton<RepositorioCache>().AddSingleton<IRepositorioCache>(sp => sp.GetRequiredService<RepositorioCache>());
        servicios.AddSingleton<RepositorioAlertas>().AddSingleton<IRepositorioAlertas>(sp => sp.GetRequiredService<RepositorioAlertas>());
        servicios.AddSingleton<RepositorioParametros>().AddSingleton<IParametros>(sp => sp.GetRequiredService<RepositorioParametros>());
        servicios.AddSingleton<RepositorioMensajes>();
        servicios.AddSingleton<RepositorioPagos>();
        servicios.AddSingleton<RepositorioIdempotencia>();
        servicios.AddSingleton<RepositorioClientes>();

        // ---- Seguridad y resiliencia
        servicios.AddSingleton(_ => new HuellaPrivada(config["Seguridad:ClaveHuella"]));
        servicios.AddSingleton<EjecutorResiliente>();

        // ---- Servicios externos (simulados; cambiar aquí por las implementaciones reales)
        var simulacion = new OpcionesSimulacion();
        config.GetSection("Simulacion").Bind(simulacion);
        if (!Path.IsPathRooted(simulacion.CarpetaBuzon)) simulacion.CarpetaBuzon = Path.Combine(carpetaBase, simulacion.CarpetaBuzon);
        servicios.AddSingleton(simulacion);
        servicios.AddSingleton<IServicioJce, SimuladorJce>();
        servicios.AddSingleton<IServicioMsp, SimuladorMsp>();
        servicios.AddSingleton<IServicioDgii, SimuladorDgii>();
        servicios.AddSingleton<IPasarelaPagos, SimuladorPasarela>();
        servicios.AddSingleton<IEnviadorNotificaciones, EnviadorArchivo>();
        servicios.AddSingleton<IProveedorFirma>(_ =>
        {
            var ruta = config["Firma:RutaClave"] ?? "claves/firma-conadis.pem";
            if (!Path.IsPathRooted(ruta)) ruta = Path.Combine(carpetaBase, ruta);
            var vence = DateOnly.TryParse(config["Firma:VenceCertificado"], out var v) ? v : new DateOnly(2027, 6, 30);
            return new FirmaEcdsa(ruta, vence, simulacion);
        });

        // ---- Cliente HTTP del Core (Integración se identifica con su API key)
        servicios.AddHttpClient<ClienteCore>(http =>
        {
            http.BaseAddress = new Uri(config["Core:UrlBase"] ?? "https://localhost:7101/");
            http.Timeout = TimeSpan.FromSeconds(60);   // el tiempo por llamada lo controla EjecutorResiliente
            var clave = config["Core:ApiKey"];
            if (!string.IsNullOrWhiteSpace(clave)) http.DefaultRequestHeaders.Add(ApiKey.Encabezado, clave);
        });

        // ---- Casos de uso
        servicios.AddScoped<PuenteCore>();
        servicios.AddScoped<ServicioReportesIntegracion>();
        servicios.AddScoped<ServicioIdentidad>();
        servicios.AddScoped<ServicioDiagnostico>();
        servicios.AddScoped<ServicioDgii>();
        servicios.AddScoped<ServicioFirmaDigital>();
        servicios.AddScoped<ServicioPortal>();
        servicios.AddScoped<ServicioVerificacion>();
        servicios.AddScoped<ServicioPagos>();
        servicios.AddScoped<ProcesadorCola>();
        servicios.AddScoped<TareasProgramadas>();
        servicios.AddScoped<ServicioOperadores>();
        return servicios;
    }
}
