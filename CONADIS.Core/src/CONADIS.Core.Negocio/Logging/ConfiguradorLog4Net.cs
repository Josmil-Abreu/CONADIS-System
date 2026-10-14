using System.Reflection;
using log4net;
using log4net.Appender;
using log4net.Config;

namespace CONADIS.Core.Negocio.Logging;

/// <summary>
/// Arranca Log4Net leyendo el archivo log4net.config del programa que se ejecuta (API, aplicación interna...).
/// La cadena de conexión del appender de base de datos NO se escribe en log4net.config:
/// se inyecta aquí desde appsettings.json, para tenerla en un solo lugar.
/// </summary>
public static class ConfiguradorLog4Net
{
    /// <param name="rutaConfig">Ruta de log4net.config (normalmente junto al ejecutable).</param>
    /// <param name="cadenaConexionLogs">Base donde están dbo.Log4NetLog y dbo.ppInsertarLog.</param>
    /// <param name="aplicacion">Nombre que aparece en cada línea del log (ej.: "Core.Api").</param>
    public static void Inicializar(string rutaConfig, string? cadenaConexionLogs, string aplicacion)
    {
        var archivo = new FileInfo(rutaConfig);
        if (!archivo.Exists)
            throw new FileNotFoundException(
                $"No se encontró la configuración de Log4Net en '{archivo.FullName}'. " +
                "Verifica que log4net.config tenga 'Copiar en el directorio de salida = Copiar si es posterior'.");

        // Disponible en el patrón de log4net.config como %property{aplicacion}
        GlobalContext.Properties["aplicacion"] = aplicacion;
        // Valor por defecto cuando el log no viene de una petición HTTP (procesos, pruebas)
        GlobalContext.Properties["correlacion"] = "-";

        var repositorio = LogManager.GetRepository(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());
        XmlConfigurator.Configure(repositorio, archivo);

        if (!string.IsNullOrWhiteSpace(cadenaConexionLogs))
        {
            foreach (var appender in repositorio.GetAppenders().OfType<AdoNetAppender>())
            {
                appender.ConnectionString = cadenaConexionLogs;
                appender.ActivateOptions(); // aplica la nueva cadena
            }
        }

        LogManager.GetLogger(typeof(ConfiguradorLog4Net)).Info($"Log4Net inicializado ({aplicacion}).");
    }

    /// <summary>Vacía los buffers y cierra los appenders. Llamar al cerrar la aplicación.</summary>
    public static void Cerrar()
    {
        try
        {
            LogManager.GetLogger(typeof(ConfiguradorLog4Net)).Info("Aplicación cerrándose.");
            LogManager.Shutdown();
        }
        catch
        {
            // Al cerrar no hay a dónde reportar un fallo del propio log.
        }
    }
}