using CONADIS.Caja.App.Formularios;
using CONADIS.Caja.Negocio;
using CONADIS.Caja.Negocio.Logging;
using CONADIS.Caja.Negocio.Servicios;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CONADIS.Caja.App;

internal static class Program
{
    /// <summary>
    /// Arranque de la Caja:
    ///  1. Lee appsettings.json (base local CajaDB, URL y clave de Integración).
    ///  2. Inicia Log4Net (archivo logs/caja-app.log + tabla dbo.Log4NetLog de CONADIS_CajaDB).
    ///  3. Registra la lógica (AgregarLogicaCaja) y las pantallas en el contenedor de dependencias.
    ///  4. Muestra el inicio de sesión y, si es correcto, la pantalla principal.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        ConfiguradorLog4Net.Inicializar(Path.Combine(AppContext.BaseDirectory, "log4net.config"),
            config.GetConnectionString("CajaDB"), "Caja.App");

        var servicios = new ServiceCollection();
        servicios.AddSingleton<IConfiguration>(config);
        servicios.AddLogging(b => b.ClearProviders().AddProvider(new ProveedorLog4Net()));
        servicios.AgregarLogicaCaja(config);

        // Pantallas (una instancia nueva cada vez que se abren)
        servicios.AddTransient<FormLogin>();
        servicios.AddTransient<FormPrincipal>();
        servicios.AddTransient<FormAbrirCaja>();

        using var proveedor = servicios.BuildServiceProvider();
        var log = proveedor.GetRequiredService<ILogger<FormPrincipal>>();

        // Cualquier error no controlado: se registra y se muestra un mensaje en vez de cerrar la aplicación.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            log.LogError(e.Exception, "Error no controlado en la Caja");
            Ui.MostrarError(null, e.Exception);
        };

        var sincronizacion = proveedor.GetRequiredService<SincronizacionPeriodica>();
        sincronizacion.Intervalo = TimeSpan.FromSeconds(Math.Max(15, config.GetValue("Caja:SegundosEntreSincronizaciones", 60)));

        try
        {
            // Abierta desde CONADIS.Inicio (login único): "--pase <texto>" ya trae al usuario autenticado.
            var pase = PaseDeArgumentos(args);
            if (pase is not null)
            {
                var autenticacion = proveedor.GetRequiredService<ServicioAutenticacionCaja>();
                var usuario = Task.Run(() => autenticacion.IniciarConPaseAsync(pase, config["Inicio:ClaveTraspaso"] ?? "")).GetAwaiter().GetResult();
                if (usuario is not null) proveedor.GetRequiredService<ContextoCaja>().IniciarSesion(usuario);
            }

            // Login → principal. Al cerrar sesión en la principal se vuelve al login.
            while (true)
            {
                var contexto = proveedor.GetRequiredService<ContextoCaja>();
                if (!contexto.HayUsuario)
                {
                    using var login = proveedor.GetRequiredService<FormLogin>();
                    if (login.ShowDialog() != DialogResult.OK) break;
                }

                using var principal = proveedor.GetRequiredService<FormPrincipal>();
                Application.Run(principal);
                if (!principal.VolverAlLogin) break;
            }
        }
        finally
        {
            sincronizacion.DisposeAsync().AsTask().GetAwaiter().GetResult();
            ConfiguradorLog4Net.Cerrar();
        }
    }

    private static string? PaseDeArgumentos(string[] args)
    {
        var i = Array.IndexOf(args, "--pase");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
