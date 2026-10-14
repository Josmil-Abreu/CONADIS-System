using CONADIS.Integracion.Negocio.Externos;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Resiliencia;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Toda llamada de Integración al Core pasa por aquí: con cortacircuito, tiempo de espera,
/// reintentos (solo si la operación es segura de repetir) y una fila en la bitácora.
/// Si el Core no está disponible lanza <see cref="ServicioNoDisponibleException"/> (HTTP 503 para el canal).
/// </summary>
public sealed class PuenteCore(ClienteCore core, EjecutorResiliente ejecutor)
{
    /// <param name="reintentar">
    /// true solo para operaciones idempotentes (GET, pagos con TransaccionId...). Un POST que crea
    /// algo nuevo (una solicitud) NO se reintenta automáticamente para no crearlo dos veces.
    /// </param>
    public async Task<RespuestaCore> EnviarAsync(HttpMethod metodo, string ruta, string? cuerpoJson, ContextoLlamada contexto,
        string operacion, bool? reintentar = null, CancellationToken ct = default)
    {
        var resultado = await ejecutor.EjecutarAsync(ServiciosExternos.Core,
            new OpcionesLlamada(operacion, Reintentar: reintentar ?? metodo == HttpMethod.Get), contexto,
            t => core.EnviarAsync(metodo, ruta, cuerpoJson, contexto, t),
            r => r.Estado.ToString(), ct: ct);

        return resultado.Exito
            ? resultado.Valor!
            : throw new ServicioNoDisponibleException(ServiciosExternos.Core,
                "El sistema central no está disponible en este momento. Intente de nuevo en unos minutos.");
    }

    public Task<RespuestaCore> GetAsync(string ruta, ContextoLlamada contexto, string operacion, CancellationToken ct = default)
        => EnviarAsync(HttpMethod.Get, ruta, null, contexto, operacion, true, ct);

    public Task<RespuestaCore> PostAsync(string ruta, string? cuerpoJson, ContextoLlamada contexto, string operacion,
        bool reintentar = false, CancellationToken ct = default)
        => EnviarAsync(HttpMethod.Post, ruta, cuerpoJson, contexto, operacion, reintentar, ct);

    public Task<RespuestaCore> PutAsync(string ruta, string? cuerpoJson, ContextoLlamada contexto, string operacion,
        CancellationToken ct = default)
        => EnviarAsync(HttpMethod.Put, ruta, cuerpoJson, contexto, operacion, false, ct);

    /// <summary>Codifica un segmento de ruta (cédulas, números de trámite) para que no altere la URL.</summary>
    public static string Segmento(string valor) => Uri.EscapeDataString(valor.Trim());
}
