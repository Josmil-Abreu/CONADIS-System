using CONADIS.Core.Negocio.Seguridad;

namespace CONADIS.Core.Pruebas;

/// <summary>Pruebas unitarias: no necesitan base de datos.</summary>
public class SeguridadPruebas
{
    // Hash del usuario "admin" tal como está en 09_DatosIniciales.sql (contraseña: Conadis2026!)
    private const string HashAdminSemilla =
        "PBKDF2-SHA256$600000$aerm/xsBmOkLbMBzWHp6OA==$Xq5StB1nXMAs/4Qq4xj9hx4y1xLYbzWOXgrTfQ2k5w8=";

    [Fact]
    public void Hashear_y_verificar_la_misma_contrasena_coincide()
    {
        var hash = HasherContrasena.Hashear("Clave#Segura1");
        Assert.True(HasherContrasena.Verificar("Clave#Segura1", hash));
    }

    [Fact]
    public void Dos_hashes_de_la_misma_contrasena_son_distintos_por_el_salt()
    {
        Assert.NotEqual(HasherContrasena.Hashear("Clave#Segura1"), HasherContrasena.Hashear("Clave#Segura1"));
    }

    [Fact]
    public void El_hash_de_los_datos_iniciales_verifica_con_Conadis2026()
    {
        Assert.True(HasherContrasena.Verificar("Conadis2026!", HashAdminSemilla));
        Assert.False(HasherContrasena.Verificar("conadis2026!", HashAdminSemilla));
    }

    [Theory]
    [InlineData("admin123")]                // contraseña antigua en texto plano
    [InlineData("PBKDF2-SHA256$x$y$z")]     // formato dañado
    [InlineData("")]
    public void Un_hash_invalido_nunca_verifica_ni_lanza_excepcion(string hashGuardado)
    {
        Assert.False(HasherContrasena.Verificar("admin123", hashGuardado));
    }

    [Theory]
    [InlineData("corta1A", false)]
    [InlineData("sinmayusculas1", false)]
    [InlineData("SINMINUSCULAS1", false)]
    [InlineData("SinNumeros", false)]
    [InlineData("Valida2026", true)]
    public void Valida_la_fortaleza_de_la_contrasena(string contrasena, bool esValida)
    {
        Assert.Equal(esValida, HasherContrasena.ValidarFortaleza(contrasena) is null);
    }

    [Fact]
    public void La_api_key_coincide_solo_con_su_hash()
    {
        var hash = ApiKey.Hashear("clave-de-integracion");
        Assert.True(ApiKey.Coincide("clave-de-integracion", hash));
        Assert.False(ApiKey.Coincide("otra-clave", hash));
        Assert.False(ApiKey.Coincide(null, hash));
    }
}