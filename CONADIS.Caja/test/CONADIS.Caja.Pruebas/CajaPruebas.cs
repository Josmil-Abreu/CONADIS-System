using System.Net;
using System.Text;
using CONADIS.Caja.Negocio.Datos;
using CONADIS.Caja.Negocio.Externos;
using CONADIS.Caja.Negocio.Modelos;
using CONADIS.Caja.Negocio.Servicios;
using Microsoft.Extensions.Logging.Abstractions;

namespace CONADIS.Caja.Pruebas;

internal static class Ayudas
{
    public static readonly string Cadena = Environment.GetEnvironmentVariable("CONADIS_CAJA_PRUEBAS")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=CONADIS_CajaDB;Trusted_Connection=True;TrustServerCertificate=True";

    public static ClienteIntegracionCaja Cliente(IntegracionFalsa falsa)
        => new(new HttpClient(falsa) { BaseAddress = new Uri("https://integracion.local/") });

    public static SolicitudCobro Solicitud(string medio = MediosPagoCaja.Efectivo, string documento = "40123456788",
        bool creditoFiscal = false, decimal? recibido = 1000m) => new()
    {
        DocumentoCliente = documento,
        NombreCliente = "Cliente de Prueba",
        QuiereCreditoFiscal = creditoFiscal,
        Lineas = [new LineaCobro("DUP-CARNET", 1)],
        MedioPago = medio,
        MontoRecibido = medio == MediosPagoCaja.Efectivo ? recibido : null,
        Autorizacion = medio == MediosPagoCaja.Tarjeta ? "AUT123" : null,
        Referencia = medio == MediosPagoCaja.Transferencia ? "TRF-001" : null
    };
}

/// <summary>Integración simulada: cada petición se responde con la función indicada; guarda las peticiones.</summary>
internal sealed class IntegracionFalsa(Func<HttpRequestMessage, (HttpStatusCode, string)> responder) : HttpMessageHandler
{
    public List<(string Ruta, string? Cuerpo)> Peticiones { get; } = [];

    public static IntegracionFalsa Fija(HttpStatusCode estado, string cuerpo) => new(_ => (estado, cuerpo));
    public static IntegracionFalsa Caida() => new(_ => throw new HttpRequestException("Conexión rechazada"));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        var cuerpo = peticion.Content is null ? null : await peticion.Content.ReadAsStringAsync(ct);
        lock (Peticiones) Peticiones.Add((peticion.RequestUri!.AbsolutePath, cuerpo));
        var (estado, texto) = responder(peticion);
        return new HttpResponseMessage(estado) { Content = new StringContent(texto, Encoding.UTF8, "application/json") };
    }
}

/// <summary>Pruebas unitarias de las reglas de cobro (sin base de datos ni red).</summary>
public class ReglasCobroPruebas
{
    private static readonly RespuestaRnc RncActivo = new(true, true, "EMPRESA DEMO SRL");

    [Fact]
    public void Sin_credito_fiscal_se_emite_consumo_E32()
        => Assert.Equal(TiposEcf.Consumo, ReglasCobro.ElegirTipo(Ayudas.Solicitud(), null, sinConexion: true));

    [Fact]
    public void Credito_fiscal_con_rnc_activo_es_E31_y_gubernamental_es_E45()
    {
        var s = Ayudas.Solicitud(documento: "401000001", creditoFiscal: true);
        Assert.Equal(TiposEcf.CreditoFiscal, ReglasCobro.ElegirTipo(s, RncActivo, false));
        Assert.Equal(TiposEcf.Gubernamental, ReglasCobro.ElegirTipo(s with { EsGubernamental = true }, RncActivo, false));
    }

