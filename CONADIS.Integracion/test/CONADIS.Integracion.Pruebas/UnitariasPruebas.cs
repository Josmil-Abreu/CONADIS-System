using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Externos;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Repositorios;
using CONADIS.Integracion.Negocio.Resiliencia;
using CONADIS.Integracion.Negocio.Servicios;

namespace CONADIS.Integracion.Pruebas;

/// <summary>Pruebas unitarias: no necesitan base de datos ni red.</summary>
public class SeguridadPruebas
{
    [Fact]
    public void La_huella_es_estable_e_ignora_guiones()
    {
        var h = Fabrica.Huella();
        Assert.Equal(h.Cedula("00112345678"), h.Cedula("001-1234567-8"));
        Assert.Equal(64, h.Cedula("00112345678").Length);
        Assert.NotEqual(h.Cedula("00112345678"), h.Cedula("00112345679"));
    }

    [Fact]
    public void La_huella_depende_de_la_clave_secreta()
    {
        var otra = new Negocio.Seguridad.HuellaPrivada("otra-clave-secreta-distinta-0123456789-abc");
        Assert.NotEqual(Fabrica.Huella().Cedula("00112345678"), otra.Cedula("00112345678"));
    }

    [Fact]
    public void Una_clave_de_huellas_corta_se_rechaza()
    {
        Assert.Throws<ArgumentException>(() => new Negocio.Seguridad.HuellaPrivada("corta"));
    }
}

public class SimuladoresPruebas
{
    private static readonly OpcionesSimulacion SinLatencia = new() { LatenciaMs = 0 };

    [Fact]
    public async Task La_JCE_valida_una_cedula_correcta_y_siempre_devuelve_los_mismos_datos()
    {
        var jce = new SimuladorJce(SinLatencia);
        var cedula = Fabrica.CedulaNueva();
        var a = await jce.ConsultarCedulaAsync(cedula);
        var b = await jce.ConsultarCedulaAsync(cedula);
        Assert.True(a.Valida);
        Assert.NotNull(a.Nombres);
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task La_JCE_rechaza_digito_incorrecto_y_cedulas_canceladas()
    {
        var jce = new SimuladorJce(SinLatencia);
        var c = Fabrica.CedulaNueva();
        Assert.False((await jce.ConsultarCedulaAsync(c[..10] + ((c[10] - '0' + 1) % 10))).Valida);
        Assert.False((await jce.ConsultarCedulaAsync(Fabrica.CedulaConPrefijo("999"))).Valida);
    }

    [Fact]
    public async Task Con_la_JCE_apagada_la_consulta_falla()
    {
        var jce = new SimuladorJce(new OpcionesSimulacion { LatenciaMs = 0, JceDisponible = false });
        await Assert.ThrowsAsync<HttpRequestException>(() => jce.ConsultarCedulaAsync(Fabrica.CedulaNueva()));
    }

    [Theory]
    [InlineData("CERT-123", "G80", EstadosDiagnostico.Validado)]
    [InlineData("CERT-123", "Z99", EstadosDiagnostico.NoCoincide)]
    [InlineData("000-999", "G80", EstadosDiagnostico.NoEncontrado)]
    public async Task El_MSP_clasifica_el_diagnostico(string certificado, string cie10, string esperado)
    {
        var msp = new SimuladorMsp(SinLatencia);
        Assert.Equal(esperado, await msp.ValidarDiagnosticoAsync(new ConsultaDiagnostico("00112345678", "Hospital", certificado, cie10)));
    }

    [Theory]
    [InlineData("101234567", true, true)]
    [InlineData("101234569", true, false)]
    [InlineData("001234567", false, false)]
    public async Task La_DGII_valida_el_RNC(string rnc, bool existe, bool activo)
    {
        var r = await new SimuladorDgii(SinLatencia).ConsultarRncAsync(rnc);
        Assert.Equal(existe, r.Existe);
        Assert.Equal(activo, r.Activo);
    }

    [Fact]
    public async Task La_DGII_acepta_un_ecf_bien_formado_y_rechaza_uno_mal_formado()
    {
        var dgii = new SimuladorDgii(SinLatencia);
        var bueno = new ComprobanteFiscal("E320000000001", "32", "401000001", null, 300, 0, DateTimeOffset.Now);
        Assert.Equal(EstadosEcf.Aceptado, (await dgii.EnviarComprobanteAsync(bueno)).Estado);
        Assert.Equal(EstadosEcf.Rechazado, (await dgii.EnviarComprobanteAsync(bueno with { Encf = "B0100000001" })).Estado);
    }

    [Fact]
    public async Task La_pasarela_aprueba_o_rechaza_segun_el_token()
    {
        var p = new SimuladorPasarela(SinLatencia);
        Assert.True((await p.CobrarAsync(new CobroEnLinea("OC-2026-0000001", 300, "tok_visa", "00112345678", "Prueba"))).Aprobado);
        Assert.False((await p.CobrarAsync(new CobroEnLinea("OC-2026-0000001", 300, "tok_rechazada", "00112345678", "Prueba"))).Aprobado);
    }
}

public class FirmaPruebas : IDisposable
{
    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"firma-prueba-{Guid.NewGuid():N}.pem");

