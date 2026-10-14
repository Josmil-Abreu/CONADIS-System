using CONADIS.Integracion.Negocio;
using CONADIS.Integracion.Negocio.Contratos;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Servicios;

namespace CONADIS.Integracion.Pruebas;

/// <summary>
/// Pruebas contra CONADIS_IntegracionDB en LocalDB. Cada una se revierte al terminar.
/// </summary>
public class PruebasIntegracion
{
    /* ------------------------------- Seguridad ------------------------------ */

    [Fact]
    public async Task La_api_key_valida_identifica_al_cliente_y_la_invalida_no()
    {
        using var e = new Entorno();
        var seguridad = e.Obtener<ServicioSeguridad>();

        var caja = await seguridad.ValidarApiKeyAsync(Entorno.ApiKeyCaja);
        var web = await seguridad.ValidarApiKeyAsync(Entorno.ApiKeyWeb);

        Assert.Equal("CAJA", caja?.Codigo);
        Assert.Equal("WEB", web?.Codigo);
        Assert.Null(await seguridad.ValidarApiKeyAsync("clave-inventada"));
        Assert.Null(await seguridad.ValidarApiKeyAsync(null));
    }

    /* --------------------------------- Pagos -------------------------------- */

    [Fact]
    public async Task Un_cobro_de_caja_queda_guardado_con_su_mensaje_para_el_Core()
    {
        using var e = new Entorno();
        var pagos = e.Obtener<ServicioPagos>();
        var cobro = Entorno.Cobro();

        var acuse = await pagos.RegistrarCobroCajaAsync(await e.ClienteAsync(), cobro);
        var pago = await pagos.ObtenerAsync(cobro.TransaccionId);

        Assert.Equal(EstadosAcuse.Recibido, acuse.Estado);
        Assert.NotNull(pago);
        Assert.Equal(EstadosPago.Recibido, pago.Estado);
        Assert.Equal(EstadosMensaje.Pendiente, pago.EstadoMensaje);
        Assert.Equal(500m, pago.Total);
    }

    [Fact]
    public async Task Un_cobro_reenviado_no_se_duplica()
    {
        using var e = new Entorno();
        var pagos = e.Obtener<ServicioPagos>();
        var cliente = await e.ClienteAsync();
        var cobro = Entorno.Cobro();

        var primero = await pagos.RegistrarCobroCajaAsync(cliente, cobro);
        var segundo = await pagos.RegistrarCobroCajaAsync(cliente, cobro);

        Assert.Equal(EstadosAcuse.Recibido, primero.Estado);
        Assert.Equal(EstadosAcuse.Duplicado, segundo.Estado);
        Assert.Equal(primero.PagoId, segundo.PagoId);
    }

    [Fact]
    public async Task El_mismo_TransaccionId_con_otro_monto_se_rechaza()
    {
        using var e = new Entorno();
        var pagos = e.Obtener<ServicioPagos>();
        var cliente = await e.ClienteAsync();
        var id = Guid.NewGuid();

        await pagos.RegistrarCobroCajaAsync(cliente, Entorno.Cobro(id, 500m));
        var error = await Assert.ThrowsAsync<ReglaNegocioException>(
            () => pagos.RegistrarCobroCajaAsync(cliente, Entorno.Cobro(id, 900m)));

        Assert.Equal(50309, error.Codigo);
    }

    [Fact]
    public async Task Un_cobro_con_medio_de_pago_invalido_se_rechaza_antes_de_ir_a_la_base()
    {
        using var e = new Entorno();
        var cobro = Entorno.Cobro() with { MedioPago = "Bitcoin" };

        await Assert.ThrowsAsync<SolicitudInvalidaException>(
            () => e.Obtener<ServicioPagos>().RegistrarCobroCajaAsync(new ClienteApi(1, "CAJA", "Caja"), cobro));
    }

    [Fact]
    public async Task Pago_en_linea_aprobado_y_reenvio_sin_cobrar_otra_vez()
    {
        using var e = new Entorno();
        var pagos = e.Obtener<ServicioPagos>();
        var web = await e.ClienteAsync(Entorno.ApiKeyWeb);
        var pago = new PagoEnLineaDto(Guid.NewGuid(), "OC-2026-0000001", "DUP-CARNET", "Duplicado de carnet",
            1, 500m, 500m, Entorno.CedulaValida(), "Persona de Prueba", "PERSONA PRUEBA", "4111 1111 1111 1111");

        var acuse = await pagos.RegistrarPagoEnLineaAsync(web, pago);
        var repetido = await pagos.RegistrarPagoEnLineaAsync(web, pago);

        Assert.Equal(EstadosAcuse.Recibido, acuse.Estado);
        Assert.StartsWith("PAS-", acuse.ReferenciaPasarela);
        Assert.Equal(EstadosAcuse.Duplicado, repetido.Estado);
        Assert.True(await pagos.VerificarPagoAsync(acuse.ReferenciaPasarela!, 500m));
        Assert.False(await pagos.VerificarPagoAsync(acuse.ReferenciaPasarela!, 999m));
    }