    [Fact]
    public void Credito_fiscal_exige_rnc_valido_activo_y_conexion()
    {
        var conCedula = Ayudas.Solicitud(creditoFiscal: true);
        Assert.Equal(58101, Assert.Throws<ReglaNegocioException>(() => ReglasCobro.ElegirTipo(conCedula, RncActivo, false)).Codigo);
        var s = Ayudas.Solicitud(documento: "401000001", creditoFiscal: true);
        Assert.Equal(58102, Assert.Throws<ReglaNegocioException>(() => ReglasCobro.ElegirTipo(s, null, true)).Codigo);
        Assert.Equal(58103, Assert.Throws<ReglaNegocioException>(() => ReglasCobro.ElegirTipo(s, new RespuestaRnc(false, false, null), false)).Codigo);
        Assert.Equal(58104, Assert.Throws<ReglaNegocioException>(() => ReglasCobro.ElegirTipo(s, new RespuestaRnc(true, false, "X"), false)).Codigo);
    }

    [Fact]
    public void Validar_revisa_documento_lineas_y_medio_de_pago()
    {
        Assert.Equal("40123456788", ReglasCobro.Validar(Ayudas.Solicitud(documento: "401-2345678-8")));
        Assert.Equal(58109, Assert.Throws<ReglaNegocioException>(() => ReglasCobro.Validar(Ayudas.Solicitud(documento: "40123456789"))).Codigo);
        Assert.Equal(58110, Assert.Throws<ReglaNegocioException>(() => ReglasCobro.Validar(Ayudas.Solicitud(documento: "123"))).Codigo);
        Assert.Equal(58105, Assert.Throws<ReglaNegocioException>(() => ReglasCobro.Validar(Ayudas.Solicitud() with { Lineas = [] })).Codigo);
        Assert.Equal(58106, Assert.Throws<ReglaNegocioException>(() =>
            ReglasCobro.Validar(Ayudas.Solicitud() with { Lineas = [new LineaCobro("DUP-CARNET", 0)] })).Codigo);
        Assert.Equal(58107, Assert.Throws<ReglaNegocioException>(() =>
            ReglasCobro.Validar(Ayudas.Solicitud() with { Lineas = [new LineaCobro("DUP-CARNET", 1), new LineaCobro("dup-carnet", 2)] })).Codigo);
        Assert.Equal(58111, Assert.Throws<ReglaNegocioException>(() => ReglasCobro.Validar(Ayudas.Solicitud(recibido: null))).Codigo);
        Assert.Equal(58112, Assert.Throws<ReglaNegocioException>(() =>
            ReglasCobro.Validar(Ayudas.Solicitud(MediosPagoCaja.Tarjeta) with { Autorizacion = null })).Codigo);
        Assert.Equal(58113, Assert.Throws<ReglaNegocioException>(() =>
            ReglasCobro.Validar(Ayudas.Solicitud(MediosPagoCaja.Transferencia) with { Referencia = " " })).Codigo);
        Assert.Equal(58114, Assert.Throws<ReglaNegocioException>(() => ReglasCobro.Validar(Ayudas.Solicitud() with { MedioPago = "Cheque" })).Codigo);
    }

    [Fact]
    public void Calcula_totales_con_itbis_y_cambio()
    {
        var hoy = DateTimeOffset.Now;
        var tasas = new Dictionary<string, TasaLocal>
        {
            ["DUP-CARNET"] = new("DUP-CARNET", "Duplicado", 500m, 0m, true, hoy),
            ["CERT-ACCES"] = new("CERT-ACCES", "Certificación", 5000m, 18m, true, hoy),
            ["VIEJA"] = new("VIEJA", "Inactiva", 1m, 0m, false, hoy)
        };
        var (sub, itbis, total) = ReglasCobro.Calcular([new LineaCobro("DUP-CARNET", 2), new LineaCobro("CERT-ACCES", 1)], tasas);
        Assert.Equal(6000m, sub);
        Assert.Equal(900m, itbis);
        Assert.Equal(6900m, total);
        Assert.Throws<ReglaNegocioException>(() => ReglasCobro.Calcular([new LineaCobro("VIEJA", 1)], tasas));
        Assert.Equal(100m, ReglasCobro.Cambio(6900m, 7000m));
        Assert.Equal(58006, Assert.Throws<ReglaNegocioException>(() => ReglasCobro.Cambio(6900m, 500m)).Codigo);
    }

