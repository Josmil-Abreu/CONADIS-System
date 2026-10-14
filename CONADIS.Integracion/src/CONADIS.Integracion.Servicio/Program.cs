using CONADIS.Integracion.Negocio;
using CONADIS.Integracion.Negocio.Logging;
using CONADIS.Integracion.Servicio.Trabajadores;

// Servicio de Windows de Integración. Con F5 corre como consola; instalado (Scripts\instalar-servicio.bat)
// corre como servicio "CONADIS Integracion" que arranca solo con Windows y se reinicia si falla.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "CONADIS Integracion");

// Log4Net: archivo logs\integracion-servicio.log + tabla Log4NetLog de la base de Integración
ConfiguradorLog4Net.Inicializar(Path.Combine(builder.Environment.ContentRootPath, "log4net.config"),
    builder.Configuration.GetConnectionString("IntegracionDB"), "Integracion.Servicio");
builder.Logging.ClearProviders().AddProvider(new ProveedorLog4Net());

// Misma lógica y configuración que la API (repositorios, simuladores, cliente del Core...)
builder.Services.AgregarIntegracion(builder.Configuration, builder.Environment.ContentRootPath);

var opciones = new OpcionesServicio();
builder.Configuration.GetSection("Servicio").Bind(opciones);
builder.Services.AddSingleton(opciones);

builder.Services.AddHostedService<TrabajadorCola>();
builder.Services.AddHostedService<TrabajadorProgramado>();

var host = builder.Build();
host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.Register(ConfiguradorLog4Net.Cerrar);
host.Run();
