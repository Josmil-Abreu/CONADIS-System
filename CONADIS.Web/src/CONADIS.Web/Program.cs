using CONADIS.Web.Infraestructura;
using CONADIS.Web.Negocio;
using CONADIS.Web.Negocio.Logging;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- 1. Log4Net (archivo + tabla Log4NetLog de CONADIS_WebDB)
var cadena = builder.Configuration.GetConnectionString("WebDB");
ConfiguradorLog4Net.Inicializar(Path.Combine(builder.Environment.ContentRootPath, "log4net.config"), cadena, "Web.Portal");
builder.Logging.ClearProviders().AddProvider(new ProveedorLog4Net());

// ---------------------------------------------------------------- 2. Lógica del portal (CONADIS.Web.Negocio)
// Cuentas y borradores en CONADIS_WebDB; trámites, carnet y pagos a través de Integración (nunca directo al Core).
builder.Services.AgregarLogicaPortal(builder.Configuration);

// Bandeja de salida: reenvía cada 30 s lo que el ciudadano hizo mientras Integración o el Core estaban caídos
builder.Services.AddHostedService<TrabajadorBandejaSalida>();

// ---------------------------------------------------------------- 3. MVC + manejo de errores de negocio / Integración caída
builder.Services.AddControllersWithViews(o => o.Filters.Add<FiltroErroresPortal>());

// Sesión: borrador del asistente de solicitud mientras el ciudadano avanza por los pasos
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromMinutes(30);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
});

// ---------------------------------------------------------------- 4. Autenticación por cookie (datos de la cuenta en un claim)
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Cuenta/IniciarSesion";
        o.LogoutPath = "/Cuenta/CerrarSesion";
        o.AccessDeniedPath = "/Cuenta/AccesoDenegado";
        o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();
app.UseAuthentication();   // debe ir ANTES de UseAuthorization
app.UseAuthorization();

app.MapStaticAssets();

// Rutas de Áreas (Portal de Empresas)
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Certificaciones}/{action=Index}/{id?}");

// Ruta por defecto
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Lifetime.ApplicationStopped.Register(ConfiguradorLog4Net.Cerrar);
app.Run();
