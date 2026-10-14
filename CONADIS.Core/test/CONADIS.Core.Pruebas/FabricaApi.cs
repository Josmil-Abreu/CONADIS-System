using CONADIS.Core.Negocio.Seguridad;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CONADIS.Core.Pruebas;

/// <summary>
/// Levanta la API del Core en memoria (sin abrir puertos) para probarla con peticiones HTTP reales.
/// Usa la misma base de datos que el resto de las pruebas.
/// </summary>
public sealed class FabricaApi : WebApplicationFactory<Program>
{
    /// <summary>La clave de demostración de Integración (su hash está en appsettings.json de la API).</summary>
    public const string ClaveIntegracion = "conadis-integracion-demo-2026";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:CoreDB", BaseDatosPrueba.Cadena);
    }

    /// <summary>Cliente HTTP que ya envía la API key, como lo hará el módulo de Integración.</summary>
    public HttpClient ClienteIntegracion()
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Add(ApiKey.Encabezado, ClaveIntegracion);
        cliente.DefaultRequestHeaders.Add("X-Usuario-Final", "prueba-automatizada");
        return cliente;
    }
}