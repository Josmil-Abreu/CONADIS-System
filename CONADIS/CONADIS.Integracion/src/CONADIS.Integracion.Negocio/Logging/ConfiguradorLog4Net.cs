using System.Reflection;
using log4net;
using log4net.Appender;
using log4net.Config;

namespace CONADIS.Integracion.Negocio.Logging;

/// <summary>
/// Arranca Log4Net con el archivo log4net.config que viaja junto al ejecutable.
/// La cadena de conexión del appender de base de datos se inyecta aquí desde la
/// configuración del módulo, para no repetirla en dos archivos.
/// </summary>
public static class ConfiguradorLog4Net
{
    /// <param name="rutaConfig">Ruta de log4net.config (normalmente junto al ejecutable).</param>
    /// <param name="cadenaConexionLogs">CONADIS_IntegracionDB (donde están dbo.Log4NetLog y dbo.ppLog_Insertar). Null = solo archivo.</param>
    /// <param name="modulo">Nombre que aparece en cada línea del archivo (Integracion.Api, Integracion.Worker...).</param>
    public static void Inicializar(string rutaConfig, string? cadenaConexionLogs, string modulo)
    {
        var archivo = new FileInfo(rutaConfig);
        if (!archivo.Exists)
            throw new FileNotFoundException(
                $"No se encontró la configuración de Log4Net en '{archivo.FullName}'. " +
                "Verifica que log4net.config tenga 'Copiar en el directorio de salida'.");

        GlobalContext.Properties["modulo"] = modulo;
        GlobalContext.Properties["correlacion"] = Correlacion.Instancia;

        var ensamblado = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var repositorio = LogManager.GetRepository(ensamblado);
        XmlConfigurator.Configure(repositorio, archivo);

        foreach (var appender in repositorio.GetAppenders().OfType<AdoNetAppender>())
        {
            if (string.IsNullOrWhiteSpace(cadenaConexionLogs))
            {
                // Sin cadena de conexión el appender fallaría en cada mensaje: se apaga.
                appender.Threshold = log4net.Core.Level.Off;
            }
            else
            {
                appender.ConnectionString = cadenaConexionLogs;
            }
            appender.ActivateOptions();
        }

        LogManager.GetLogger(typeof(ConfiguradorLog4Net)).Info($"Log4Net inicializado para {modulo}.");
    }

    /// <summary>Vacía los buffers y cierra los appenders. Llamar al cerrar la aplicación.</summary>
    public static void Cerrar()
    {
        try
        {
            LogManager.GetLogger(typeof(ConfiguradorLog4Net)).Info("Aplicación cerrándose.");
            LogManager.Shutdown();
        }
        catch (Exception)
        {
            // Al cerrar no hay a dónde reportar un fallo del propio log.
        }
    }
}
