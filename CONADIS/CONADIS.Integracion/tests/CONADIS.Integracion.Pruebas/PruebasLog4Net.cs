using log4net;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Logging;
using CONADIS.Integracion.Negocio.Servicios;
using CONADIS.Integracion.Negocio.Simuladores;

namespace CONADIS.Integracion.Pruebas;

/// <summary>
/// Comprueba que Log4Net escribe en el archivo y en dbo.Log4NetLog.
/// Va fuera de una transacción porque el appender abre su propia conexión:
/// deja una línea de log real en la base (es lo esperado de un log).
/// </summary>
public class PruebasLog4Net
{
    [Fact]
    public async Task Log4Net_escribe_en_el_archivo_y_en_la_tabla_Log4NetLog()
    {
        var config = Path.Combine(AppContext.BaseDirectory, "log4net.config");
        ConfiguradorLog4Net.Inicializar(config, Entorno.CadenaConexion, "Integracion.Pruebas");

        var marca = $"Prueba de Log4Net {Guid.NewGuid():N}";
        using (Correlacion.Iniciar())
            LogManager.GetLogger(typeof(PruebasLog4Net)).Info(marca);

        var sp = new EjecutorSp(Entorno.CadenaConexion);
        var administracion = new ServicioAdministracion(sp, new ServicioCola(sp), new EstadoSimuladores());
        var logs = await administracion.ListarLogsAsync(DateTime.UtcNow.AddMinutes(-5), null, "INFO", 500);

        Assert.Contains(logs, l => l.Mensaje.Contains(marca));

        var archivos = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "logs"), "integracion-*.log");
        Assert.NotEmpty(archivos);
    }
}
