using System.Text.Json;
using log4net;
using CONADIS.Integracion.Negocio.Datos;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Última respuesta conocida de cada consulta (snd.Cache). Cuando un servicio está caído,
/// Integración responde con ella (aunque esté vencida) en vez de fallar.
/// </summary>
public sealed class ServicioCache(EjecutorSp sp)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioCache));
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record Entrada<T>(T Valor, bool Vencido, DateTime Actualizado);

    public async Task<Entrada<T>?> ObtenerAsync<T>(string clave, bool permitirVencido, CancellationToken ct = default)
    {
        var fila = await sp.ConsultarUnoAsync("snd.ppCache_Obtener", f => f, ct,
            Parametro.Texto("@Clave", clave, 200),
            Parametro.Booleano("@PermitirVencido", permitirVencido));
        if (fila is null) return null;

        try
        {
            var valor = JsonSerializer.Deserialize<T>(fila.Texto("ValorJson"), Json);
            return valor is null ? null : new Entrada<T>(valor, fila.Booleano("Vencido"), fila.Fecha("FechaActualizacion"));
        }
        catch (JsonException ex)
        {
            // Una entrada que ya no corresponde al formato actual se ignora como si no existiera.
            Log.Warn($"Entrada de caché ilegible: {clave}.", ex);
            return null;
        }
    }

    /// <summary>Guarda el valor. Si falla solo se registra en el log: la caché es un extra, nunca bloquea.</summary>
    public async Task GuardarAsync<T>(string clave, string servicio, T valor, int? segundosVigencia = null, CancellationToken ct = default)
    {
        try
        {
            await sp.EjecutarAsync("snd.ppCache_Guardar", ct,
                Parametro.Texto("@Clave", clave, 200),
                Parametro.Texto("@ServicioCodigo", servicio, 20),
                Parametro.Texto("@ValorJson", JsonSerializer.Serialize(valor, Json)),
                Parametro.Entero("@SegundosVigencia", segundosVigencia));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"No se pudo guardar en caché: {clave}.", ex);
        }
    }

    public Task InvalidarAsync(string clave, CancellationToken ct = default)
        => sp.EjecutarAsync("snd.ppCache_Invalidar", ct, Parametro.Texto("@Clave", clave, 200));
}
