using System.Collections.Concurrent;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Modelos;

namespace CONADIS.Integracion.Negocio.Repositorios;

/// <summary>integ.Bitacora: una fila por intercambio con un servicio externo (solo inserción).</summary>
public sealed class RepositorioBitacora(EjecutorSp db) : IRepositorioBitacora
{
    public Task RegistrarAsync(EntradaBitacora e, CancellationToken ct = default)
        => db.EjecutarAsync("integ.ppBitacora_Registrar",
        [
            Parametro.TextoAnsi("@Servicio", e.Servicio, 20),
            Parametro.Texto("@Operacion", Cortar(e.Operacion, 60), 60),
            Parametro.TextoAnsi("@Direccion", e.Direccion, 10),
            Parametro.Booleano("@Exito", e.Exito),
            Parametro.TextoAnsi("@Resultado", Cortar(e.Resultado, 30), 30),
            Parametro.Texto("@Cliente", Cortar(e.Contexto.Cliente, 100), 100),
            Parametro.Texto("@Usuario", Cortar(e.Contexto.Usuario, 100), 100),
            Parametro.Texto("@Proposito", Cortar(e.Contexto.Proposito, 200), 200),
            Parametro.TextoAnsi("@ReferenciaHash", e.ReferenciaHash, 64),
            Parametro.TextoAnsi("@CodigoRespuesta", Cortar(e.CodigoRespuesta, 20), 20),
            Parametro.Entero("@LatenciaMs", e.LatenciaMs),
            Parametro.Booleano("@DesdeCache", e.DesdeCache),
            Parametro.TextoAnsi("@CorrelacionId", Cortar(e.Contexto.CorrelacionId, 64), 64),
            Parametro.Texto("@Detalle", Cortar(e.Detalle, 500), 500)
        ], ct);

    public Task<List<RegistroBitacora>> ListarAsync(DateTimeOffset desde, DateTimeOffset hasta, string? servicio = null,
        string? cliente = null, string? referenciaHash = null, bool soloFallos = false, int maximo = 500, CancellationToken ct = default)
        => db.ConsultarListaAsync("integ.ppBitacora_Listar", f => new RegistroBitacora(f.EnteroLargo("BitacoraId"),
                f.FechaHora("Fecha"), f.Texto("Servicio"), f.Texto("Operacion"), f.Texto("Direccion"), f.TextoONulo("Cliente"),
                f.TextoONulo("Usuario"), f.TextoONulo("Proposito"), f.Booleano("Exito"), f.Texto("Resultado"),
                f.TextoONulo("CodigoRespuesta"), f.EnteroONulo("LatenciaMs"), f.Booleano("DesdeCache"),
                f.TextoONulo("CorrelacionId"), f.TextoONulo("Detalle")),
            [
                Parametro.FechaHora("@Desde", desde), Parametro.FechaHora("@Hasta", hasta),
                Parametro.TextoAnsi("@Servicio", servicio, 20), Parametro.Texto("@Cliente", cliente, 100),
                Parametro.TextoAnsi("@ReferenciaHash", referenciaHash, 64), Parametro.Booleano("@SoloFallos", soloFallos),
                Parametro.Entero("@Maximo", Math.Clamp(maximo, 1, 5000))
            ], ct);

    public Task<List<EstadisticaServicio>> EstadisticasAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct = default)
        => db.ConsultarListaAsync("integ.ppBitacora_Estadisticas", f => new EstadisticaServicio(f.Texto("Servicio"),
                f.Entero("Total"), f.Entero("Exitosas"), f.Entero("Fallidas"), f.Entero("DesdeCache"), f.Decimal("PorcentajeExito"),
                f.DecimalONulo("LatenciaPromedioMs"), f.EnteroONulo("LatenciaMaximaMs"), f.DecimalONulo("LatenciaP95Ms")),
            [Parametro.FechaHora("@Desde", desde), Parametro.FechaHora("@Hasta", hasta)], ct);

    internal static string? Cortar(string? texto, int maximo)
        => texto is null ? null : texto.Length <= maximo ? texto : texto[..maximo];
}

/// <summary>integ.Circuito: estado del cortacircuito de cada servicio externo (compartido por la API y el servicio de Windows).</summary>
public sealed class RepositorioCircuito(EjecutorSp db) : IRepositorioCircuito
{
    public async Task<EstadoCircuito> PuedeLlamarAsync(string servicio, CancellationToken ct = default)
    {
        var f = (await db.ConsultarAsync("integ.ppCircuito_PuedeLlamar", [Parametro.TextoAnsi("@Servicio", servicio, 20)], ct)).Rows[0];
        return new EstadoCircuito(f.Booleano("Permitido"), f.Texto("Estado"), f.FechaHoraONula("AbiertoHasta"));
    }

    public Task RegistrarResultadoAsync(string servicio, bool exito, string? error = null, CancellationToken ct = default)
        => db.ConsultarAsync("integ.ppCircuito_RegistrarResultado",
            [
                Parametro.TextoAnsi("@Servicio", servicio, 20), Parametro.Booleano("@Exito", exito),
                Parametro.Texto("@Error", RepositorioBitacora.Cortar(error, 500), 500)
            ], ct);

    public Task<List<CircuitoDetalle>> ListarAsync(CancellationToken ct = default)
        => db.ConsultarListaAsync("integ.ppCircuito_Listar", f => new CircuitoDetalle(f.Texto("Servicio"), f.Texto("Estado"),
            f.Entero("FallosConsecutivos"), f.FechaHoraONula("AbiertoHasta"), f.FechaHora("UltimoCambio"), f.TextoONulo("UltimoError")), ct: ct);

