using CONADIS.Core.Negocio.Servicios;

namespace CONADIS.Core.Pruebas;

/// <summary>
/// Pruebas de integración: usan la base CONADIS_CoreDB real (scripts 01 al 10 ejecutados).
/// Dejan registros en la bitácora de auditoría (inicios de sesión), lo cual es correcto.
/// </summary>
public class AutenticacionPruebas
{
    private readonly ServicioAutenticacion _servicio = new(BaseDatosPrueba.Ejecutor());

    [Fact]
    public async Task Admin_con_la_contrasena_correcta_inicia_sesion()
    {
        var r = await _servicio.IniciarSesionAsync("admin", "Conadis2026!", "127.0.0.1");

        Assert.True(r.Exito);
        Assert.NotNull(r.Usuario);
        Assert.Equal("Administrador", r.Usuario!.Rol);
    }

    [Fact]
    public async Task El_evaluador_trae_su_disciplina_y_su_centro()
    {
        var r = await _servicio.IniciarSesionAsync("medico", "Conadis2026!", "127.0.0.1");

        Assert.True(r.Exito);
        Assert.Equal("Medica", r.Usuario!.Disciplina);
        Assert.NotNull(r.Usuario.CentroId);
    }

    [Fact]
    public async Task Usuario_inexistente_y_contrasena_incorrecta_dan_el_mismo_mensaje()
    {
        var inexistente = await _servicio.IniciarSesionAsync("no-existe", "Conadis2026!", "127.0.0.1");
        var incorrecta = await _servicio.IniciarSesionAsync("auditor", "Equivocada1", "127.0.0.1");

        Assert.False(inexistente.Exito);
        Assert.False(incorrecta.Exito);
        Assert.Equal(inexistente.Mensaje, incorrecta.Mensaje);   // no revela qué usuarios existen

        // Un acceso correcto reinicia el contador de intentos fallidos
        Assert.True((await _servicio.IniciarSesionAsync("auditor", "Conadis2026!", "127.0.0.1")).Exito);
    }
}