    [Fact]
    public async Task Pago_en_linea_declinado_no_se_registra()
    {
        using var e = new Entorno();
        var pagos = e.Obtener<ServicioPagos>();
        var pago = new PagoEnLineaDto(Guid.NewGuid(), "OC-2026-0000002", "DUP-CARNET", "Duplicado de carnet",
            1, 500m, 500m, Entorno.CedulaValida(), "Persona de Prueba", "PERSONA PRUEBA", "4000 0000 0000 0002");

        var web = await e.ClienteAsync(Entorno.ApiKeyWeb);

        await Assert.ThrowsAsync<RechazoServicioExternoException>(() => pagos.RegistrarPagoEnLineaAsync(web, pago));
        Assert.Null(await pagos.ObtenerAsync(pago.TransaccionId));
    }

    /* ------------------------------ Procesador ------------------------------ */

    [Fact]
    public async Task El_procesador_entrega_el_pago_al_Core_y_lo_marca_Aplicado()
    {
        using var e = new Entorno();
        var pagos = e.Obtener<ServicioPagos>();
        var cobro = Entorno.Cobro();
        await pagos.RegistrarCobroCajaAsync(await e.ClienteAsync(), cobro);

        var resumen = await e.Obtener<ProcesadorCola>().ProcesarLoteAsync("prueba");
        var pago = await pagos.ObtenerAsync(cobro.TransaccionId);

        Assert.True(resumen.Entregados >= 1);
        Assert.Equal(EstadosPago.Aplicado, pago!.Estado);
        Assert.Equal(EstadosMensaje.Entregado, pago.EstadoMensaje);
    }

    [Fact]
    public async Task Con_el_Core_caido_el_pago_espera_en_la_cola_y_no_se_pierde()
    {
        using var e = new Entorno();
        var pagos = e.Obtener<ServicioPagos>();
        var cobro = Entorno.Cobro();
        await pagos.RegistrarCobroCajaAsync(await e.ClienteAsync(), cobro);

        e.Simuladores.Caer(CodigosServicio.Core);
        var resumen = await e.Obtener<ProcesadorCola>().ProcesarLoteAsync("prueba");
        var pago = await pagos.ObtenerAsync(cobro.TransaccionId);
        var mensaje = await e.Obtener<ServicioCola>().ObtenerAsync(pago!.MensajeGuid!.Value);

        Assert.True(resumen.Reintentar >= 1);
        Assert.Equal(EstadosPago.Recibido, pago.Estado);
        Assert.Equal(EstadosMensaje.Pendiente, mensaje!.Estado);
        Assert.Equal(1, mensaje.Intentos);
        Assert.True(mensaje.ProximoIntento > DateTime.UtcNow);
        Assert.Contains("no está disponible", mensaje.UltimoError);
    }

    [Fact]
    public async Task Un_pago_que_el_Core_rechaza_queda_Rechazado_sin_reintentos()
    {
        using var e = new Entorno();
        var pagos = e.Obtener<ServicioPagos>();
        var cobro = Entorno.Cobro(tasa: "RECHAZAR");
        await pagos.RegistrarCobroCajaAsync(await e.ClienteAsync(), cobro);

        await e.Obtener<ProcesadorCola>().ProcesarLoteAsync("prueba");
        var pago = await pagos.ObtenerAsync(cobro.TransaccionId);

        Assert.Equal(EstadosPago.Rechazado, pago!.Estado);
        Assert.Equal(EstadosMensaje.Entregado, pago.EstadoMensaje);
        Assert.Contains("no existe en el catálogo", pago.Observacion);
    }

    [Fact]
    public async Task El_mantenimiento_se_ejecuta_sin_errores()
    {
        using var e = new Entorno();
        var resumen = await e.Obtener<ProcesadorCola>().MantenimientoAsync();
        Assert.True(resumen.MensajesBorrados >= 0);
    }

    /* ------------------------ Consultas con caché (JCE/MSP) ------------------ */

    [Fact]
    public async Task La_JCE_valida_la_cedula_y_si_se_cae_responde_desde_la_cache()
    {
        using var e = new Entorno();
        var identidad = e.Obtener<ServicioIdentidad>();
        var cedula = Entorno.CedulaValida("4027654321");

        var enLinea = await identidad.ValidarCedulaAsync(cedula);
        e.Simuladores.Caer(CodigosServicio.Jce);
        var desdeCache = await identidad.ValidarCedulaAsync(cedula);

        Assert.True(enLinea.Valor.EsValida);
        Assert.False(enLinea.DesdeCache);
        Assert.True(desdeCache.DesdeCache);
        Assert.Equal(enLinea.Valor.Nombres, desdeCache.Valor.Nombres);
    }