    public Task ReiniciarAsync(string servicio, CancellationToken ct = default)
        => db.EjecutarAsync("integ.ppCircuito_Reiniciar", [Parametro.TextoAnsi("@Servicio", servicio, 20)], ct);
}

/// <summary>integ.CacheValidacion: resultados de la JCE y el MSP (30 días).</summary>
public sealed class RepositorioCache(EjecutorSp db) : IRepositorioCache
{
    public Task<EntradaCache?> ObtenerAsync(string servicio, string clave, CancellationToken ct = default)
        => db.ConsultarUnoAsync("integ.ppCache_Obtener", f => new EntradaCache(f.Texto("Resultado"), f.TextoONulo("Datos"),
                f.FechaHora("FechaConsulta"), f.FechaHora("VenceEn")),
            [Parametro.TextoAnsi("@Servicio", servicio, 10), Parametro.TextoAnsi("@Clave", clave, 64)], ct);

    public Task GuardarAsync(string servicio, string clave, string resultado, string? datos, int? dias = null, CancellationToken ct = default)
        => db.EjecutarAsync("integ.ppCache_Guardar",
        [
            Parametro.TextoAnsi("@Servicio", servicio, 10), Parametro.TextoAnsi("@Clave", clave, 64),
            Parametro.TextoAnsi("@Resultado", resultado, 30), Parametro.Texto("@Datos", datos),
            Parametro.Entero("@Dias", dias)
        ], ct);

    public Task InvalidarAsync(string servicio, string clave, CancellationToken ct = default)
        => db.EjecutarAsync("integ.ppCache_Invalidar",
            [Parametro.TextoAnsi("@Servicio", servicio, 10), Parametro.TextoAnsi("@Clave", clave, 64)], ct);

    public async Task<int> PurgarAsync(CancellationToken ct = default)
        => (await db.ConsultarAsync("integ.ppCache_Purgar", ct: ct)).Rows[0].Entero("Eliminados");
}

/// <summary>integ.Alerta: avisos al administrador.</summary>
public sealed class RepositorioAlertas(EjecutorSp db) : IRepositorioAlertas
{
    public Task RegistrarAsync(string severidad, string servicio, string mensaje, CancellationToken ct = default)
        => db.EjecutarAsync("integ.ppAlerta_Registrar",
        [
            Parametro.TextoAnsi("@Severidad", severidad, 10), Parametro.TextoAnsi("@Servicio", servicio, 20),
            Parametro.Texto("@Mensaje", RepositorioBitacora.Cortar(mensaje, 500), 500)
        ], ct);

    public Task<List<Alerta>> PendientesAsync(CancellationToken ct = default)
        => db.ConsultarListaAsync("integ.ppAlerta_ListarPendientes", f => new Alerta(f.EnteroLargo("AlertaId"), f.FechaHora("Fecha"),
            f.Texto("Severidad"), f.Texto("Servicio"), f.Texto("Mensaje")), ct: ct);

    public Task AtenderAsync(long alertaId, string usuario, string? comentario, CancellationToken ct = default)
        => db.EjecutarAsync("integ.ppAlerta_Atender",
        [
            Parametro.EnteroLargo("@AlertaId", alertaId), Parametro.Texto("@Usuario", usuario, 100),
            Parametro.Texto("@Comentario", RepositorioBitacora.Cortar(comentario, 500), 500)
        ], ct);
}

/// <summary>
/// cfg.Parametro con una caché en memoria de 60 segundos: se consultan en cada llamada externa
/// (tiempos de espera, reintentos) y no tiene sentido ir a la base cada vez.
/// </summary>
public sealed class RepositorioParametros(EjecutorSp db) : IParametros
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, string> _valores = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _cargado = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _candado = new(1, 1);

    public async Task<int> EnteroAsync(string clave, int porDefecto, CancellationToken ct = default)
        => int.TryParse(await TextoAsync(clave, ct), out var v) ? v : porDefecto;

    public async Task<string?> TextoAsync(string clave, CancellationToken ct = default)
    {
        if (DateTimeOffset.Now - _cargado > Vigencia)
        {
            try
            {
                await RecargarAsync(ct);
            }
            catch (Exception ex) when (ex is BaseDatosNoDisponibleException or ErrorBaseDatosException)
            {
                // Sin base de datos se siguen usando los últimos valores (o los valores por defecto)
            }
        }
        return _valores.TryGetValue(clave, out var v) ? v : null;
    }

    public Task<List<ParametroIntegracion>> ListarAsync(CancellationToken ct = default)
        => db.ConsultarListaAsync("cfg.ppParametro_Listar", f => new ParametroIntegracion(f.Texto("Clave"), f.Texto("Valor"),
            f.Texto("Descripcion"), f.FechaHora("FechaCambio"), f.Texto("UsuarioCambio")), ct: ct);

    public async Task ActualizarAsync(string clave, string valor, string usuario, CancellationToken ct = default)
    {
        await db.EjecutarAsync("cfg.ppParametro_Actualizar",
            [Parametro.TextoAnsi("@Clave", clave, 60), Parametro.Texto("@Valor", valor, 200), Parametro.Texto("@Usuario", usuario, 100)], ct);
        _cargado = DateTimeOffset.MinValue; // fuerza recarga
    }

    private async Task RecargarAsync(CancellationToken ct)
    {
        await _candado.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.Now - _cargado <= Vigencia) return;
            foreach (var p in await ListarAsync(ct)) _valores[p.Clave] = p.Valor;
            _cargado = DateTimeOffset.Now;
        }
        finally
        {
            _candado.Release();
        }
    }
}
