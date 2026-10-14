using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Logging;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;
using CONADIS.Core.Negocio.Servicios;
using CONADIS.Core.Web.Infraestructura;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- 1. Log4Net (archivo + tabla Log4NetLog de CONADIS_CoreDB)
var cadena = builder.Configuration.GetConnectionString("CoreDB")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:CoreDB en appsettings.json.");
ConfiguradorLog4Net.Inicializar(Path.Combine(builder.Environment.ContentRootPath, "log4net.config"), cadena, "Core.Web");
builder.Logging.ClearProviders().AddProvider(new ProveedorLog4Net());

// ---------------------------------------------------------------- 2. Lógica del Core (la misma que usa Core.Api)
builder.Services.AddSingleton(_ => new EjecutorSp(cadena));
builder.Services.AddScoped<ServicioAutenticacion>();
builder.Services.AddScoped<ServicioExpedientes>();
builder.Services.AddScoped<ServicioCitas>();
builder.Services.AddScoped<ServicioValoracion>();
builder.Services.AddScoped<ServicioDictamen>();
builder.Services.AddScoped<ServicioCarnets>();
builder.Services.AddScoped<ServicioReportes>();
builder.Services.AddScoped<ServicioAuditoria>();
builder.Services.AddScoped<ServicioUsuarios>();
builder.Services.AddScoped<ServicioCatalogos>();
builder.Services.AddScoped<ServicioSolicitudesServicio>();

// Firma digital del carnet: el Core se la pide a Integración (igual que en Core.Api).
builder.Services.AddHttpClient<IFirmaDigital, FirmaDigitalIntegracion>(http =>
{
    http.BaseAddress = new Uri(builder.Configuration["Integracion:UrlBase"] ?? "https://localhost:7102/");
    http.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Integracion:TiempoEsperaSegundos", 10));
    var clave = builder.Configuration["Integracion:ApiKey"];
    if (!string.IsNullOrWhiteSpace(clave)) http.DefaultRequestHeaders.Add(ApiKey.Encabezado, clave);
});

// Reportes de Integración (el Core los pide por la API con su clave, ámbito "reportes")
builder.Services.AddHttpClient<ClienteReportesIntegracion>(http =>
{
    http.BaseAddress = new Uri(builder.Configuration["Integracion:UrlBase"] ?? "https://localhost:7102/");
    http.Timeout = TimeSpan.FromSeconds(60);
    var clave = builder.Configuration["Integracion:ApiKey"];
    if (!string.IsNullOrWhiteSpace(clave)) http.DefaultRequestHeaders.Add(ApiKey.Encabezado, clave);
});

// ---------------------------------------------------------------- 3. Seguridad: cookie + un permiso por área (RBAC)
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Cuenta/IniciarSesion";
        o.LogoutPath = "/Cuenta/CerrarSesion";
        o.AccessDeniedPath = "/Cuenta/AccesoDenegado";
        o.ExpireTimeSpan = TimeSpan.FromMinutes(15);   // RNF-SEG: sesión inactiva se cierra a los 15 minutos
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.Name = "CONADIS.Core";
        // Una sesión de una ejecución anterior (cookie restaurada por el navegador) se rechaza y se borra:
        // el personal debe volver a entrar desde CONADIS.Inicio.
        o.Events.OnValidatePrincipal = async ctx =>
        {
            if (ctx.Principal?.FindFirst(SesionPersonal.ClaimInstancia)?.Value != SesionPersonal.Instancia)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorization(o =>
{
    foreach (var (area, roles) in Menu.Permisos) o.AddPolicy(area, p => p.RequireRole(roles));
});

builder.Services.AddControllersWithViews(o => o.Filters.Add<FiltroErroresCore>());

builder.Services.AgregarLimitePeticiones();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Inicio/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UsarCabecerasSeguridad();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllerRoute(name: "default", pattern: "{controller=Inicio}/{action=Index}/{id?}");

app.Lifetime.ApplicationStopped.Register(ConfiguradorLog4Net.Cerrar);
app.Run();