    [Fact]
    public async Task Firma_ES256_verificable_y_ligada_al_contenido()
    {
        using var firma = new FirmaEcdsa(_ruta, new DateOnly(2030, 1, 1), new OpcionesSimulacion { LatenciaMs = 0 });
        var doc = await firma.FirmarAsync("CONADIS|CARNET|1|abc");

        Assert.Equal("ES256", doc.Algoritmo);
        Assert.Equal(3, doc.Firma.Split('.').Length);
        Assert.True(doc.Firma.Length is >= 20 and <= 600, "El Core exige entre 20 y 600 caracteres.");
        Assert.True(firma.Verificar("CONADIS|CARNET|1|abc", doc.Firma));
        Assert.False(firma.Verificar("CONADIS|CARNET|2|abc", doc.Firma));      // otro contenido
        Assert.False(firma.Verificar("CONADIS|CARNET|1|abc", doc.Firma + "x")); // firma alterada
    }

    [Fact]
    public async Task La_clave_se_guarda_y_se_reutiliza()
    {
        string f;
        using (var primera = new FirmaEcdsa(_ruta, new DateOnly(2030, 1, 1), new OpcionesSimulacion { LatenciaMs = 0 }))
            f = (await primera.FirmarAsync("hola")).Firma;

        using var segunda = new FirmaEcdsa(_ruta, new DateOnly(2030, 1, 1), new OpcionesSimulacion { LatenciaMs = 0 });
        Assert.True(segunda.Verificar("hola", f));
    }

    [Fact]
    public async Task Con_el_certificado_vencido_no_se_firma()
    {
        using var firma = new FirmaEcdsa(_ruta, new DateOnly(2020, 1, 1), new OpcionesSimulacion { LatenciaMs = 0 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => firma.FirmarAsync("hola"));
    }

    public void Dispose()
    {
        if (File.Exists(_ruta)) File.Delete(_ruta);
    }
}

public class ResilienciaPruebas
{
    [Fact]
    public async Task Exito_cierra_el_circuito_y_queda_en_la_bitacora()
    {
        var (ejecutor, bitacora, circuito) = Fabrica.Ejecutor();
        var r = await ejecutor.EjecutarAsync(ServiciosExternos.Jce, new OpcionesLlamada("Prueba"), Fabrica.Contexto(), _ => Task.FromResult(42));

        Assert.True(r.Exito);
        Assert.Equal(42, r.Valor);
        Assert.Equal(new[] { true }, circuito.Resultados);
        Assert.True(Assert.Single(bitacora.Entradas).Exito);
    }

    [Fact]
    public async Task Un_fallo_se_reintenta_3_veces_y_cuenta_una_sola_caida()
    {
        var (ejecutor, bitacora, circuito) = Fabrica.Ejecutor();
        var intentos = 0;
        var r = await ejecutor.EjecutarAsync<int>(ServiciosExternos.Msp, new OpcionesLlamada("Prueba"), Fabrica.Contexto(),
            _ => { intentos++; throw new HttpRequestException("caído"); });

        Assert.False(r.Exito);
        Assert.Equal(3, intentos);
        Assert.Equal(new[] { false }, circuito.Resultados);
        Assert.Equal(ResultadosLlamada.Error, Assert.Single(bitacora.Entradas).Resultado);
    }

