using CONADIS.Integracion.Negocio;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Seguridad;
using CONADIS.Integracion.Negocio.Servicios;
using CONADIS.Integracion.Negocio.Simuladores;

// Las pruebas de integración comparten la misma base: se ejecutan una detrás de otra.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CONADIS.Integracion.Pruebas;

/// <summary>Pruebas que no necesitan base de datos.</summary>
public class PruebasUnitarias
{
    [Fact]
    public void El_hash_de_la_api_key_es_SHA256_hexadecimal_en_mayuscula()
    {
        // Mismo resultado que CONVERT(CHAR(64), HASHBYTES('SHA2_256', 'abc'), 2) en SQL Server
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", ApiKey.Hashear("abc"));
    }

    [Fact]
    public void Generar_api_key_produce_claves_distintas_y_largas()
    {
        var a = ApiKey.Generar();
        var b = ApiKey.Generar();
        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 40);
    }

    [Fact]
    public void Un_parametro_sin_arroba_se_rechaza()
    {
        Assert.Throws<ArgumentException>(() => Parametro.Texto("Nombre", "x"));
    }

    [Theory]
    [InlineData("snd.ppPago_Registrar; DROP TABLE snd.Mensaje")]
    [InlineData("SELECT * FROM snd.Mensaje")]
    [InlineData("snd.pp--")]
    public async Task EjecutorSp_no_acepta_texto_SQL(string textoMalicioso)
    {
        var sp = new EjecutorSp("Server=.;Database=x;Integrated Security=true");
        await Assert.ThrowsAsync<ArgumentException>(() => sp.EjecutarAsync(textoMalicioso));
    }

    [Theory]
    [InlineData("0011234567")]
    [InlineData("4021234567")]
    public void La_cedula_con_su_digito_verificador_es_valida(string diezDigitos)
    {
        Assert.True(Digitos.CedulaValida(Entorno.CedulaValida(diezDigitos)));
    }

    [Fact]
    public void La_cedula_con_digito_verificador_incorrecto_no_es_valida()
    {
        var buena = Entorno.CedulaValida();
        var mala = buena[..10] + (char)('0' + (buena[10] - '0' + 1) % 10);
        Assert.False(Digitos.CedulaValida(mala));
        Assert.False(Digitos.CedulaValida("123"));
    }

    [Fact]
    public void Rnc_y_tarjetas_de_prueba()
    {
        Assert.True(Digitos.RncValido("101010101"));
        Assert.False(Digitos.RncValido("101010102"));
        Assert.True(Digitos.TarjetaValida("4111 1111 1111 1111"));
        Assert.True(Digitos.TarjetaValida("4000-0000-0000-0002"));
        Assert.False(Digitos.TarjetaValida("4111 1111 1111 1112"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("123")]
    [InlineData("001-1234567-AB")]
    public void Validar_cedula_mal_formada_lanza_SolicitudInvalida(string? cedula)
    {
        Assert.Throws<SolicitudInvalidaException>(() => Validaciones.Cedula(cedula));
    }

    [Fact]
    public void Validar_cedula_acepta_guiones()
    {
        Assert.Equal("00112345678", Validaciones.Cedula("001-1234567-8"));
    }

    [Fact]
    public void Un_rango_de_fechas_al_reves_se_rechaza()
    {
        var hoy = DateTime.UtcNow;
        Assert.Throws<SolicitudInvalidaException>(() => Validaciones.Rango(hoy, hoy.AddDays(-1)));
    }

    [Fact]
    public void Simular_caida_de_un_servicio_desconocido_se_rechaza()
    {
        Assert.Throws<SolicitudInvalidaException>(() => new EstadoSimuladores().Caer("NOEXISTE"));
    }
}
