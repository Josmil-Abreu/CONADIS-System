using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Repositorios;
using CONADIS.Integracion.Negocio.Reportes;
using CONADIS.Integracion.Negocio.Seguridad;
using CONADIS.Integracion.Negocio.Servicios;

namespace CONADIS.Integracion.Pruebas;

public class ReportesIntegracionPruebas
{
    [Fact]
    public void Los_rdlc_de_integracion_estan_incrustados()
    {
        var disponibles = GeneradorReportes.Disponibles().ToList();
        foreach (var r in new[] { "rptTransaccionesIntegracion", "rptComprobanteIntegracion", "rptPagosRecibidos", "rptDisponibilidadServicios" })
            Assert.Contains(r, disponibles);
    }

    [Fact]
    public async Task Un_reporte_desconocido_o_un_rango_invalido_se_rechazan()
    {
        var s = new ServicioReportesIntegracion(new EjecutorSp("Server=no-existe"));
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        Assert.Equal(56042, (await Assert.ThrowsAsync<ReglaNegocioException>(() => s.PeriodoAsync("otro", hoy, hoy, FormatoReporte.Pdf, "prueba"))).Codigo);
        Assert.Equal(56040, (await Assert.ThrowsAsync<ReglaNegocioException>(() => s.PeriodoAsync("pagos", hoy, hoy.AddDays(-1), FormatoReporte.Pdf, "prueba"))).Codigo);
    }
}

/// <summary>Genera los PDF reales con CONADIS_IntegracionDB (scripts 01 a 08).</summary>
public class ReportesIntegracionBaseDatosPruebas
{
    private readonly ServicioReportesIntegracion _reportes = new(BaseDatosPrueba.Ejecutor());

    [Fact]
    public async Task El_core_tiene_el_ambito_de_reportes()
    {
        var core = await new RepositorioClientes(BaseDatosPrueba.Ejecutor()).ObtenerPorHashAsync(ApiKey.Hashear("conadis-core-demo-2026"));
        Assert.True(core!.TieneAmbito(Ambitos.Reportes));
    }

    [Fact]
    public async Task Los_reportes_de_integracion_generan_pdf_y_excel()
    {
        var desde = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        var hasta = DateOnly.FromDateTime(DateTime.Today);
        foreach (var r in ServicioReportesIntegracion.PorPeriodo.Keys)
        {
            Assert.NotEmpty((await _reportes.PeriodoAsync(r, desde, hasta, FormatoReporte.Pdf, "prueba")).Contenido);
            Assert.NotEmpty((await _reportes.PeriodoAsync(r, desde, hasta, FormatoReporte.Excel, "prueba")).Contenido);
        }
    }
}