    [Fact]
    public async Task La_JCE_caida_sin_cache_responde_no_disponible()
    {
        using var e = new Entorno();
        e.Simuladores.Caer(CodigosServicio.Jce);

        await Assert.ThrowsAsync<ServicioExternoNoDisponibleException>(
            () => e.Obtener<ServicioIdentidad>().ValidarCedulaAsync(Entorno.CedulaValida("1112223334")));
    }

    [Fact]
    public async Task Una_cedula_con_digito_incorrecto_no_es_valida()
    {
        using var e = new Entorno();
        var buena = Entorno.CedulaValida("2223334445");
        var mala = buena[..10] + (char)('0' + (buena[10] - '0' + 1) % 10);

        var r = await e.Obtener<ServicioIdentidad>().ValidarCedulaAsync(mala);

        Assert.False(r.Valor.EsValida);
    }

    [Fact]
    public async Task El_MSP_reconoce_diagnosticos_y_rechaza_formatos_invalidos()
    {
        using var e = new Entorno();
        var msp = e.Obtener<ServicioDiagnostico>();
        var cedula = Entorno.CedulaValida();

        var paralisis = await msp.ValidarAsync(new ValidacionMspSolicitudDto(cedula, "g80.1"));
        var invalido = await msp.ValidarAsync(new ValidacionMspSolicitudDto(cedula, "XYZ"));

        Assert.True(paralisis.Valor.EsValido);
        Assert.Equal("Parálisis cerebral infantil", paralisis.Valor.Descripcion);
        Assert.False(invalido.Valor.EsValido);
    }

    /* ----------------------------- Cortacircuito ---------------------------- */

    [Fact]
    public async Task Tras_varios_fallos_el_cortacircuito_se_abre_y_deja_de_llamar()
    {
        using var e = new Entorno();
        await e.UmbralCircuitoAsync(CodigosServicio.Jce, 3);
        var identidad = e.Obtener<ServicioIdentidad>();
        e.Simuladores.Caer(CodigosServicio.Jce);

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<ServicioExternoNoDisponibleException>(
                () => identidad.ValidarCedulaAsync(Entorno.CedulaValida($"555000000{i}")));

        // Aunque la JCE "vuelva", el circuito sigue abierto hasta que pase su tiempo
        e.Simuladores.Restaurar(CodigosServicio.Jce);
        var error = await Assert.ThrowsAsync<ServicioExternoNoDisponibleException>(
            () => identidad.ValidarCedulaAsync(Entorno.CedulaValida("5550000009")));
        var servicio = await e.Obtener<ServicioAdministracion>().ObtenerServicioAsync(CodigosServicio.Jce);

        Assert.Contains("cortacircuito", error.Message);
        Assert.Equal("Abierto", servicio.EstadoCircuito);
        Assert.False(await e.Obtener<ServicioResiliencia>().PuedeLlamarAsync(CodigosServicio.Jce));
    }

    /* --------------------------------- DGII --------------------------------- */

    [Fact]
    public async Task Con_la_DGII_caida_el_eCF_queda_en_cola_y_se_envia_al_volver()
    {
        using var e = new Entorno();
        var fiscal = e.Obtener<ServicioFiscal>();
        var factura = Factura();

        e.Simuladores.Caer(CodigosServicio.Dgii);
        var respuesta = await fiscal.EnviarAsync(await e.ClienteAsync(), factura);
        Assert.Equal(EstadosDgii.EnCola, respuesta.Estado);

        e.Simuladores.Restaurar(CodigosServicio.Dgii);
        var resumen = await e.Obtener<ProcesadorCola>().ProcesarLoteAsync("prueba");
        var mensaje = await e.Obtener<ServicioCola>().ObtenerAsync(Guid.Parse(respuesta.TrackId!));

        Assert.True(resumen.Entregados >= 1);
        Assert.Equal(EstadosMensaje.Entregado, mensaje!.Estado);
        Assert.Contains("Aceptado", mensaje.RespuestaJson);
    }

    [Fact]
    public async Task La_DGII_firma_acepta_y_valida_RNC()
    {
        using var e = new Entorno();
        var fiscal = e.Obtener<ServicioFiscal>();
        var factura = Factura();

        var documento = await fiscal.FirmarAsync(factura);
        var respuesta = await fiscal.EnviarAsync(await e.ClienteAsync(), factura);
        var rnc = await fiscal.ValidarRncAsync("101010101");

        Assert.True(documento.EsPrueba);
        Assert.Contains(factura.Encf, documento.Xml);
        Assert.Equal(EstadosDgii.Aceptado, respuesta.Estado);
        Assert.True(rnc.Valor);
    }

