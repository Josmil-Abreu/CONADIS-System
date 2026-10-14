using CONADIS.Core.Negocio.Seguridad;

namespace CONADIS.Core.Pruebas;

/// <summary>Pase de sesión de CONADIS.Inicio (login único): firma, módulo y vencimiento.</summary>
public class PaseInicioPruebas
{
    private const string Clave = "clave-de-traspaso-de-prueba-0123456789";

    [Fact]
    public void Un_pase_valido_devuelve_el_usuario()
    {
        var pase = PaseInicio.Emitir("Core", "medico", Clave);
        Assert.Equal("medico", PaseInicio.Validar(pase, "Core", Clave));
    }

    [Fact]
    public void Se_rechaza_si_es_de_otro_modulo_si_vencio_o_si_lo_alteran()
    {
        var pase = PaseInicio.Emitir("Core", "admin", Clave);
        Assert.Null(PaseInicio.Validar(pase, "Web", Clave));                                         // otro módulo
        Assert.Null(PaseInicio.Validar(pase, "Core", Clave, DateTimeOffset.UtcNow.AddMinutes(2)));   // vencido
        Assert.Null(PaseInicio.Validar(pase, "Core", Clave + "x"));                                  // otra clave

        var partes = pase.Split('.');
        var otro = PaseInicio.Emitir("Core", "auditor", Clave).Split('.')[0];
        Assert.Null(PaseInicio.Validar($"{otro}.{partes[1]}", "Core", Clave));                       // contenido cambiado
        Assert.Null(PaseInicio.Validar("basura", "Core", Clave));
        Assert.Null(PaseInicio.Validar("a.b", "Core", Clave));
        Assert.Null(PaseInicio.Validar(null, "Core", Clave));
    }

    [Fact]
    public void La_clave_debe_ser_larga()
        => Assert.Throws<InvalidOperationException>(() => PaseInicio.Emitir("Core", "admin", "corta"));
}
