using System.Net;
using System.Net.Http.Json;
using CONADIS.Core.Api.Contratos;
using CONADIS.Core.Negocio.Modelos;
using Microsoft.AspNetCore.Mvc;

namespace CONADIS.Core.Pruebas;

/// <summary>
/// Pruebas de la API REST con peticiones HTTP reales (la API corre en memoria).
/// Comprueban la seguridad por API key, los códigos HTTP y el formato de los errores.
/// </summary>
public class ApiPruebas(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    [Fact]
    public async Task La_salud_responde_sin_clave_y_confirma_la_base_de_datos()
    {
        var respuesta = await fabrica.CreateClient().GetAsync("/api/v1/salud");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var salud = await respuesta.Content.ReadFromJsonAsync<RespuestaSalud>();
        Assert.True(salud!.BaseDatos);
    }

    [Fact]
    public async Task Sin_clave_o_con_clave_incorrecta_responde_401()
    {
        var sinClave = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await sinClave.GetAsync("/api/v1/catalogos/centros")).StatusCode);

        var claveMala = fabrica.CreateClient();
        claveMala.DefaultRequestHeaders.Add("X-Api-Key", "clave-inventada");
        Assert.Equal(HttpStatusCode.Unauthorized, (await claveMala.GetAsync("/api/v1/catalogos/centros")).StatusCode);
    }

    [Fact]
    public async Task Con_la_clave_de_Integracion_lista_los_centros()
    {
        var centros = await fabrica.ClienteIntegracion().GetFromJsonAsync<List<CentroValoracion>>("/api/v1/catalogos/centros");
        Assert.Contains(centros!, c => c.Nombre == DatosPrueba.CentroSede);
    }

    [Fact]
    public async Task Una_solicitud_web_solo_la_ve_su_cedula()
    {
        var cliente = fabrica.ClienteIntegracion();
        var cedula = DatosPrueba.CedulaNueva();

        var respuesta = await cliente.PostAsJsonAsync("/api/v1/solicitudes", new NuevaSolicitud
        {
            Canal = "Web",
            Tipo = "Certificacion",
            CedulaCuenta = cedula,
            Cedula = cedula,
            Nombres = "Prueba",
            Apellidos = "Api Portal",
            EstadoIdentidad = "Validada",
            EstadoDiagnostico = "Validado",
            Correo = "api@correo.do",
            ConsentimientoDatos = true
        });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var registrada = await respuesta.Content.ReadFromJsonAsync<SolicitudRegistrada>();

        var propia = await cliente.GetAsync($"/api/v1/ciudadanos/{cedula}/tramites/{registrada!.NumeroTramite}");
        Assert.Equal(HttpStatusCode.OK, propia.StatusCode);
        Assert.Equal("Solicitado", (await propia.Content.ReadFromJsonAsync<TramiteCiudadano>())!.Estado);

        var ajena = await cliente.GetAsync($"/api/v1/ciudadanos/{DatosPrueba.CedulaNueva()}/tramites/{registrada.NumeroTramite}");
        Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
    }

    [Fact]
    public async Task Una_regla_de_negocio_responde_422_con_ProblemDetails_y_correlacion()
    {
        var cliente = fabrica.ClienteIntegracion();
        cliente.DefaultRequestHeaders.Add("X-Correlacion-Id", "prueba-422");

        var respuesta = await cliente.PostAsJsonAsync("/api/v1/ordenes",
            new PeticionOrden("NO-EXISTE", 1, DatosPrueba.CedulaNueva(), "Pagador", null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        var problema = await respuesta.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Regla de negocio", problema!.Title);
        Assert.False(string.IsNullOrWhiteSpace(problema.Detail));
        Assert.Equal("prueba-422", respuesta.Headers.GetValues("X-Correlacion-Id").Single());
    }

    [Fact]
    public async Task Un_pago_reenviado_por_HTTP_no_se_duplica()
    {
        var cliente = fabrica.ClienteIntegracion();
        var cedula = DatosPrueba.CedulaNueva();

        var orden = await (await cliente.PostAsJsonAsync("/api/v1/ordenes",
            new PeticionOrden("COPIA-CERT", 1, cedula, "Pagador Http", null))).Content.ReadFromJsonAsync<OrdenCobro>();

        var pago = new NuevoPago(Guid.NewGuid(), "CAJA", "E320000000003", "COPIA-CERT", 1, orden!.Total, "Tarjeta",
            cedula, "Pagador Http", orden.NumeroOrden, null, "CAJA-01", "cajero", DateTimeOffset.Now);

        var primero = await (await cliente.PostAsJsonAsync("/api/v1/pagos", pago)).Content.ReadFromJsonAsync<RespuestaPago>();
        var segundo = await (await cliente.PostAsJsonAsync("/api/v1/pagos", pago)).Content.ReadFromJsonAsync<RespuestaPago>();

        Assert.Equal(EstadosPago.Aplicado, primero!.Estado);
        Assert.Equal(EstadosPago.Duplicado, segundo!.Estado);
    }

    [Fact]
    public async Task Un_cuerpo_incompleto_responde_400()
    {
        var respuesta = await fabrica.ClienteIntegracion().PostAsJsonAsync("/api/v1/pagos", new { origen = "CAJA" });
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }
}