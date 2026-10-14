using CONADIS.Core.Negocio.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CONADIS.Inicio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        // Solo archivo (logs/inicio.log): cada módulo registra los accesos en su propia base.
        ConfiguradorLog4Net.Inicializar(Path.Combine(AppContext.BaseDirectory, "log4net.config"), null, "Inicio");
        using var logs = LoggerFactory.Create(b => b.AddProvider(new ProveedorLog4Net()));

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            logs.CreateLogger("Inicio").LogError(e.Exception, "Error no controlado en CONADIS.Inicio");
            MessageBox.Show(e.Exception.Message, "CONADIS - Inicio", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        try
        {
            Application.Run(new FormInicio(new LoginUnico(config, logs)));
        }
        finally
        {
            ConfiguradorLog4Net.Cerrar();
        }
    }
}