    [Fact]
    public void Traduce_estados_de_dgii_y_de_integracion()
    {
        Assert.Equal(EstadosDgii.Aceptado, ReglasCobro.EstadoDesdeDgii("Aceptado"));
        Assert.Equal(EstadosDgii.Rechazado, ReglasCobro.EstadoDesdeDgii("Rechazado"));
        Assert.Equal(EstadosDgii.Contingencia, ReglasCobro.EstadoDesdeDgii(null));
        Assert.Equal("Aplicado", ReglasCobro.EstadoLinea("Aplicado"));
        Assert.Equal("Recibido", ReglasCobro.EstadoLinea("Recibido"));
        Assert.Equal("Recibido", ReglasCobro.EstadoLinea("Otro"));
    }
}

/// <summary>Cliente de Integración (con un HttpMessageHandler falso).</summary>
public class ClienteIntegracionCajaPruebas
{
    [Fact]
    public async Task Consulta_rnc_y_404_es_null()
    {
        var falsa = IntegracionFalsa.Fija(HttpStatusCode.OK, """{"existe":true,"activo":true,"razonSocial":"EMPRESA DEMO SRL"}""");
        var r = await Ayudas.Cliente(falsa).RncAsync("401000001");
        Assert.Equal("EMPRESA DEMO SRL", r!.RazonSocial);
        Assert.Equal("/api/v1/caja/rnc/401000001", falsa.Peticiones[0].Ruta);

        Assert.Null(await Ayudas.Cliente(IntegracionFalsa.Fija(HttpStatusCode.NotFound, "")).OrdenAsync("ord-1"));
    }

    [Fact]
    public async Task Envia_el_pago_con_camelCase_y_lee_la_respuesta_202()
    {
        var tx = Guid.NewGuid();
        var falsa = IntegracionFalsa.Fija(HttpStatusCode.Accepted,
            $$"""{"transaccionId":"{{tx}}","estado":"Recibido","esNuevo":true,"fechaRecepcion":"2026-10-14T10:00:00-04:00","detalle":null}""");
        var pago = new PagoCaja(tx, SincronizadorCaja.OrigenCaja, "E320000000001", "DUP-CARNET", 1, 500m, "Efectivo", "40123456788", "Cliente", null, null,
            "CAJA-01", "cajero", DateTimeOffset.Now);
        var r = await Ayudas.Cliente(falsa).EnviarPagoAsync(pago, "cajero");
        Assert.True(r.EsNuevo);
        Assert.Equal("Recibido", r.Estado);
        Assert.Contains("\"transaccionId\"", falsa.Peticiones[0].Cuerpo);
        Assert.Contains("\"tasaCodigo\":\"DUP-CARNET\"", falsa.Peticiones[0].Cuerpo);
        Assert.Contains("\"origen\":\"CAJA\"", falsa.Peticiones[0].Cuerpo);   // Integración y el Core exigen CAJA en mayúsculas
    }

    [Fact]
    public async Task Errores_de_red_401_y_5xx_son_sin_conexion_y_4xx_es_regla_de_negocio()
    {
        await Assert.ThrowsAsync<SinConexionException>(() => Ayudas.Cliente(IntegracionFalsa.Caida()).RncAsync("401000001"));
        await Assert.ThrowsAsync<SinConexionException>(() =>
            Ayudas.Cliente(IntegracionFalsa.Fija(HttpStatusCode.ServiceUnavailable, "{}")).RncAsync("401000001"));
        await Assert.ThrowsAsync<SinConexionException>(() =>
            Ayudas.Cliente(IntegracionFalsa.Fija(HttpStatusCode.Unauthorized, "")).RncAsync("401000001"));
        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            Ayudas.Cliente(IntegracionFalsa.Fija(HttpStatusCode.UnprocessableEntity, """{"detail":"Tasa inactiva"}""")).RncAsync("1"));
        Assert.Equal("Tasa inactiva", ex.Message);
    }
}

/// <summary>
/// Flujo completo contra CONADIS_CajaDB (ejecute antes los scripts 01 a 05 de la caja).
/// Integración se simula: así la prueba no depende de que el SND esté encendido.
/// </summary>
public class CajaBaseDatosPruebas
{
    private readonly EjecutorSp _db = new(Ayudas.Cadena);

