using System.Net;
using System.Text;
using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Externos;
using CONADIS.Web.Negocio.Modelos;
using CONADIS.Web.Negocio.Seguridad;
using CONADIS.Web.Negocio.Servicios;

namespace CONADIS.Web.Pruebas;

internal static class Ayudas
{
    public static readonly string Cadena = Environment.GetEnvironmentVariable("CONADIS_WEB_PRUEBAS")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=CONADIS_WebDB;Trusted_Connection=True;TrustServerCertificate=True";

    public static string CedulaNueva()
    {
        var diez = "4" + Random.Shared.NextInt64(0, 1_000_000_000).ToString("D9");
        return diez + ValidadorCedula.CalcularDigito(diez);
    }

    public static CuentaSesion Sesion(string cedula = "40123456788")
        => new(1, cedula, "Ciudadano Prueba", "prueba@correo.do", "Validada", true, false, 100);

    /// <summary>Integración simulada: responde lo indicado y guarda la última petición.</summary>
    public static (ClienteSnd Cliente, IntegracionFalsa Falsa) Snd(HttpStatusCode estado, string cuerpo)
    {
        var falsa = new IntegracionFalsa(estado, cuerpo);
        return (new ClienteSnd(new HttpClient(falsa) { BaseAddress = new Uri("https://integracion.local/") }), falsa);
    }
}

internal sealed class IntegracionFalsa(HttpStatusCode estado, string cuerpo) : HttpMessageHandler
{
    public HttpRequestMessage? Peticion { get; private set; }
    public string? CuerpoEnviado { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        Peticion = peticion;
        CuerpoEnviado = peticion.Content is null ? null : await peticion.Content.ReadAsStringAsync(ct);
        return new HttpResponseMessage(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
    }
}

/// <summary>Pruebas unitarias (sin base de datos ni red).</summary>
public class ClienteSndPruebas
{
    [Fact]
    public async Task Envia_la_cedula_de_la_sesion_y_la_clave_de_idempotencia()
    {
        var (snd, falsa) = Ayudas.Snd(HttpStatusCode.Created,
            """{"numeroTramite":"TRA-2026-0000001","numeroExpediente":"EXP-2026-0000001","fechaRegistro":"2026-10-14T10:00:00-04:00","numeroOrden":null}""");
        var portal = new ServicioPortal(snd);
        var cedula = Ayudas.CedulaNueva();

        var r = await portal.EnviarSolicitudAsync(Ayudas.Sesion(cedula), new SolicitudPortal
        {
            Tipo = "Certificacion", Cedula = cedula, Nombres = "A", Apellidos = "B", ConsentimientoDatos = true
        }, "clave-123");

        Assert.Equal("TRA-2026-0000001", r.NumeroTramite);
        Assert.Equal("clave-123", falsa.Peticion!.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal($"ciudadano:{cedula}", falsa.Peticion.Headers.GetValues("X-Usuario-Final").Single());
        Assert.Contains($"\"cedulaCuenta\":\"{cedula}\"", falsa.CuerpoEnviado);
        Assert.Contains("\"canal\":\"Web\"", falsa.CuerpoEnviado);
    }

    [Fact]
    public async Task Un_422_de_Integracion_llega_como_regla_de_negocio_con_su_mensaje()
    {
        var (snd, _) = Ayudas.Snd(HttpStatusCode.UnprocessableEntity,
            """{"title":"Regla de negocio","status":422,"detail":"Ya tiene una solicitud activa (TRA-2026-0000009)."}""");
        var e = await Assert.ThrowsAsync<ReglaNegocioException>(() => new ServicioPortal(snd).EnviarSolicitudAsync(Ayudas.Sesion(),
            new SolicitudPortal { Tipo = "Certificacion", Cedula = "40123456788", Nombres = "A", Apellidos = "B", ConsentimientoDatos = true }, "k"));
        Assert.Contains("TRA-2026-0000009", e.Message);
    }

    [Fact]
    public async Task Un_503_se_traduce_en_servicio_no_disponible()
    {
        var (snd, _) = Ayudas.Snd(HttpStatusCode.ServiceUnavailable, """{"detail":"El sistema central no está disponible."}""");
        await Assert.ThrowsAsync<SndNoDisponibleException>(() => new ServicioPortal(snd).MisTramitesAsync(Ayudas.Sesion()));
    }

    [Fact]
    public async Task Un_tramite_inexistente_devuelve_null()
    {
        var (snd, _) = Ayudas.Snd(HttpStatusCode.NotFound, "");
        Assert.Null(await new ServicioPortal(snd).TramiteAsync(Ayudas.Sesion(), "TRA-2026-9999999"));
    }

    [Fact]
    public async Task No_se_puede_pagar_la_orden_de_otra_persona()
    {
        var (snd, _) = Ayudas.Snd(HttpStatusCode.OK,
            """{"numeroOrden":"OC-2026-0000001","tasaCodigo":"COPIA-CERT","concepto":"Copia","documento":"00100000001","nombrePagador":"Otra","referencia":null,"cantidad":1,"montoUnitario":300,"total":300,"estado":"Pendiente","fechaCreacion":"2026-10-14T10:00:00-04:00","fechaPago":null,"numeroComprobante":null}""");
        var e = await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            new ServicioPortal(snd).PagarAsync(Ayudas.Sesion(), "OC-2026-0000001", "tok_visa", "clave"));
        Assert.Contains("no corresponde", e.Message);
    }

