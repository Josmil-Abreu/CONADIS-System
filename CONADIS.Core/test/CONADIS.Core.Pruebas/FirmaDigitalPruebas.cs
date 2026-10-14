using System.Net;
using System.Text;
using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Servicios;

namespace CONADIS.Core.Pruebas;

/// <summary>
/// Pruebas unitarias del cliente de firma (no necesitan red ni base de datos):
/// se simula la respuesta del módulo de Integración con un HttpMessageHandler falso.
/// </summary>
public class FirmaDigitalPruebas
{
    [Fact]
    public async Task Envia_el_contenido_a_Integracion_y_devuelve_su_firma()
    {
        var falso = new ManejadorHttpFalso(HttpStatusCode.OK, """{"firma":"FIRMA-SIMULADA-0123456789"}""");
        var firma = new FirmaDigitalIntegracion(new HttpClient(falso) { BaseAddress = new Uri("https://integracion.local/") });

        var resultado = await firma.FirmarAsync("CONADIS|CARNET|1|abc");

        Assert.Equal("FIRMA-SIMULADA-0123456789", resultado);
        Assert.Equal(HttpMethod.Post, falso.Peticion!.Method);
        Assert.Equal("/api/v1/firma", falso.Peticion.RequestUri!.AbsolutePath);
        Assert.Contains("CONADIS|CARNET|1|abc", falso.CuerpoEnviado);
    }

    [Fact]
    public async Task Si_Integracion_responde_error_se_lanza_excepcion()
    {
        var falso = new ManejadorHttpFalso(HttpStatusCode.ServiceUnavailable, "{}");
        var firma = new FirmaDigitalIntegracion(new HttpClient(falso) { BaseAddress = new Uri("https://integracion.local/") });

        await Assert.ThrowsAsync<HttpRequestException>(() => firma.FirmarAsync("contenido"));
    }

    [Fact]
    public async Task Con_Integracion_caida_el_carnet_no_se_emite_y_la_base_ni_se_toca()
    {
        var falso = new ManejadorHttpFalso(HttpStatusCode.InternalServerError, "{}");
        var firma = new FirmaDigitalIntegracion(new HttpClient(falso) { BaseAddress = new Uri("https://integracion.local/") });

        // Cadena de conexión a un servidor inexistente: si el servicio intentara usar la base, fallaría distinto
        var carnets = new ServicioCarnets(new EjecutorSp("Server=servidor-que-no-existe;Database=X;Connect Timeout=1"), firma);
        var analista = new UsuarioSesion(1, "analista", "Analista", "a@conadis.gob.do", Roles.AnalistaRegistro, null, null);

        await Assert.ThrowsAsync<FirmaNoDisponibleException>(() => carnets.EmitirAsync(analista, 1));
    }
}

/// <summary>Simula un servidor HTTP: guarda la petición recibida y responde lo indicado.</summary>
internal sealed class ManejadorHttpFalso(HttpStatusCode estado, string cuerpoRespuesta) : HttpMessageHandler
{
    public HttpRequestMessage? Peticion { get; private set; }
    public string? CuerpoEnviado { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        Peticion = peticion;
        CuerpoEnviado = peticion.Content is null ? null : await peticion.Content.ReadAsStringAsync(ct);
        return new HttpResponseMessage(estado) { Content = new StringContent(cuerpoRespuesta, Encoding.UTF8, "application/json") };
    }
}