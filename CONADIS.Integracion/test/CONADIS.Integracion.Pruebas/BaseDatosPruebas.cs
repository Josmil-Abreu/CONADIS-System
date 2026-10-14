using System.Security.Cryptography;
using System.Text;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Repositorios;
using CONADIS.Integracion.Negocio.Seguridad;
using CONADIS.Integracion.Negocio.Servicios;

namespace CONADIS.Integracion.Pruebas;

/// <summary>
/// Pruebas contra la base CONADIS_IntegracionDB de LocalDB (ejecute antes los scripts 01 a 07).
/// Cada prueba usa identificadores nuevos para poder repetirse.
/// </summary>
public class BaseDatosPruebas
{
    private readonly EjecutorSp _db = BaseDatosPrueba.Ejecutor();

    [Fact]
    public async Task Las_api_keys_de_demostracion_se_reconocen_con_sus_ambitos()
    {
        var clientes = new RepositorioClientes(_db);

        var caja = await clientes.ObtenerPorHashAsync(ApiKey.Hashear("conadis-caja-demo-2026"));
        Assert.Equal("Caja", caja!.Nombre);
        Assert.True(caja.TieneAmbito(Ambitos.Caja));

        var vencida = await clientes.ObtenerPorHashAsync(ApiKey.Hashear("institucion-vencida-2026"));
        Assert.False(vencida!.ConvenioVigente);

        Assert.Null(await clientes.ObtenerPorHashAsync(ApiKey.Hashear("clave-inventada")));
    }