    [Fact]
    public void Una_solicitud_sin_consentimiento_se_rechaza_antes_de_enviarla()
    {
        Assert.Throws<ReglaNegocioException>(() => ServicioPortal.ValidarSolicitud(
            new SolicitudPortal { Tipo = "Certificacion", Cedula = "40123456788", Nombres = "A", Apellidos = "B", ConsentimientoDatos = false }));
    }
}

public class SeguridadPortalPruebas
{
    [Fact]
    public void El_token_se_guarda_solo_como_hash()
    {
        var (token, hash) = TokenSeguro.Generar();
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, TokenSeguro.Hashear(token));
        Assert.NotEqual(token, hash);
    }

    [Theory]
    [InlineData("809-555-0101", true)]
    [InlineData("8295550101", true)]
    [InlineData("305-555-0101", false)]
    [InlineData("555-0101", false)]
    public void Valida_telefonos_dominicanos(string telefono, bool esperado)
        => Assert.Equal(esperado, ValidacionesPortal.TelefonoValido(telefono));

    [Theory]
    [InlineData("ciudadano@correo.do", true)]
    [InlineData("sin-arroba.do", false)]
    [InlineData("a@b", false)]
    public void Valida_correos(string correo, bool esperado) => Assert.Equal(esperado, ValidacionesPortal.CorreoValido(correo));

    [Fact]
    public async Task El_registro_exige_contrasena_de_12_caracteres_y_no_consulta_nada_antes()
    {
        var (snd, falsa) = Ayudas.Snd(HttpStatusCode.OK, "{}");
        var cuentas = new ServicioCuentas(new EjecutorSp("Server=no-existe;Database=X;Connect Timeout=1"), snd);
        var e = await Assert.ThrowsAsync<ReglaNegocioException>(() => cuentas.RegistrarAsync(
            new DatosRegistro(Ayudas.CedulaNueva(), "nuevo@correo.do", null, "Corta1A", "Corta1A", true), null));
        Assert.Contains("12", e.Message);
        Assert.Null(falsa.Peticion);
    }

    [Fact]
    public async Task Si_la_JCE_invalida_la_cedula_no_se_crea_la_cuenta()
    {
        var (snd, _) = Ayudas.Snd(HttpStatusCode.OK, """{"estado":"Invalida","nombres":null,"apellidos":null,"motivo":"La cédula está cancelada."}""");
        var cuentas = new ServicioCuentas(new EjecutorSp("Server=no-existe;Database=X;Connect Timeout=1"), snd);
        var e = await Assert.ThrowsAsync<ReglaNegocioException>(() => cuentas.RegistrarAsync(
            new DatosRegistro(Ayudas.CedulaNueva(), "nuevo@correo.do", null, "ContrasenaSegura2026", "ContrasenaSegura2026", true), null));
        Assert.Contains("cancelada", e.Message);
    }
}

/// <summary>Pruebas contra CONADIS_WebDB (ejecute antes los scripts 01 a 04 del portal).</summary>
public class CuentasBaseDatosPruebas
{
    private readonly EjecutorSp _db = new(Ayudas.Cadena);

