using System.Collections.Concurrent;
using log4net;
using CONADIS.Integracion.Negocio.Contratos;

namespace CONADIS.Integracion.Negocio.Simuladores;

/// <summary>
/// Interruptor de "servicio caído" para las demostraciones: permite apagar en vivo la JCE,
/// el Core, la DGII... y ver cómo actúan el cortacircuito, la caché y la cola.
/// Es un singleton: lo comparten todos los simuladores del proceso.
/// </summary>
public sealed class EstadoSimuladores
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(EstadoSimuladores));
    private readonly ConcurrentDictionary<string, byte> _caidos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Latencia simulada de cada llamada (ms). 0 en las pruebas.</summary>
    public int LatenciaMinimaMs { get; set; } = 30;
    public int LatenciaMaximaMs { get; set; } = 120;

    public IReadOnlyCollection<string> Caidos => _caidos.Keys.OrderBy(k => k).ToArray();

    public bool EstaCaido(string servicio) => _caidos.ContainsKey(servicio);

    public void Caer(string servicio)
    {
        ValidarCodigo(servicio);
        if (_caidos.TryAdd(servicio.ToUpperInvariant(), 0))
            Log.Warn($"Simulador {servicio.ToUpperInvariant()}: marcado como CAÍDO.");
    }

    public void Restaurar(string servicio)
    {
        ValidarCodigo(servicio);
        if (_caidos.TryRemove(servicio.ToUpperInvariant(), out _))
            Log.Info($"Simulador {servicio.ToUpperInvariant()}: restaurado.");
    }

    public void RestaurarTodos() => _caidos.Clear();

    /// <summary>Lo llama cada simulador al empezar: espera la latencia y falla si está caído.</summary>
    internal async Task AntesDeLlamarAsync(string servicio, CancellationToken ct)
    {
        if (LatenciaMaximaMs > 0)
            await Task.Delay(Random.Shared.Next(LatenciaMinimaMs, Math.Max(LatenciaMinimaMs, LatenciaMaximaMs) + 1), ct);

        if (EstaCaido(servicio))
            throw new ServicioExternoNoDisponibleException(servicio, $"El servicio {servicio} (simulado) no está disponible.");
    }

    private static void ValidarCodigo(string servicio)
    {
        if (!CodigosServicio.Todos.Contains(servicio, StringComparer.OrdinalIgnoreCase))
            throw new SolicitudInvalidaException($"Servicio desconocido: '{servicio}'. Válidos: {string.Join(", ", CodigosServicio.Todos)}.");
    }
}
