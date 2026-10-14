using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Externos;
using CONADIS.Web.Negocio.Servicios;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CONADIS.Web.Negocio;

/// <summary>
/// Registra la lógica del portal en el contenedor de dependencias. En el Program.cs del portal:
///     builder.Services.AgregarLogicaPortal(builder.Configuration);
/// Lee: ConnectionStrings:WebDB, Integracion:UrlBase e Integracion:ApiKey.
/// </summary>
public static class ConfiguracionPortal
{
    public const string EncabezadoApiKey = "X-Api-Key";

    public static IServiceCollection AgregarLogicaPortal(this IServiceCollection servicios, IConfiguration config)
    {
        servicios.AddSingleton(_ => new EjecutorSp(config.GetConnectionString("WebDB")));
        servicios.AddHttpClient<ClienteSnd>(http =>
        {
            http.BaseAddress = new Uri(config["Integracion:UrlBase"] ?? "https://localhost:7102/");
            http.Timeout = TimeSpan.FromSeconds(30);
            var clave = config["Integracion:ApiKey"];
            if (!string.IsNullOrWhiteSpace(clave)) http.DefaultRequestHeaders.Add(EncabezadoApiKey, clave);
        });
        servicios.AddScoped<ServicioCuentas>();
        servicios.AddScoped<ServicioPortal>();
        servicios.AddScoped<ServicioBorradores>();
        servicios.AddScoped<ServicioReportesPortal>();
        return servicios;
    }
}
