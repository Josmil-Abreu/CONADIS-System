using System.Security.Claims;
using System.Text.Encodings.Web;
using CONADIS.Core.Negocio.Seguridad;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CONADIS.Core.Api.Seguridad;

/// <summary>Un sistema autorizado a llamar a la API (por ahora solo Integración).</summary>
public sealed class ClienteApi
{
    public string Nombre { get; set; } = string.Empty;

    /// <summary>SHA-256 de la clave (nunca la clave en claro). Se calcula con ApiKey.Hashear.</summary>
    public string HashClave { get; set; } = string.Empty;
}

public sealed class OpcionesApiKey : AuthenticationSchemeOptions
{
    public List<ClienteApi> Clientes { get; set; } = [];
}

/// <summary>
/// Autenticación sistema-a-sistema: el cliente envía su clave en el encabezado X-Api-Key.
/// Se compara su SHA-256 con los hashes configurados (en tiempo constante).
/// Si coincide, la petición queda autenticada con el nombre del cliente (ej.: "Integracion").
/// </summary>
public sealed class ManejadorApiKey(IOptionsMonitor<OpcionesApiKey> opciones, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<OpcionesApiKey>(opciones, logger, encoder)
{
    public const string Esquema = "ApiKey";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKey.Encabezado, out var valores) || string.IsNullOrWhiteSpace(valores))
            return Task.FromResult(AuthenticateResult.NoResult());

        var clave = valores.ToString();
        var cliente = Options.Clientes.FirstOrDefault(c => ApiKey.Coincide(clave, c.HashClave));
        if (cliente is null)
        {
            Logger.LogWarning("API key no válida desde {Ip}.", Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("API key no válida."));
        }

        var identidad = new ClaimsIdentity([new Claim(ClaimTypes.Name, cliente.Nombre)], Esquema);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identidad), Esquema);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>Respuesta 401 con un cuerpo JSON que explica qué falta.</summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "No autenticado",
            Detail = $"Envía una API key válida en el encabezado {ApiKey.Encabezado}."
        });
    }
}