    [Fact]
    public async Task Si_tiene_exito_en_el_segundo_intento_la_llamada_es_exitosa()
    {
        var (ejecutor, _, circuito) = Fabrica.Ejecutor();
        var intentos = 0;
        var r = await ejecutor.EjecutarAsync(ServiciosExternos.Msp, new OpcionesLlamada("Prueba"), Fabrica.Contexto(),
            _ => ++intentos == 1 ? throw new HttpRequestException("falla momentánea") : Task.FromResult("ok"));

        Assert.True(r.Exito);
        Assert.Equal(2, intentos);
        Assert.Equal(new[] { true }, circuito.Resultados);
    }

    [Fact]
    public async Task Sin_respuesta_a_tiempo_se_reporta_tiempo_agotado()
    {
        var (ejecutor, _, _) = Fabrica.Ejecutor();
        var r = await ejecutor.EjecutarAsync(ServiciosExternos.Jce, new OpcionesLlamada("Prueba", Reintentar: false), Fabrica.Contexto(),
            async t => { await Task.Delay(TimeSpan.FromSeconds(10), t); return 1; });

        Assert.False(r.Exito);
        Assert.Equal(ResultadosLlamada.TiempoAgotado, r.Resultado);
    }

    [Fact]
    public async Task Con_el_circuito_abierto_no_se_llama_al_servicio()
    {
        var (ejecutor, bitacora, _) = Fabrica.Ejecutor(circuitoAbierto: true);
        var llamado = false;
        var r = await ejecutor.EjecutarAsync(ServiciosExternos.Jce, new OpcionesLlamada("Prueba"), Fabrica.Contexto(),
            _ => { llamado = true; return Task.FromResult(1); });

        Assert.False(llamado);
        Assert.Equal(ResultadosLlamada.CircuitoAbierto, r.Resultado);
        Assert.Equal(ResultadosLlamada.CircuitoAbierto, Assert.Single(bitacora.Entradas).Resultado);
    }
}

public class IdentidadPruebas
{
    [Fact]
    public async Task La_segunda_consulta_sale_de_la_cache_sin_llamar_a_la_JCE()
    {
        var (ejecutor, bitacora, _) = Fabrica.Ejecutor();
        var jce = new JceContadora();
        var servicio = new ServicioIdentidad(jce, ejecutor, new CacheEnMemoria(), Fabrica.Huella());
        var cedula = Fabrica.CedulaNueva();

        var primera = await servicio.ValidarAsync(cedula, Fabrica.Contexto());
        var segunda = await servicio.ValidarAsync(cedula, Fabrica.Contexto());

        Assert.Equal(EstadosIdentidad.Validada, primera.Estado);
        Assert.Equal(primera.Nombres, segunda.Nombres);
        Assert.True(segunda.DesdeCache);
        Assert.Equal(1, jce.Llamadas);
        Assert.True(bitacora.Entradas[1].DesdeCache);
        // La bitácora guarda la huella, nunca la cédula
        Assert.DoesNotContain(bitacora.Entradas, e => e.ReferenciaHash == cedula);
    }

    [Fact]
    public async Task Si_la_JCE_no_responde_el_resultado_es_Pendiente()
    {
        var (ejecutor, _, circuito) = Fabrica.Ejecutor();
        var servicio = new ServicioIdentidad(new JceContadora(disponible: false), ejecutor, new CacheEnMemoria(), Fabrica.Huella());

        var r = await servicio.ValidarAsync(Fabrica.CedulaNueva(), Fabrica.Contexto());

        Assert.Equal(EstadosIdentidad.Pendiente, r.Estado);
        Assert.Equal(new[] { false }, circuito.Resultados);
    }

    [Fact]
    public async Task Una_cedula_mal_formada_es_invalida_sin_consultar_a_la_JCE()
    {
        var (ejecutor, _, _) = Fabrica.Ejecutor();
        var jce = new JceContadora();
        var r = await new ServicioIdentidad(jce, ejecutor, new CacheEnMemoria(), Fabrica.Huella()).ValidarAsync("123", Fabrica.Contexto());

        Assert.Equal(EstadosIdentidad.Invalida, r.Estado);
        Assert.Equal(0, jce.Llamadas);
    }