    [Fact]
    public async Task Login_de_cajero_y_contrasena_incorrecta()
    {
        var auth = new ServicioAutenticacionCaja(_db, NullLogger<ServicioAutenticacionCaja>.Instance);
        var ok = await auth.IniciarSesionAsync("cajero2", "Conadis2026!");
        Assert.True(ok.Exito, ok.Mensaje);
        Assert.Equal(RolesCaja.Cajero, ok.Usuario!.Rol);
        Assert.False((await auth.IniciarSesionAsync("cajero2", "incorrecta")).Exito);
        await auth.IniciarSesionAsync("cajero2", "Conadis2026!");   // reinicia el contador de intentos
        await Assert.ThrowsAsync<ReglaNegocioException>(() => auth.AutorizarSupervisorAsync("cajero2", "Conadis2026!"));
    }

    [Fact]
    public async Task Abrir_cobrar_sincronizar_nota_de_credito_arqueo_y_cierre()
    {
        var auth = new ServicioAutenticacionCaja(_db, NullLogger<ServicioAutenticacionCaja>.Instance);
        var cajero = (await auth.IniciarSesionAsync("cajero2", "Conadis2026!")).Usuario!;
        var supervisor = await auth.AutorizarSupervisorAsync("supervisor.caja", "Conadis2026!");

        // Integración simulada: DGII acepta, el pago queda Recibido y luego Aplicado.
        var aplicado = false;
        (HttpStatusCode, string) Aplicar()
        {
            aplicado = true;
            return (HttpStatusCode.OK, """{"transaccionId":"00000000-0000-0000-0000-000000000000","estado":"Aplicado","esNuevo":false,"fechaRecepcion":"2026-10-14T10:00:00-04:00"}""");
        }
        var falsa = new IntegracionFalsa(p => p.RequestUri!.AbsolutePath switch
        {
            "/api/v1/caja/ecf" => (HttpStatusCode.OK, """{"estado":"Aceptado","codigoSeguridad":"ABC123","trackId":"t1","mensaje":"ok"}"""),
            "/api/v1/caja/pagos" => (HttpStatusCode.Accepted, """{"transaccionId":"00000000-0000-0000-0000-000000000000","estado":"Recibido","esNuevo":true,"fechaRecepcion":"2026-10-14T10:00:00-04:00"}"""),
            "/api/v1/caja/tasas" => (HttpStatusCode.OK, """
                [{"codigo":"DUP-CARNET","descripcion":"Duplicado de carnet PCD","monto":500,"itbisPorcentaje":0,"activa":true},
                 {"codigo":"CERT-ACCES","descripcion":"Certificación de accesibilidad de obra/empresa","monto":5000,"itbisPorcentaje":0,"activa":true},
                 {"codigo":"COPIA-CERT","descripcion":"Copia certificada de certificación","monto":300,"itbisPorcentaje":0,"activa":true},
                 {"codigo":"CONST-REG","descripcion":"Constancia de registro","monto":200,"itbisPorcentaje":0,"activa":true}]
                """),
            var r when r.StartsWith("/api/v1/caja/pagos/") => Aplicar(),
            _ => (HttpStatusCode.NotFound, "")
        });
        var cliente = Ayudas.Cliente(falsa);
        var sesiones = new ServicioSesiones(_db, NullLogger<ServicioSesiones>.Instance);
        var cobro = new ServicioCobro(_db, cliente, NullLogger<ServicioCobro>.Instance);
        var sincronizador = new SincronizadorCaja(_db, cliente, cobro, NullLogger<SincronizadorCaja>.Instance);

        // Si una ejecución anterior dejó la sesión abierta, se cierra de forma administrativa.
        if (await sesiones.ObtenerAbiertaAsync(cajero.UsuarioId) is { } vieja)
            await sesiones.CerrarForzadoAsync(vieja.SesionId, supervisor, "Limpieza antes de la prueba automática");

        var caja = (await sesiones.CajasAsync()).Single(c => c.Codigo == "CAJA-02");
        var sesion = await sesiones.AbrirAsync(caja.CajaId, cajero, 1000m);
        Assert.Equal(EstadosSesion.Abierta, sesion.Estado);

        var emitido = await cobro.CobrarAsync(sesion, cajero, Ayudas.Solicitud(recibido: 600m));
        Assert.Equal(TiposEcf.Consumo, emitido.Tipo);
        Assert.StartsWith("E32", emitido.Encf);
        Assert.Equal(100m, emitido.Cambio);
        Assert.Equal(EstadosDgii.Aceptado, emitido.EstadoDgii);

        var r = await sincronizador.SincronizarAsync();
        Assert.True(r.Conectado);
        Assert.True(r.PagosEnviados >= 1);
        Assert.True(aplicado);
        var detalle = await cobro.ObtenerAsync(emitido.FacturaId);
        Assert.Equal("Aplicado", detalle!.Lineas[0].EstadoSincronizacion);

        var nota = await cobro.EmitirNotaCreditoAsync(emitido.FacturaId, sesion, supervisor, "Cobro duplicado por error del cajero");
        Assert.StartsWith("E34", nota.Encf);
        Assert.True((await cobro.ObtenerAsync(emitido.FacturaId))!.Anulada);

        var arqueo = await sesiones.ArquearAsync(sesion.SesionId, cajero, 1000m, 0m, 0m);
        Assert.Equal(1000m, arqueo.EfectivoEsperado);   // fondo + 500 − 500 (nota de crédito)
        Assert.False(arqueo.HayDiferencias);
        var cierre = await sesiones.CerrarAsync(sesion.SesionId, supervisor, null, "Prueba automática");
        Assert.Equal(0m, cierre.DiferenciaTotal);

        var rpt = await new ServicioReportesCaja(_db).CierreCajaAsync(sesion.SesionId);
        Assert.Equal(3, rpt.Tables.Count);
    }

