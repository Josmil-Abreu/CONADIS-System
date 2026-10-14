using System.Threading.RateLimiting;

namespace CONADIS.Web.Infraestructura;

/// <summary>
/// Endurecimiento HTTP: cabeceras de seguridad (CSP, anti-clickjacking, nosniff, referrer) y límite de peticiones
/// por IP. Los POST de la cuenta (login, registro, consulta) tienen un límite más estricto contra fuerza bruta.
/// </summary>
public static class Seguridad
{
    public static IServiceCollection AgregarLimitePeticiones(this IServiceCollection servicios)
        => servicios.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
                var sensible = HttpMethods.IsPost(ctx.Request.Method)
                               && (ctx.Request.Path.StartsWithSegments("/Cuenta") || ctx.Request.Path.StartsWithSegments("/Consulta"));
                return sensible
                    ? RateLimitPartition.GetFixedWindowLimiter("auth:" + ip, _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) })
                    : RateLimitPartition.GetFixedWindowLimiter("gen:" + ip, _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) });
            });
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await ctx.HttpContext.Response.WriteAsync("Demasiados intentos. Espere un minuto e inténtelo de nuevo.", ct);
            };
        });

    public static IApplicationBuilder UsarCabecerasSeguridad(this IApplicationBuilder app)
        => app.Use(async (ctx, siguiente) =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            h["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
                                           "img-src 'self' data:; font-src 'self' data:; frame-ancestors 'none'; form-action 'self'; base-uri 'self'";
            await siguiente();
        });
}
