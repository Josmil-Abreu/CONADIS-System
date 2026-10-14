using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Repositorios;
using CONADIS.Integracion.Negocio.Seguridad;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CONADIS.Integracion.Api.Seguridad;

/// <summary>
/// Autenticación sistema-a-sistema contra seg.ClienteApi: el cliente envía su clave en X-Api-Key,
/// se calcula su SHA-256 y se busca en la base. Cada cliente recibe un claim "ambito" por cada
/// ámbito autorizado (portal, caja, firma, discapacidad.verificar).
/// Para no consultar la base en cada petición, el resultado se recuerda 60 segundos.
/// </summary>
public sealed class ManejadorClientes(IOptionsMonitor<AuthenticationSchemeOptions> opciones, ILoggerFactory logger,
    UrlEncoder encoder, RepositorioClientes clientes) : AuthenticationHandler<AuthenticationSchemeOptions>(opciones, logger, encoder)
{
    public const string Esquema = "ApiKey";
    public const string ClaimAmbito = "ambito";
    private const string ClaveItem = "ClienteAutenticado";

    private static readonly ConcurrentDictionary<string, (ClienteAutenticado? Cliente, DateTimeOffset Hasta)> Cache = new();

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var clave = Request.Headers[ApiKey.Encabezado].ToString();
        if (string.IsNullOrWhiteSpace(clave)) return AuthenticateResult.NoResult();

        var hash = ApiKey.Hashear(clave);
        ClienteAutenticado? cliente;
        if (Cache.TryGetValue(hash, out var guardado) && guardado.Hasta > DateTimeOffset.Now)
        {
            cliente = guardado.Cliente;
        }
        else
        {
            cliente = await clientes.ObtenerPorHashAsync(hash, Context.RequestAborted);
            Cache[hash] = (cliente, DateTimeOffset.Now.AddSeconds(60));
        }

        if (cliente is null)
        {
            Logger.LogWarning("API key no válida desde {Ip}.", Context.Connection.RemoteIpAddress);
            return AuthenticateResult.Fail("API key no válida.");
        }

        Context.Items[ClaveItem] = cliente;
        var claims = new List<Claim> { new(ClaimTypes.Name, cliente.Nombre), new("tipo", cliente.Tipo) };
        claims.AddRange(cliente.Ambitos.Select(a => new Claim(ClaimAmbito, a)));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Esquema)), Esquema));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = 401, Title = "No autenticado", Detail = $"Envía una API key válida en el encabezado {ApiKey.Encabezado}."
        });
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = 403, Title = "Acceso denegado", Detail = "Este sistema no tiene el ámbito necesario para esta operación."
        });
    }

    /// <summary>El cliente autenticado de la petición actual.</summary>
    public static ClienteAutenticado Cliente(HttpContext contexto)
        => contexto.Items[ClaveItem] as ClienteAutenticado
           ?? throw new InvalidOperationException("La petición no está autenticada.");
}

/// <summary>Nombres de las políticas de autorización (una por ámbito).</summary>
public static class Politicas
{
    public const string Portal = Ambitos.Portal;
    public const string Caja = Ambitos.Caja;
    public const string Firma = Ambitos.Firma;
}
