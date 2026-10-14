using CONADIS.Caja.Negocio.Datos;
using CONADIS.Caja.Negocio.Externos;
using CONADIS.Caja.Negocio.Servicios;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CONADIS.Caja.Negocio;

/// <summary>
/// Registra la lógica de la Caja. En el Program.cs de la aplicación de escritorio:
///     servicios.AgregarLogicaCaja(configuracion);
/// Lee: ConnectionStrings:CajaDB, Integracion:UrlBase e Integracion:ApiKey.
/// </summary>
public static class ConfiguracionCaja
{
    public const string EncabezadoApiKey = "X-Api-Key";

    public static IServiceCollection AgregarLogicaCaja(this IServiceCollection servicios, IConfiguration config)
    {
        servicios.AddSingleton(_ => new EjecutorSp(config.GetConnectionString("CajaDB")));
        servicios.AddHttpClient<ClienteIntegracionCaja>(http =>
        {
            http.BaseAddress = new Uri(config["Integracion:UrlBase"] ?? "https://localhost:7102/");
            http.Timeout = TimeSpan.FromSeconds(15);
            var clave = config["Integracion:ApiKey"];
            if (!string.IsNullOrWhiteSpace(clave)) http.DefaultRequestHeaders.Add(EncabezadoApiKey, clave);
        });
        servicios.AddTransient<ServicioAutenticacionCaja>();
        servicios.AddTransient<ServicioSesiones>();
        servicios.AddTransient<ServicioCobro>();
        servicios.AddTransient<SincronizadorCaja>();
        servicios.AddTransient<ServicioReportesCaja>();
        return servicios;
    }
}