    [Fact]
    public async Task Un_eCF_con_totales_que_no_cuadran_se_rechaza()
    {
        using var e = new Entorno();
        var factura = Factura() with { Total = 999m };

        await Assert.ThrowsAsync<SolicitudInvalidaException>(() => e.Obtener<ServicioFiscal>().FirmarAsync(factura));
    }

    /* ------------------------- Eventos e idempotencia ------------------------ */

    [Fact]
    public async Task Un_evento_reenviado_no_se_encola_dos_veces()
    {
        using var e = new Entorno();
        var eventos = e.Obtener<ServicioEventos>();
        var cliente = await e.ClienteAsync();
        var evento = new EventoDto(Guid.NewGuid(), Guid.NewGuid(), "Caja.Cerrada", """{"total":1500.00}""");

        var primero = await eventos.PublicarAsync(cliente, evento);
        var segundo = await eventos.PublicarAsync(cliente, evento);

        Assert.False(primero.EraDuplicado);
        Assert.True(segundo.EraDuplicado);
        Assert.Equal(primero.MensajeGuid, segundo.MensajeGuid);
    }

    [Fact]
    public async Task Idempotencia_Nuevo_EnProceso_Repetido_y_Conflicto()
    {
        using var e = new Entorno();
        var idem = e.Obtener<ServicioIdempotencia>();
        var cliente = await e.ClienteAsync();
        var clave = Guid.NewGuid().ToString();

        var nuevo = await idem.IniciarAsync(cliente.ClienteId, clave, """{"a":1}""");
        var enProceso = await idem.IniciarAsync(cliente.ClienteId, clave, """{"a":1}""");
        await idem.CompletarAsync(cliente.ClienteId, clave, 201, """{"ok":true}""");
        var repetido = await idem.IniciarAsync(cliente.ClienteId, clave, """{"a":1}""");
        var conflicto = await idem.IniciarAsync(cliente.ClienteId, clave, """{"a":2}""");

        Assert.Equal(ResultadosIdempotencia.Nuevo, nuevo.Resultado);
        Assert.Equal(ResultadosIdempotencia.EnProceso, enProceso.Resultado);
        Assert.Equal(ResultadosIdempotencia.Repetido, repetido.Resultado);
        Assert.Equal(201, repetido.CodigoHttp);
        Assert.Equal("""{"ok":true}""", repetido.RespuestaJson);
        Assert.Equal(ResultadosIdempotencia.Conflicto, conflicto.Resultado);
    }

    /* -------------------------- Bitácora y reportes ------------------------- */

    [Fact]
    public async Task Cada_llamada_externa_queda_en_la_bitacora_con_su_correlacion()
    {
        using var e = new Entorno();
        await e.Obtener<ServicioIdentidad>().ValidarCedulaAsync(Entorno.CedulaValida());

        var registros = await e.Obtener<ServicioBitacora>().ListarAsync(
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1), correlacionId: e.CorrelacionId);

        var llamada = Assert.Single(registros);
        Assert.Equal("JCE", llamada.ServicioCodigo);
        Assert.Equal(Direcciones.Saliente, llamada.Direccion);
        Assert.True(llamada.Exitoso);
    }

    [Fact]
    public async Task Todos_los_reportes_devuelven_datos()
    {
        using var e = new Entorno();
        await e.Obtener<ServicioPagos>().RegistrarCobroCajaAsync(await e.ClienteAsync(), Entorno.Cobro());
        await e.Obtener<ProcesadorCola>().ProcesarLoteAsync("prueba");
        var reportes = e.Obtener<ServicioReportes>();
        var desde = DateTime.UtcNow.AddDays(-1);
        var hasta = DateTime.UtcNow.AddDays(1);

        foreach (var info in ServicioReportes.Catalogo)
        {
            var tabla = await reportes.ObtenerAsync(info.Nombre, desde, hasta);
            Assert.True(tabla.Columns.Count > 0, $"El reporte {info.Nombre} no devolvió columnas.");
        }

        var pagos = await reportes.PagosRecibidosAsync(desde, hasta, "CAJA");
        var disponibilidad = await reportes.DisponibilidadAsync(desde, hasta);
        Assert.True(pagos.Rows.Count > 0);
        Assert.Contains(disponibilidad.Rows.Cast<System.Data.DataRow>(), f => (string)f["Servicio"] == "CORE");
    }

    // -------------------------------------------------------------------------

    private static FacturaFiscalDto Factura() => new(
        Guid.NewGuid(), "E32" + Random.Shared.NextInt64(1, 9_999_999_999).ToString("D10"), "E32", "101010101",
        null, null, DateTime.UtcNow, 500m, 0m, 500m,
        [new LineaFacturaDto("Duplicado de carnet", 1, 500m, 0m)]);
}
