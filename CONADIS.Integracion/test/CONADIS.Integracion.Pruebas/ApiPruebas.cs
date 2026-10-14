using System.Net;
using System.Net.Http.Json;
using CONADIS.Integracion.Api.Controladores;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Seguridad;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CONADIS.Integracion.Pruebas;

/// <summary>Levanta la API de Integración en memoria. El Core apunta a una dirección inexistente a propósito.</summary>
public sealed class FabricaApi : WebApplicationFactory<Program>
{
    public const string ClaveCaja = "conadis-caja-demo-2026";
    public const string ClaveWeb = "conadis-web-demo-2026";
    public const string ClaveCore = "conadis-core-demo-2026";
    public const string ClaveInstitucionVencida = "institucion-vencida-2026";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:IntegracionDB", BaseDatosPrueba.Cadena);
        builder.UseSetting("Core:UrlBase", "http://127.0.0.1:9/");        // Core "caído": nadie escucha en el puerto 9
        builder.UseSetting("Simulacion:LatenciaMs", "0");
        builder.UseSetting("Firma:RutaClave", Path.Combine(Path.GetTempPath(), "conadis-firma-pruebas.pem"));
    }

    public HttpClient Cliente(string clave)
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Add(ApiKey.Encabezado, clave);
        return c;
    }
}

/// <summary>Pruebas HTTP de la API de Integración (necesitan la base CONADIS_IntegracionDB).</summary>
public class ApiPruebas(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    private static PagoEntrante Pago() => new(Guid.NewGuid(), "CAJA", "E320000000005", "COPIA-CERT", 1, 300m, "Efectivo",
        "00112345678", "Pagador API", null, null, "CAJA-01", "cajero.prueba", DateTimeOffset.Now);

    [Fact]
    public async Task Sin_clave_responde_401_y_con_un_ambito_ajeno_403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await fabrica.CreateClient().GetAsync("/api/v1/caja/tasas")).StatusCode);
        // El portal no puede usar los servicios de la Caja
        Assert.Equal(HttpStatusCode.Forbidden, (await fabrica.Cliente(FabricaApi.ClaveWeb).PostAsJsonAsync("/api/v1/caja/pagos", Pago())).StatusCode);
    }

    [Fact]
    public async Task La_Caja_cobra_aunque_el_Core_este_caido_y_un_reenvio_no_duplica()
    {
        var caja = fabrica.Cliente(FabricaApi.ClaveCaja);
        var pago = Pago();

        var r1 = await caja.PostAsJsonAsync("/api/v1/caja/pagos", pago);
        Assert.Equal(HttpStatusCode.Accepted, r1.StatusCode);
        var primero = await r1.Content.ReadFromJsonAsync<RespuestaRecepcionPago>();
        Assert.True(primero!.EsNuevo);
        Assert.Equal(EstadosPagoRecibido.Recibido, primero.Estado);   // en cola: el Core está caído

        var segundo = await (await caja.PostAsJsonAsync("/api/v1/caja/pagos", pago)).Content.ReadFromJsonAsync<RespuestaRecepcionPago>();
        Assert.False(segundo!.EsNuevo);

        var estado = await caja.GetFromJsonAsync<RespuestaRecepcionPago>($"/api/v1/caja/pagos/{pago.TransaccionId}");
        Assert.Equal(EstadosPagoRecibido.Recibido, estado!.Estado);
    }

    [Fact]
    public async Task Si_el_Core_esta_caido_las_consultas_responden_503()
    {
        var r = await fabrica.Cliente(FabricaApi.ClaveWeb).GetAsync("/api/v1/portal/catalogos/centros");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.True(r.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task El_Core_obtiene_una_firma_ES256()
    {
        var r = await fabrica.Cliente(FabricaApi.ClaveCore).PostAsJsonAsync("/api/v1/firma", new { contenido = "CONADIS|CARNET|1|abc" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var firma = await r.Content.ReadFromJsonAsync<FirmaDocumento>();
        Assert.Equal("ES256", firma!.Algoritmo);
        Assert.Equal(3, firma.Firma.Split('.').Length);
    }

    [Fact]
    public async Task Una_institucion_con_convenio_vencido_recibe_403()
    {
        var r = await fabrica.Cliente(FabricaApi.ClaveInstitucionVencida)
            .GetAsync($"/api/v1/verificacion/condicion/{Fabrica.CedulaNueva()}?proposito=Beneficio%20de%20transporte");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var problema = await r.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("convenio", problema!.Detail);
    }

    [Fact]
    public async Task La_identidad_se_valida_con_la_JCE_simulada()
    {
        var r = await fabrica.Cliente(FabricaApi.ClaveWeb).GetFromJsonAsync<RespuestaIdentidad>($"/api/v1/portal/identidad/{Fabrica.CedulaNueva()}");
        Assert.Equal(EstadosIdentidad.Validada, r!.Estado);
        Assert.NotNull(r.Nombres);
    }
}