    [Fact]
    public async Task Si_el_MSP_no_responde_el_diagnostico_queda_en_validacion_manual()
    {
        var (ejecutor, _, _) = Fabrica.Ejecutor();
        var msp = new SimuladorMsp(new OpcionesSimulacion { LatenciaMs = 0, MspDisponible = false });
        var r = await new ServicioDiagnostico(msp, ejecutor, Fabrica.Huella())
            .ValidarAsync(new ConsultaDiagnostico(Fabrica.CedulaNueva(), "Hospital", "CERT-1", "G80"), Fabrica.Contexto());

        Assert.Equal(EstadosDiagnostico.ValidacionManual, r);
    }
}

public class ReglasPruebas
{
    private static ClienteAutenticado Cliente(string nombre, params string[] ambitos)
        => new(1, nombre, "Canal", ambitos, false, null, true);

    private static ServicioPagos ServicioPagos()
    {
        var (ejecutor, _, _) = Fabrica.Ejecutor();
        var core = new PuenteCore(new ClienteCore(new HttpClient { BaseAddress = new Uri("http://core.invalido/") }), ejecutor);
        return new ServicioPagos(new RepositorioPagos(new EjecutorSp("Server=no-existe;Database=X;Connect Timeout=1")), core,
            new SimuladorPasarela(new OpcionesSimulacion { LatenciaMs = 0 }), ejecutor);
    }

    private static PagoEntrante Pago(string origen) => new(Guid.NewGuid(), origen, "E320000000001", "COPIA-CERT", 1, 300m, "Efectivo",
        "00112345678", "Prueba", null, null, "CAJA-01", "cajero", DateTimeOffset.Now);

    [Fact]
    public async Task La_Caja_no_puede_registrar_un_pago_con_origen_WEB()
    {
        var e = await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            ServicioPagos().RecibirAsync(Pago("WEB"), Cliente("Caja", Ambitos.Caja), Fabrica.Contexto()));
        Assert.Contains("CAJA", e.Message);
    }

    [Fact]
    public async Task Un_sistema_sin_ambito_de_pagos_es_rechazado()
    {
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            ServicioPagos().RecibirAsync(Pago("CAJA"), Cliente("Core", Ambitos.Firma), Fabrica.Contexto()));
    }

    [Fact]
    public async Task Una_institucion_con_convenio_vencido_recibe_acceso_denegado_y_queda_registrada()
    {
        var bitacora = new BitacoraEnMemoria();
        var (ejecutor, _, _) = Fabrica.Ejecutor();
        var core = new PuenteCore(new ClienteCore(new HttpClient { BaseAddress = new Uri("http://core.invalido/") }), ejecutor);
        var servicio = new ServicioVerificacion(core, bitacora, Fabrica.Huella());
        var vencida = new ClienteAutenticado(9, "Institución Vencida", "Institucion", [Ambitos.Verificar], false, new DateOnly(2025, 12, 31), false);

        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            servicio.VerificarAsync(vencida, Fabrica.CedulaNueva(), "Beneficio de transporte", Fabrica.Contexto()));
        Assert.Equal("Denegado", Assert.Single(bitacora.Entradas).Resultado);
    }

    [Fact]
    public async Task El_servicio_de_firma_rechaza_contenido_vacio()
    {
        var (ejecutor, _, _) = Fabrica.Ejecutor();
        using var proveedor = new FirmaEcdsa(Path.Combine(Path.GetTempPath(), $"f-{Guid.NewGuid():N}.pem"), new DateOnly(2030, 1, 1),
            new OpcionesSimulacion { LatenciaMs = 0 });
        var servicio = new ServicioFirmaDigital(proveedor, ejecutor, new AlertasEnMemoria(), new ParametrosFijos());
        await Assert.ThrowsAsync<ReglaNegocioException>(() => servicio.FirmarAsync("", Fabrica.Contexto()));
    }

    [Fact]
    public async Task Alerta_cuando_el_certificado_esta_por_vencer()
    {
        var (ejecutor, _, _) = Fabrica.Ejecutor();
        var alertas = new AlertasEnMemoria();
        using var proveedor = new FirmaEcdsa(Path.Combine(Path.GetTempPath(), $"f-{Guid.NewGuid():N}.pem"),
            DateOnly.FromDateTime(DateTime.Today.AddDays(10)), new OpcionesSimulacion { LatenciaMs = 0 });

        var dias = await new ServicioFirmaDigital(proveedor, ejecutor, alertas, new ParametrosFijos()).RevisarVencimientoAsync();

        Assert.Equal(10, dias);
        Assert.StartsWith("Media|Firma|", Assert.Single(alertas.Mensajes));
    }
}
