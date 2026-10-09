using CONADIS.Application;
using CONADIS.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// MVC
builder.Services.AddControllersWithViews();

// Capas del proyecto (Application + Infrastructure)
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(
    builder.Configuration.GetConnectionString("ConadisDb")); // null => base en memoria

// Sesión: guarda el borrador del asistente de solicitud mientras no exista un servicio de borradores
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromMinutes(30);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
});

// Autenticación por cookies
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Cuenta/IniciarSesion";
        o.LogoutPath = "/Cuenta/CerrarSesion";
        o.AccessDeniedPath = "/Cuenta/AccesoDenegado";
        o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        o.SlidingExpiration = true;
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

app.Run();
