using CONADIS.Core.Negocio.Servicios;

namespace CONADIS.Core.Pruebas;

/// <summary>Pruebas de integración de lectura: confirman que los datos iniciales están cargados.</summary>
public class CatalogosPruebas
{
    private readonly ServicioCatalogos _servicio = new(BaseDatosPrueba.Ejecutor());

    [Fact]
    public async Task Existen_los_6_roles_y_los_10_usuarios_de_demostracion()
    {
        var usuarios = new ServicioUsuarios(BaseDatosPrueba.Ejecutor());
        Assert.Equal(6, (await usuarios.ListarRolesAsync()).Count);
        Assert.True((await usuarios.ListarAsync()).Count >= 10);
    }

    [Fact]
    public async Task El_baremo_vigente_tiene_4_rangos()
    {
        var rangos = await _servicio.ObtenerBaremoVigenteAsync();
        Assert.Equal(4, rangos.Count);
        Assert.All(rangos, r => Assert.Equal("BAR-2026.1", r.Version));
    }

    [Fact]
    public async Task El_buscador_CIF_encuentra_por_codigo_y_por_texto()
    {
        Assert.Contains(await _servicio.BuscarCodigosCifAsync("b2"), c => c.Codigo == "b210");
        Assert.Contains(await _servicio.BuscarCodigosCifAsync("andar"), c => c.Codigo == "d450");
    }

    [Fact]
    public async Task La_tasa_de_duplicado_de_carnet_esta_activa()
    {
        var tasas = await _servicio.ListarTasasAsync();
        Assert.Contains(tasas, t => t.Codigo == "DUP-CARNET" && t.Monto == 500m);
    }
}