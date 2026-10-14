using System.Threading.RateLimiting;
using CONADIS.Core.Api.Infraestructura;
using CONADIS.Core.Api.Seguridad;
using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Logging;
using CONADIS.Core.Negocio.Seguridad;
using CONADIS.Core.Negocio.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- 1. Log4Net
// Todos los logs (los nuestros y los de ASP.NET Core) van al archivo y a la tabla dbo.Log4NetLog.
var cadenaCore = builder.Configuration.GetConnectionString("CoreDB")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:CoreDB en appsettings.json.");

ConfiguradorLog4Net.Inicializar(Path.Combine(builder.Environment.ContentRootPath, "log4net.config"), cadenaCore, "Core.Api");
builder.Logging.ClearProviders().AddProvider(new ProveedorLog4Net());

// ---------------------------------------------------------------- 2. Lógica de negocio
// EjecutorSp no guarda estado: una sola instancia para toda la aplicación.
// La cadena se lee al crearla (no antes), así las pruebas pueden cambiarla.
builder.Services.AddSingleton(sp => new EjecutorSp(sp.GetRequiredService<IConfiguration>().GetConnectionString("CoreDB")));
builder.Services.AddScoped<ServicioCatalogos>();
builder.Services.AddScoped<ServicioExpedientes>();
builder.Services.AddScoped<ServicioCitas>();
builder.Services.AddScoped<ServicioDictamen>();
builder.Services.AddScoped<ServicioCarnets>();
builder.Services.AddScoped<ServicioPagos>();
builder.Services.AddScoped<ServicioSolicitudesServicio>();
builder.Services.AddScoped<ServicioNotificaciones>();

// Firma digital: el Core se la pide al módulo de Integración por HTTP, identificándose con su propia API key.
builder.Services.AddHttpClient<IFirmaDigital, FirmaDigitalIntegracion>((sp, http) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    http.BaseAddress = new Uri(config["Integracion:UrlBase"] ?? "https://localhost:7102/");
    http.Timeout = TimeSpan.FromSeconds(config.GetValue("Integracion:TiempoEsperaSegundos", 10));
    var clave = config["Integracion:ApiKey"];
    if (!string.IsNullOrWhiteSpace(clave)) http.DefaultRequestHeaders.Add(ApiKey.Encabezado, clave);
});

// ---------------------------------------------------------------- 3. Seguridad
// Autenticación por API key. Política por defecto: TODO endpoint exige estar autenticado,
// salvo los marcados con [AllowAnonymous] (solo /api/v1/salud).
builder.Services.AddAuthentication(ManejadorApiKey.Esquema)
    .AddScheme<OpcionesApiKey, ManejadorApiKey>(ManejadorApiKey.Esquema,
        opciones => builder.Configuration.GetSection("ApiKeys").Bind(opciones));

builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder(ManejadorApiKey.Esquema).RequireAuthenticatedUser().Build());

// Límite de peticiones por cliente: protege el Core si un cliente entra en un bucle de reintentos.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            contexto.User.Identity?.Name ?? contexto.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("LimitePeticionesPorMinuto", 600),
                Window = TimeSpan.FromMinutes(1)
            }));
});

// ---------------------------------------------------------------- 4. Errores y controladores
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ManejadorErrores>();
builder.Services.AddControllers();

// ---------------------------------------------------------------- 5. Swagger (solo en desarrollo)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CONADIS Core API",
        Version = "v1",
        Description = "Servicios del módulo Core. Cliente autorizado: módulo de Integración (encabezado X-Api-Key)."
    });

    // Botón "Authorize" de Swagger para escribir la API key una sola vez
    c.AddSecurityDefinition(ManejadorApiKey.Esquema, new OpenApiSecurityScheme
    {
        Name = ApiKey.Encabezado,
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Description = "API key del sistema cliente."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = ManejadorApiKey.Esquema } },
            Array.Empty<string>()
        }
    });

    var xml = Path.Combine(AppContext.BaseDirectory, "CONADIS.Core.Api.xml");
    if (File.Exists(xml)) c.IncludeXmlComments(xml);
});

// ================================================================ Tubería de cada petición
var app = builder.Build();

app.UseMiddleware<Correlacion>();   // primero: el id de correlación queda en todos los logs
app.UseExceptionHandler();          // convierte excepciones en ProblemDetails (ManejadorErrores)

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();             // https://localhost:7101/swagger
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();               // después de autenticar: el límite es por cliente
app.UseAuthorization();
app.MapControllers();

app.Lifetime.ApplicationStopped.Register(ConfiguradorLog4Net.Cerrar);
app.Logger.LogInformation("CONADIS Core API iniciada ({Entorno}).", app.Environment.EnvironmentName);

app.Run();

/// <summary>Visible para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;