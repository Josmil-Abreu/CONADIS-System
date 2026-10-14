using System.Security.Claims;
using System.Text.Json;

namespace CONADIS.Web.Extensions;

public static class SessionExtensions
{
    public static void SetObject<T>(this ISession session, string key, T value)
        => session.SetString(key, JsonSerializer.Serialize(value));

    public static T? GetObject<T>(this ISession session, string key)
    {
        var json = session.GetString(key);
        return json is null ? default : JsonSerializer.Deserialize<T>(json);
    }
}

public static class ClaimsExtensions
{
    public const string ClaimCedula = "cedula";
    public const string ClaimRnc = "rnc";
    public const string ClaimExpediente = "numeroExpediente";

    public static string? Cedula(this ClaimsPrincipal user) => user.FindFirstValue(ClaimCedula);
    public static string? Rnc(this ClaimsPrincipal user) => user.FindFirstValue(ClaimRnc);
    public static string? NumeroExpediente(this ClaimsPrincipal user) => user.FindFirstValue(ClaimExpediente);
}