    [Fact]
    public async Task Un_pago_reenviado_no_se_duplica_y_queda_encolado_para_el_Core()
    {
        var caja = (await new RepositorioClientes(_db).ObtenerPorHashAsync(ApiKey.Hashear("conadis-caja-demo-2026")))!;
        var pagos = new RepositorioPagos(_db);
        var pago = new PagoEntrante(Guid.NewGuid(), "CAJA", "E320000000001", "COPIA-CERT", 1, 300m, "Efectivo",
            "00112345678", "Prueba BD", null, null, "CAJA-01", "cajero", DateTimeOffset.Now);
        var carga = System.Text.Json.JsonSerializer.Serialize(pago);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(carga)));

        var primero = await pagos.RecibirAsync(pago, caja.ClienteId, hash, carga, "prueba", default);
        var segundo = await pagos.RecibirAsync(pago, caja.ClienteId, hash, carga, "prueba", default);

        Assert.True(primero.EsNuevo);
        Assert.False(segundo.EsNuevo);
        Assert.Equal(EstadosPagoRecibido.Recibido, segundo.Pago.Estado);

        // Mismo identificador con otro contenido: conflicto (error 56020)
        var e = await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            pagos.RecibirAsync(pago with { Total = 1m }, caja.ClienteId, new string('F', 64), carga, null, default));
        Assert.Equal(56020, e.Codigo);

        // Otro cliente no ve el pago de la Caja
        var web = (await new RepositorioClientes(_db).ObtenerPorHashAsync(ApiKey.Hashear("conadis-web-demo-2026")))!;
        Assert.Null(await pagos.ObtenerAsync(pago.TransaccionId, web.ClienteId));

        await pagos.RegistrarRespuestaCoreAsync(pago.TransaccionId, EstadosPagoRecibido.Aplicado);
        Assert.Equal(EstadosPagoRecibido.Aplicado, (await pagos.ObtenerAsync(pago.TransaccionId, caja.ClienteId))!.Estado);
    }

    [Fact]
    public async Task La_cola_reprograma_un_fallo_y_completa_un_exito()
    {
        var cola = new RepositorioMensajes(_db);
        var clave = "PRUEBA-" + Guid.NewGuid().ToString("N");

        var encolado = await cola.EncolarAsync("Prueba.Tipo", "SMS", "{\"prueba\":true}", clave);
        Assert.True(encolado.Encolado);
        Assert.False((await cola.EncolarAsync("Prueba.Tipo", "SMS", "{\"prueba\":true}", clave)).Encolado);

        var trabajador = "prueba-" + Guid.NewGuid().ToString("N")[..8];
        var tomados = await cola.TomarAsync(trabajador, 500, destino: "SMS");
        Assert.Contains(tomados, m => m.MensajeId == encolado.MensajeId);

        var fallo = await cola.RegistrarFalloAsync(encolado.MensajeId, "fallo de prueba");
        Assert.Equal(EstadosMensaje.Pendiente, fallo.Estado);
        Assert.True(fallo.ProximoIntento > DateTimeOffset.Now);

        // Los demás mensajes tomados por esta prueba se devuelven a la cola
        foreach (var m in tomados.Where(m => m.MensajeId != encolado.MensajeId))
            await cola.RegistrarFalloAsync(m.MensajeId, "devuelto por la prueba", transitorio: true);
    }

    [Fact]
    public async Task Un_destino_caido_nunca_da_el_mensaje_por_perdido_y_reintenta_pronto()
    {
        var cola = new RepositorioMensajes(_db);
        var encolado = await cola.EncolarAsync("Prueba.Tipo", "SMS", "{\"prueba\":true}", "PRUEBA-" + Guid.NewGuid().ToString("N"));
        var trabajador = "prueba-" + Guid.NewGuid().ToString("N")[..8];
        var tomados = await cola.TomarAsync(trabajador, 500, destino: "SMS");

        var fallo = await cola.RegistrarFalloAsync(encolado.MensajeId, "SMS no disponible", transitorio: true);
        Assert.Equal(EstadosMensaje.Pendiente, fallo.Estado);
        Assert.True(fallo.ProximoIntento <= DateTimeOffset.Now.AddSeconds(31), "Con el destino caído se reintenta cada 30 s como máximo.");

        foreach (var m in tomados.Where(m => m.MensajeId != encolado.MensajeId))
            await cola.RegistrarFalloAsync(m.MensajeId, "devuelto por la prueba", transitorio: true);
    }

    [Fact]
    public async Task La_idempotencia_devuelve_la_respuesta_guardada()
    {
        var web = (await new RepositorioClientes(_db).ObtenerPorHashAsync(ApiKey.Hashear("conadis-web-demo-2026")))!;
        var repo = new RepositorioIdempotencia(_db);
        var clave = Guid.NewGuid().ToString("N");
        var hash = new string('A', 64);

        Assert.Equal(ResultadosIdempotencia.Nueva, (await repo.IniciarAsync(web.ClienteId, clave, "/api/v1/x", hash)).Resultado);
        Assert.Equal(ResultadosIdempotencia.EnProceso, (await repo.IniciarAsync(web.ClienteId, clave, "/api/v1/x", hash)).Resultado);
        await repo.CompletarAsync(web.ClienteId, clave, 201, "{\"ok\":true}");

        var repetida = await repo.IniciarAsync(web.ClienteId, clave, "/api/v1/x", hash);
        Assert.Equal(ResultadosIdempotencia.Repetida, repetida.Resultado);
        Assert.Equal(201, repetida.CodigoHttp);
        Assert.Equal(ResultadosIdempotencia.Conflicto, (await repo.IniciarAsync(web.ClienteId, clave, "/api/v1/x", new string('B', 64))).Resultado);
    }

    [Fact]
    public async Task Cinco_fallos_abren_el_circuito_y_un_exito_lo_cierra()
    {
        var circuito = new RepositorioCircuito(_db);
        var servicio = "PRUEBA-" + Guid.NewGuid().ToString("N")[..8];

        for (var i = 0; i < 5; i++) await circuito.RegistrarResultadoAsync(servicio, false, "fallo de prueba");
        var abierto = await circuito.PuedeLlamarAsync(servicio);
        Assert.False(abierto.Permitido);
        Assert.Equal("Abierto", abierto.Estado);

        await circuito.ReiniciarAsync(servicio);
        Assert.True((await circuito.PuedeLlamarAsync(servicio)).Permitido);
    }

    [Fact]
    public async Task La_bitacora_registra_y_las_estadisticas_la_cuentan()
    {
        var bitacora = new RepositorioBitacora(_db);
        var correlacion = "prueba-" + Guid.NewGuid().ToString("N")[..12];
        await bitacora.RegistrarAsync(new EntradaBitacora(ServiciosExternos.Jce, "Prueba", "Saliente", true, "Valida",
            Fabrica.Contexto() with { CorrelacionId = correlacion }, LatenciaMs: 50));

        var desde = DateTimeOffset.Now.AddMinutes(-5);
        var hasta = DateTimeOffset.Now.AddMinutes(5);
        Assert.Contains(await bitacora.ListarAsync(desde, hasta, ServiciosExternos.Jce), r => r.CorrelacionId == correlacion);
        Assert.Contains(await bitacora.EstadisticasAsync(desde, hasta), e => e.Servicio == ServiciosExternos.Jce && e.Total > 0);
    }

    [Fact]
    public async Task Los_operadores_inician_sesion_con_la_contrasena_de_demostracion()
    {
        var operadores = new ServicioOperadores(_db);
        var ok = await operadores.IniciarSesionAsync("monitor", "Conadis2026!");
        Assert.True(ok.Exito, ok.Mensaje);
        Assert.Equal(RolesOperador.Monitor, ok.Operador!.Rol);
        Assert.False((await operadores.IniciarSesionAsync("monitor", "incorrecta")).Exito);
    }
}