    [Fact]
    public async Task Sin_conexion_la_caja_cobra_y_deja_el_ecf_en_contingencia()
    {
        var auth = new ServicioAutenticacionCaja(_db, NullLogger<ServicioAutenticacionCaja>.Instance);
        var cajero = (await auth.IniciarSesionAsync("cajero", "Conadis2026!")).Usuario!;
        var supervisor = await auth.AutorizarSupervisorAsync("supervisor.caja", "Conadis2026!");
        var cliente = Ayudas.Cliente(IntegracionFalsa.Caida());
        var sesiones = new ServicioSesiones(_db, NullLogger<ServicioSesiones>.Instance);
        var cobro = new ServicioCobro(_db, cliente, NullLogger<ServicioCobro>.Instance);

        if (await sesiones.ObtenerAbiertaAsync(cajero.UsuarioId) is { } vieja)
            await sesiones.CerrarForzadoAsync(vieja.SesionId, supervisor, "Limpieza antes de la prueba automática");
        var caja = (await sesiones.CajasAsync()).Single(c => c.Codigo == "CAJA-SANTIAGO");
        var sesion = await sesiones.AbrirAsync(caja.CajaId, cajero, 0m);

        var emitido = await cobro.CobrarAsync(sesion, cajero, Ayudas.Solicitud(MediosPagoCaja.Tarjeta));
        Assert.Equal(EstadosDgii.Contingencia, emitido.EstadoDgii);
        await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            cobro.CobrarAsync(sesion, cajero, Ayudas.Solicitud(documento: "401000001", creditoFiscal: true)));

        var r = await new SincronizadorCaja(_db, cliente, cobro, NullLogger<SincronizadorCaja>.Instance).SincronizarAsync();
        Assert.False(r.Conectado);
        Assert.True((await new SincronizadorCaja(_db, cliente, cobro, NullLogger<SincronizadorCaja>.Instance).ResumenAsync()).EcfEnContingencia >= 1);

        await sesiones.CerrarForzadoAsync(sesion.SesionId, supervisor, "Fin de la prueba sin conexión");
    }
}
