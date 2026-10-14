using System.Data;
using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Reportes;
using CONADIS.Core.Negocio.Seguridad;
using CONADIS.Core.Negocio.Servicios;

namespace CONADIS.Core.Pruebas;

/// <summary>Reportes RDLC: que estén incrustados y que los datos se adapten al ReportViewer (sin base de datos).</summary>
public class ReportesPruebas
{
    public static readonly string[] Esperados =
    [
        "rptExpedientesPorEstado", "rptEstadisticaAnonimizada", "rptIngresos", "rptCarnetsEmitidos", "rptInformeValoracion",
        "rptIndicadoresTramite", "rptTransacciones", "rptComprobanteTransaccion", "rptAcuseSolicitud", "rptCarnetEmitido", "rptReciboPago"
    ];

    [Fact]
    public void Todos_los_rdlc_estan_incrustados()
    {
        var disponibles = GeneradorReportes.Disponibles().ToList();
        foreach (var r in Esperados) Assert.Contains(r, disponibles);
    }

    [Fact]
    public void Preparar_convierte_fechas_con_zona_y_binarios()
    {
        var t = new DataTable();
        t.Columns.Add("Fecha", typeof(DateTimeOffset));
        t.Columns.Add("Hash", typeof(byte[]));
        t.Columns.Add("Texto", typeof(string));
        var ahora = DateTimeOffset.Now;
        t.Rows.Add(ahora, new byte[] { 0xAB, 0x01 }, "x");

        var p = GeneradorReportes.Preparar(t);
        Assert.Equal(typeof(DateTime), p.Columns["Fecha"]!.DataType);
        Assert.Equal(ahora.LocalDateTime, p.Rows[0]["Fecha"]);
        Assert.Equal("AB01", p.Rows[0]["Hash"]);
        Assert.Equal("x", p.Rows[0]["Texto"]);
    }

    [Fact]
    public void Tabla_convierte_records_en_columnas()
    {
        var t = GeneradorReportes.Tabla([new TramiteResumen("T-1", "Certificacion", "Solicitado", DateTimeOffset.Now, "Yo")]);
        Assert.Equal(["Numero", "Tipo", "Estado", "Actualizado", "Persona"], t.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
        Assert.Equal(1, t.Rows.Count);
    }

    [Fact]
    public void Un_reporte_inexistente_da_un_error_claro()
        => Assert.Throws<InvalidOperationException>(() =>
            GeneradorReportes.Generar("rptNoExiste", new EncabezadoReporte("Core", "prueba"), FormatoReporte.Pdf, ("Datos", new DataTable())));

    [Fact]
    public async Task Un_rol_sin_permiso_no_obtiene_documentos()
    {
        // Sin base de datos: el rol se comprueba antes de consultar.
        var analista = new UsuarioSesion(1, "analista", "Analista", "a@conadis.gob.do", Roles.AnalistaRegistro, null, null);
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            new ServicioReportes(new EjecutorSp("Server=no-existe")).AcuseSolicitudAsync(
                analista with { Rol = Roles.Evaluador }, "TRA-1"));
    }
}

/// <summary>
/// Genera de verdad cada PDF con los datos de CONADIS_CoreDB (ejecute antes los scripts 01 a 11).
/// Si un .rdlc tiene un error de diseño o no coincide con su SP, esta prueba lo muestra.
/// </summary>
public class ReportesBaseDatosPruebas
{
    private static readonly UsuarioSesion Admin = new(1, "admin", "Administrador General", "admin@conadis.gob.do", Roles.Administrador, null, null);
    private readonly ServicioReportes _reportes = new(BaseDatosPrueba.Ejecutor());
    private static readonly DateOnly Desde = DateOnly.FromDateTime(DateTime.Today.AddYears(-1));
    private static readonly DateOnly Hasta = DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task Los_reportes_por_periodo_generan_pdf_y_excel()
    {
        var casos = new (string Rdlc, DataTable Datos)[]
        {
            ("rptExpedientesPorEstado", await _reportes.ExpedientesPorEstadoAsync(Admin, Desde, Hasta)),
            ("rptEstadisticaAnonimizada", await _reportes.EstadisticaAnonimizadaAsync(Admin, Desde, Hasta)),
            ("rptIngresos", await _reportes.IngresosAsync(Admin, Desde, Hasta)),
            ("rptCarnetsEmitidos", await _reportes.CarnetsEmitidosAsync(Admin, Desde, Hasta)),
            ("rptIndicadoresTramite", await _reportes.IndicadoresTramiteAsync(Admin, Desde, Hasta)),
            ("rptTransacciones", await _reportes.TransaccionesAsync(Admin, Desde, Hasta))
        };
        foreach (var (rdlc, datos) in casos)
        {
            var pdf = await _reportes.ArchivoAsync(Admin, rdlc, datos, FormatoReporte.Pdf, "Prueba automática");
            Assert.True(pdf.Contenido.Length > 0, rdlc);
            var xlsx = await _reportes.ArchivoAsync(Admin, rdlc, datos, FormatoReporte.Excel, "Prueba automática");
            Assert.True(xlsx.Contenido.Length > 0, rdlc);
        }
    }

    [Fact]
    public async Task Los_documentos_de_transaccion_generan_pdf()
    {
        var db = BaseDatosPrueba.Ejecutor();
        var transacciones = await _reportes.TransaccionesAsync(Admin, Desde, Hasta);
        Assert.True(transacciones.Rows.Count > 0, "Ejecute 99_PruebaFlujoCompleto.sql para tener transacciones.");
        var eventoId = transacciones.Rows[^1].EnteroLargo("EventoId");
        await Generar("rptComprobanteTransaccion", await _reportes.ComprobanteTransaccionAsync(Admin, eventoId));

        // Una solicitud, un carnet, una orden y una evaluación existentes (si las hay)
        var expedientes = await new ServicioExpedientes(db).BuscarAsync(Admin, null, "Carnetizado");
        foreach (var e in expedientes.Take(1))
        {
            var completo = (await new ServicioExpedientes(db).ObtenerAsync(e.ExpedienteId))!;
            await Generar("rptAcuseSolicitud", await _reportes.AcuseSolicitudAsync(Admin, completo.Solicitudes[0].NumeroTramite));
            if (completo.Carnets.Count > 0) await Generar("rptCarnetEmitido", await _reportes.CarnetEmitidoAsync(Admin, completo.Carnets[0].CarnetId));
            if (completo.Ordenes.Count > 0) await Generar("rptReciboPago", await _reportes.ReciboPagoAsync(Admin, completo.Ordenes[0].NumeroOrden));
            if (completo.Evaluaciones.Count > 0)
                await Generar("rptInformeValoracion", await _reportes.InformeValoracionAsync(Admin, completo.Evaluaciones[0].EvaluacionId));
        }
    }

    private async Task Generar(string rdlc, DataTable datos)
        => Assert.True((await _reportes.ArchivoAsync(Admin, rdlc, datos, FormatoReporte.Pdf)).Contenido.Length > 0, rdlc);
}
