using System.Threading.RateLimiting;
using CONADIS.Integracion.Api.Infraestructura;
using CONADIS.Integracion.Api.Seguridad;
using CONADIS.Integracion.Negocio;
using CONADIS.Integracion.Negocio.Logging;
using CONADIS.Integracion.Negocio.Seguridad;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- 1. Log4Net (archivo + tabla Log4NetLog de Integración)
var cadena = builder.Configuration.GetConnectionString("IntegracionDB")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:IntegracionDB en appsettings.json.");
ConfiguradorLog4Net.Inicializar(Path.Combine(builder.Environment.ContentRootPath, "log4net.config"), cadena, "Integracion.Api");
builder.Logging.ClearProviders().AddProvider(new ProveedorLog4Net());

// ---------------------------------------------------------------- 2. Lógica de Integración (repositorios, simuladores, Core, casos de uso)
builder.Services.AgregarIntegracion(builder.Configuration, builder.Environment.ContentRootPath);

// ---------------------------------------------------------------- 3. Seguridad: API key por sistema + una política por ámbito
builder.Services.AddAuthentication(ManejadorClientes.Esquema)
    .AddScheme<AuthenticationSchemeOptions, ManejadorClientes>(ManejadorClientes.Esquema, _ => { });

builder.Services.AddAuthorization(o =>
{
    // Por defecto todo exige un sistema autenticado (salvo [AllowAnonymous]: /api/v1/salud)
    o.FallbackPolicy = new AuthorizationPolicyBuilder(ManejadorClientes.Esquema).RequireAuthenticatedUser().Build();
    foreach (var ambito in new[] { Politicas.Portal, Politicas.Caja, Politicas.Firma })
        o.AddPolicy(ambito, p => p.AddAuthenticationSchemes(ManejadorClientes.Esquema).RequireClaim(ManejadorClientes.ClaimAmbito, ambito));
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartition.GetFixedWindowLimiter(
        c.User.Identity?.Name ?? c.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("LimitePeticionesPorMinuto", 1200),
            Window = TimeSpan.FromMinutes(1)
        }));
});

// ---------------------------------------------------------------- 4. Errores, controladores y Swagger
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ManejadorErrores>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CONADIS Integración (SND)",
        Version = "v1",
        Description = "Puerta única entre el Portal Web, la Caja, las instituciones con convenio y el Core. " +
                      "Cada sistema se identifica con su API key (encabezado X-Api-Key)."
    });
    c.AddSecurityDefinition(ManejadorClientes.Esquema, new OpenApiSecurityScheme
    {
        Name = ApiKey.Encabezado, In = ParameterLocation.Header, Type = SecuritySchemeType.ApiKey, Description = "API key del sistema cliente."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = ManejadorClientes.Esquema } },
            Array.Empty<string>()
        }
    });
    var xml = Path.Combine(AppContext.BaseDirectory, "CONADIS.Integracion.Api.xml");
    if (File.Exists(xml)) c.IncludeXmlComments(xml);
});

// ================================================================ Tubería
var app = builder.Build();

app.UseMiddleware<Correlacion>();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();             // https://localhost:7102/swagger
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.Lifetime.ApplicationStopped.Register(ConfiguradorLog4Net.Cerrar);
app.Logger.LogInformation("CONADIS Integración (SND) iniciada ({Entorno}).", app.Environment.EnvironmentName);

app.Run();

/// <summary>Visible para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
