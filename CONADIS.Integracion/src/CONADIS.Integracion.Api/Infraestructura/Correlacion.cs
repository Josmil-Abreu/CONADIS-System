using System.Text.RegularExpressions;
using log4net;

namespace CONADIS.Integracion.Api.Infraestructura;

/// <summary>
/// Identificador de correlación: el canal (Web, Caja) lo envía en X-Correlacion-Id (o se genera uno nuevo)
/// y aparece en cada línea de log de esta petición. Con él se sigue una operación de punta a punta
/// por los logs de la Web, Integración y el Core (RNF-SEG-06).
/// También agrega encabezados de seguridad básicos a todas las respuestas.
/// </summary>
public sealed partial class Correlacion(RequestDelegate siguiente)
{
    public const string Encabezado = "X-Correlacion-Id";

    // Solo letras, números y guiones: evita que alguien inyecte texto raro en los logs
    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex FormatoValido();

    /// <summary>Id de correlación de la petición actual.</summary>
    public static string Obtener(HttpContext contexto) => contexto.Items[Encabezado] as string ?? string.Empty;

    public async Task InvokeAsync(HttpContext contexto)
    {
        var id = contexto.Request.Headers[Encabezado].ToString();
        if (!FormatoValido().IsMatch(id)) id = Guid.NewGuid().ToString("N");

        contexto.Items[Encabezado] = id;

        // Se agregan justo antes de enviar la respuesta, para que sobrevivan aunque
        // el manejador de errores limpie la respuesta tras una excepción.
        contexto.Response.OnStarting(() =>
        {
            contexto.Response.Headers[Encabezado] = id;
            contexto.Response.Headers.XContentTypeOptions = "nosniff";
            contexto.Response.Headers.CacheControl = "no-store";   // las respuestas tienen datos personales
            return Task.CompletedTask;
        });

        // Disponible en log4net.config como %property{correlacion}
        LogicalThreadContext.Properties["correlacion"] = id;
        try
        {
            await siguiente(contexto);
        }
        finally
        {
            LogicalThreadContext.Properties.Remove("correlacion");
        }
    }
}