    [Fact]
    public async Task La_cuenta_de_demostracion_inicia_sesion_con_cedula_o_correo()
    {
        var (snd, _) = Ayudas.Snd(HttpStatusCode.OK, "{}");
        var cuentas = new ServicioCuentas(_db, snd);

        var porCedula = await cuentas.IniciarSesionAsync("401-2345678-8", "Conadis2026!", "127.0.0.1");
        Assert.True(porCedula.Exito, porCedula.Mensaje);
        var porCorreo = await cuentas.IniciarSesionAsync("ciudadano.demo@correo.do", "Conadis2026!", "127.0.0.1");
        Assert.Equal(porCedula.Cuenta!.CuentaId, porCorreo.Cuenta!.CuentaId);
        Assert.False((await cuentas.IniciarSesionAsync("40123456788", "incorrecta", "127.0.0.1")).Exito);
    }

    [Fact]
    public async Task Registro_verificacion_preferencias_y_recuperacion()
    {
        var (snd, _) = Ayudas.Snd(HttpStatusCode.OK, """{"estado":"Validada","nombres":"María","apellidos":"Pérez Gómez","motivo":null}""");
        var cuentas = new ServicioCuentas(_db, snd);
        var cedula = Ayudas.CedulaNueva();
        var correo = $"prueba.{cedula}@correo.do";

        var registro = await cuentas.RegistrarAsync(new DatosRegistro(cedula, correo, "809-555-0101", "ContrasenaSegura2026",
            "ContrasenaSegura2026", true), "127.0.0.1");
        Assert.Equal("María Pérez Gómez", registro.NombreCompleto);   // el nombre viene de la JCE

        await cuentas.VerificarCorreoAsync(registro.TokenVerificacion);
        await cuentas.GuardarPreferenciasAsync(registro.CuentaId, true, 150);
        var perfil = await cuentas.ObtenerPerfilAsync(registro.CuentaId);
        Assert.True(perfil!.CorreoVerificado);
        Assert.Equal(150, perfil.TamanoTexto);

        var token = await cuentas.SolicitarRecuperacionAsync(correo);
        await cuentas.RestablecerContrasenaAsync(token!, "OtraContrasena2026", "OtraContrasena2026", null);
        Assert.True((await cuentas.IniciarSesionAsync(correo, "OtraContrasena2026", null)).Exito);
        // El enlace es de un solo uso
        await Assert.ThrowsAsync<ReglaNegocioException>(() => cuentas.RestablecerContrasenaAsync(token!, "Tercera2026Abc", "Tercera2026Abc", null));
        // Una cédula ya registrada no puede registrarse de nuevo
        await Assert.ThrowsAsync<ReglaNegocioException>(() => cuentas.RegistrarAsync(new DatosRegistro(cedula, "otro." + correo, null,
            "ContrasenaSegura2026", "ContrasenaSegura2026", true), null));
    }

    [Fact]
    public async Task El_borrador_conserva_su_clave_de_idempotencia()
    {
        var (snd, _) = Ayudas.Snd(HttpStatusCode.OK, "{}");
        var cuenta = (await new ServicioCuentas(_db, snd).IniciarSesionAsync("40123456788", "Conadis2026!", null)).Cuenta!;
        var borradores = new ServicioBorradores(_db);

        var b1 = await borradores.GuardarAsync(cuenta.CuentaId, TiposBorrador.Certificacion, 1, new { cedula = cuenta.Cedula });
        var b2 = await borradores.GuardarAsync(cuenta.CuentaId, TiposBorrador.Certificacion, 2, new { cedula = cuenta.Cedula, codigoCie10 = "G80" });
        Assert.Equal(b1.ClaveIdempotencia, b2.ClaveIdempotencia);
        Assert.Equal(2, (await borradores.ObtenerAsync(cuenta.CuentaId, TiposBorrador.Certificacion))!.Paso);

        await borradores.EliminarAsync(cuenta.CuentaId, TiposBorrador.Certificacion);
        Assert.Null(await borradores.ObtenerAsync(cuenta.CuentaId, TiposBorrador.Certificacion));
    }
}
