using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Servicios;

namespace CONADIS.Core.Pruebas;

/// <summary>Órdenes de cobro y pagos idempotentes (lo que necesitarán la Caja y el portal).</summary>
public class PagosPruebas
{
    private readonly ServicioPagos _pagos = new(BaseDatosPrueba.Ejecutor());

    [Fact]
    public async Task El_precio_sale_del_catalogo_y_un_pago_reenviado_no_se_duplica()
    {
        var cedula = DatosPrueba.CedulaNueva();
        var orden = await _pagos.CrearOrdenAsync("COPIA-CERT", 2, cedula, "Pagador de Prueba", null, new ContextoAccion("prueba"));
        Assert.Equal(600m, orden.Total);           // 2 x RD$300 del catálogo
        Assert.Equal("Pendiente", orden.Estado);

        var pago = new NuevoPago(Guid.NewGuid(), "CAJA", "E320000000001", "COPIA-CERT", 2, 600m, "Efectivo",
            cedula, "Pagador de Prueba", orden.NumeroOrden, null, "CAJA-01", "cajero", DateTimeOffset.Now);

        Assert.Equal(EstadosPago.Aplicado, await _pagos.RegistrarPagoAsync(pago));
        Assert.Equal(EstadosPago.Duplicado, await _pagos.RegistrarPagoAsync(pago));   // la Caja lo reenvía tras una caída

        var pagada = await _pagos.ObtenerOrdenAsync(orden.NumeroOrden);
        Assert.Equal("Pagada", pagada!.Estado);
        Assert.Equal("E320000000001", pagada.NumeroComprobante);
    }

    [Fact]
    public async Task Una_orden_ya_pagada_no_acepta_otro_pago_distinto()
    {
        var cedula = DatosPrueba.CedulaNueva();
        var orden = await _pagos.CrearOrdenAsync("CONST-REG", 1, cedula, "Pagador de Prueba", null, new ContextoAccion("prueba"));

        NuevoPago Pago() => new(Guid.NewGuid(), "WEB", "E320000000002", "CONST-REG", 1, 200m, "EnLinea",
            cedula, "Pagador de Prueba", orden.NumeroOrden, null, null, null, DateTimeOffset.Now);

        await _pagos.RegistrarPagoAsync(Pago());
        await Assert.ThrowsAsync<ReglaNegocioException>(() => _pagos.RegistrarPagoAsync(Pago()));
    }

    [Fact]
    public async Task Una_tasa_inexistente_no_genera_orden()
    {
        await Assert.ThrowsAsync<ReglaNegocioException>(() => _pagos.CrearOrdenAsync(
            "NO-EXISTE", 1, DatosPrueba.CedulaNueva(), "Pagador", null, new ContextoAccion("prueba")));
    }
}