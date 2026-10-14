using System.Globalization;
using CONADIS.Caja.Application;
using CONADIS.Caja.Infrastructure;
using CONADIS.Caja.Domain;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
var builder=WebApplication.CreateBuilder(args);
var demo=builder.Configuration.GetValue<bool>("Caja:Demo");
if(demo && !builder.Environment.IsDevelopment())throw new InvalidOperationException("La demostración solo funciona en Development.");
builder.Services.AddControllersWithViews(o=>o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(o=>o.HeaderName="X-CSRF-TOKEN");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICajaStore,EfCajaStore>();builder.Services.AddScoped<CajaService>();builder.Services.AddScoped<OutboxProcessor>();
builder.Services.AddDbContext<CajaDbContext>(o=>{if(demo)o.UseSqlite("Data Source=caja-demo.db");else o.UseSqlServer(builder.Configuration.GetConnectionString("Caja")??throw new InvalidOperationException("Configure ConnectionStrings__Caja mediante secretos."));});
var auth=builder.Services.AddAuthentication(o=>{o.DefaultScheme=CookieAuthenticationDefaults.AuthenticationScheme;o.DefaultChallengeScheme=demo?CookieAuthenticationDefaults.AuthenticationScheme:OpenIdConnectDefaults.AuthenticationScheme;});
auth.AddCookie(o=>{o.LoginPath="/Cuenta";o.AccessDeniedPath="/Caja/Denegado";o.ExpireTimeSpan=TimeSpan.FromMinutes(15);o.SlidingExpiration=true;o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Lax;o.Cookie.SecurePolicy=demo?CookieSecurePolicy.SameAsRequest:CookieSecurePolicy.Always;});
if(!demo)auth.AddOpenIdConnect(o=>{o.Authority=builder.Configuration["Oidc:Authority"];o.ClientId=builder.Configuration["Oidc:ClientId"];o.ClientSecret=builder.Configuration["Oidc:ClientSecret"];o.ResponseType="code";o.MapInboundClaims=false;o.TokenValidationParameters.NameClaimType="name";o.TokenValidationParameters.RoleClaimType="role";});
builder.Services.AddAuthorization();
if(demo){builder.Services.AddScoped<IFiscalGateway,DemoFiscal>();builder.Services.AddScoped<IVerificadorPago,DemoPagos>();builder.Services.AddScoped<IFirmaCierre,DemoFirma>();builder.Services.AddScoped<IEventosCore,DemoCore>();}
else {builder.Services.AddScoped<IFiscalGateway,PendienteIntegracion>();builder.Services.AddScoped<IVerificadorPago,PendienteIntegracion>();builder.Services.AddScoped<IFirmaCierre,PendienteIntegracion>();builder.Services.AddScoped<IEventosCore,PendienteIntegracion>();}
builder.Services.AddHttpClient("ssrs").ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{UseDefaultCredentials=true,AllowAutoRedirect=false});
builder.Services.AddHostedService<CONADIS.Caja.Worker>();
builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.GlobalLimiter=PartitionedRateLimiter.Create<Microsoft.AspNetCore.Http.HttpContext,string>(c=>RateLimitPartition.GetFixedWindowLimiter(c.User.Identity?.Name??c.Connection.RemoteIpAddress?.ToString()??"anon",_=>new(){PermitLimit=120,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));});
var app=builder.Build();
app.UseExceptionHandler("/Caja/Error");
if(!demo){app.UseHsts();app.UseHttpsRedirection();}
app.Use(async(c,next)=>{c.Response.Headers["Content-Security-Policy"]="default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self' data:; frame-ancestors 'none'; form-action 'self'; base-uri 'self'";c.Response.Headers["X-Content-Type-Options"]="nosniff";c.Response.Headers["Referrer-Policy"]="no-referrer";c.Response.Headers["Permissions-Policy"]="camera=(), microphone=(), geolocation=()";await next();});
app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("es-DO").AddSupportedCultures("es-DO").AddSupportedUICultures("es-DO"));
app.UseStaticFiles();app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();
if(demo){using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<CajaDbContext>();await db.Database.EnsureCreatedAsync();if(!db.Set<Tasa>().Any()){var today=DateOnly.FromDateTime(DateTime.UtcNow);db.Add(new Tasa("DUPLICADO","Duplicado de carnet · tarifa de demostración",500,0,today.AddYears(-1),today.AddYears(1),"4101","DEMO-001"));db.Add(new Tasa("ACCESIBILIDAD","Certificación de accesibilidad · tarifa de demostración",1500,0,today.AddYears(-1),today.AddYears(1),"4102","DEMO-002"));foreach(var type in Enum.GetValues<TipoECF>())db.Add(new SecuenciaENcf(type,1,10000,today.AddYears(1)));await db.SaveChangesAsync();}}
app.MapControllerRoute("default","{controller=Caja}/{action=Index}/{id?}");app.Run();
public partial class Program { }
