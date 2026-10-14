using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Externos;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Repositorios;
using CONADIS.Integracion.Negocio.Resiliencia;
using CONADIS.Integracion.Negocio.Seguridad;

namespace CONADIS.Integracion.Pruebas;

/// <summary>Conexión de las pruebas de integración (base CONADIS_IntegracionDB de LocalDB).</summary>
internal static class BaseDatosPrueba
{
    public static readonly string Cadena =
        Environment.GetEnvironmentVariable("CONADIS_INTEGRACION_PRUEBAS")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=CONADIS_IntegracionDB;Trusted_Connection=True;TrustServerCertificate=True";

    public static EjecutorSp Ejecutor() => new(Cadena);
}

// ---------------------------------------------------------------------------------------------
// "Dobles" en memoria: reemplazan la base de datos en las pruebas unitarias de la resiliencia.
// ---------------------------------------------------------------------------------------------

internal sealed class BitacoraEnMemoria : IRepositorioBitacora
{
    public List<EntradaBitacora> Entradas { get; } = [];
    public Task RegistrarAsync(EntradaBitacora entrada, CancellationToken ct = default) { Entradas.Add(entrada); return Task.CompletedTask; }
}

internal sealed class CircuitoEnMemoria(bool abierto = false) : IRepositorioCircuito
{
    public List<bool> Resultados { get; } = [];
    public Task<EstadoCircuito> PuedeLlamarAsync(string servicio, CancellationToken ct = default)
        => Task.FromResult(new EstadoCircuito(!abierto, abierto ? "Abierto" : "Cerrado", abierto ? DateTimeOffset.Now.AddSeconds(60) : null));
    public Task RegistrarResultadoAsync(string servicio, bool exito, string? error = null, CancellationToken ct = default)
    { Resultados.Add(exito); return Task.CompletedTask; }
}

internal sealed class CacheEnMemoria : IRepositorioCache
{
    public Dictionary<string, EntradaCache> Datos { get; } = [];
    public Task<EntradaCache?> ObtenerAsync(string servicio, string clave, CancellationToken ct = default)
        => Task.FromResult(Datos.GetValueOrDefault($"{servicio}:{clave}"));
    public Task GuardarAsync(string servicio, string clave, string resultado, string? datos, int? dias = null, CancellationToken ct = default)
    {
        Datos[$"{servicio}:{clave}"] = new EntradaCache(resultado, datos, DateTimeOffset.Now, DateTimeOffset.Now.AddDays(dias ?? 30));
        return Task.CompletedTask;
    }
}

internal sealed class ParametrosFijos : IParametros
{
    public Task<int> EnteroAsync(string clave, int porDefecto, CancellationToken ct = default)
        => Task.FromResult(clave.EndsWith("TiempoEsperaSegundos") ? 1 : porDefecto);   // 1 s para que las pruebas sean rápidas
    public Task<string?> TextoAsync(string clave, CancellationToken ct = default) => Task.FromResult<string?>(null);
}

internal sealed class AlertasEnMemoria : IRepositorioAlertas
{
    public List<string> Mensajes { get; } = [];
    public Task RegistrarAsync(string severidad, string servicio, string mensaje, CancellationToken ct = default)
    { Mensajes.Add($"{severidad}|{servicio}|{mensaje}"); return Task.CompletedTask; }
}

/// <summary>JCE falsa que cuenta las llamadas y puede "caerse".</summary>
internal sealed class JceContadora(bool disponible = true) : IServicioJce
{
    private readonly SimuladorJce _real = new(new OpcionesSimulacion { LatenciaMs = 0 });
    public int Llamadas { get; private set; }
    public Task<RespuestaJce> ConsultarCedulaAsync(string cedula, CancellationToken ct = default)
    {
        Llamadas++;
        return disponible ? _real.ConsultarCedulaAsync(cedula, ct) : throw new HttpRequestException("JCE caída (prueba).");
    }
}

internal static class Fabrica
{
    public const string ClaveHuella = "clave-de-huellas-solo-para-pruebas-0123456789";

    public static HuellaPrivada Huella() => new(ClaveHuella);

    public static (EjecutorResiliente Ejecutor, BitacoraEnMemoria Bitacora, CircuitoEnMemoria Circuito) Ejecutor(bool circuitoAbierto = false)
    {
        var bitacora = new BitacoraEnMemoria();
        var circuito = new CircuitoEnMemoria(circuitoAbierto);
        return (new EjecutorResiliente(circuito, bitacora, new ParametrosFijos()), bitacora, circuito);
    }

    public static ContextoLlamada Contexto() => new("Pruebas", "prueba", "Prueba automatizada", "prueba-correlacion");

    /// <summary>Cédula válida (dígito verificador correcto) distinta en cada ejecución. Empieza por 4.</summary>
    public static string CedulaNueva()
    {
        var diez = "4" + Random.Shared.NextInt64(0, 1_000_000_000).ToString("D9");
        return diez + ValidadorCedula.CalcularDigito(diez);
    }

    /// <summary>Cédula válida con un prefijo dado (999 = fallecido en el simulador, 888 = JCE lenta).</summary>
    public static string CedulaConPrefijo(string prefijo)
    {
        var diez = prefijo + Random.Shared.NextInt64(0, 10_000_000).ToString("D7");
        return diez + ValidadorCedula.CalcularDigito(diez);
    }
}
