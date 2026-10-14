using CONADIS.Core.Negocio.Seguridad;

namespace CONADIS.Core.Pruebas;

/// <summary>Pruebas unitarias del dígito verificador y del enmascarado de nombres.</summary>
public class ValidadorCedulaPruebas
{
    [Fact]
    public void Una_cedula_generada_con_su_digito_es_valida_y_con_otro_digito_no()
    {
        var cedula = DatosPrueba.CedulaNueva();
        Assert.True(ValidadorCedula.EsValida(cedula));

        var digitoMalo = (cedula[10] - '0' + 1) % 10;
        Assert.False(ValidadorCedula.EsValida(cedula[..10] + digitoMalo));
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("00000000000")]
    [InlineData("abcdefghijk")]
    public void Formatos_invalidos_no_pasan(string cedula)
    {
        Assert.False(ValidadorCedula.EsValida(cedula));
    }

    [Fact]
    public void Acepta_la_cedula_con_guiones()
    {
        var c = DatosPrueba.CedulaNueva();
        Assert.True(ValidadorCedula.EsValida(ValidadorCedula.Formatear(c)));
        Assert.Equal(c, ValidadorCedula.Normalizar(ValidadorCedula.Formatear(c)));
    }

    [Fact]
    public void Enmascara_el_nombre_para_la_verificacion_publica()
    {
        Assert.Equal("J*** P*** G***", ValidadorCedula.EnmascararNombre("Juan Pérez Gómez"));
    }
}