using CONADIS.Caja.Negocio.Modelos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CONADIS.Caja.Negocio.Servicios;

/// <summary>
/// Estado de la aplicación de caja mientras está abierta: quién inició sesión y qué sesión de caja tiene abierta.
/// Las pantallas lo reciben por inyección de dependencias (es único para toda la aplicación).
/// </summary>
public sealed class ContextoCaja
{
    public UsuarioCaja? Usuario { get; private set; }
    public SesionCaja? Sesion { get; private set; }

    public bool HayUsuario => Usuario is not null;
    public bool PuedeCobrar => Sesion?.Estado == EstadosSesion.Abierta;

    public UsuarioCaja UsuarioActual => Usuario ?? throw new InvalidOperationException("No hay un usuario con sesión iniciada.");
    public SesionCaja SesionActual => Sesion
        ?? throw new Datos.ReglaNegocioException(58002, "No hay una caja abierta. Abra la caja antes de cobrar.");

    /// <summary>Se dispara cuando cambia el usuario o la sesión (para refrescar la barra de estado).</summary>
    public event EventHandler? Cambio;

    public void IniciarSesion(UsuarioCaja usuario)
    {
        Usuario = usuario;
        Sesion = null;
        Cambio?.Invoke(this, EventArgs.Empty);
    }

    public void EstablecerSesion(SesionCaja? sesion)
    {
        Sesion = sesion;
        Cambio?.Invoke(this, EventArgs.Empty);
    }

    public void CerrarSesion()
    {
        Usuario = null;
        Sesion = null;
        Cambio?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Sincroniza con Integración cada cierto tiempo en segundo plano (tasas, cobros pendientes, e-CF en contingencia).
/// Si no hay conexión no pasa nada: la caja sigue cobrando y se reintenta en la próxima vuelta.
/// Uso en la pantalla principal:  sincronizacion.Resultado += ...;  sincronizacion.Iniciar();
/// </summary>
public sealed class SincronizacionPeriodica(IServiceScopeFactory alcances, ILogger<SincronizacionPeriodica> log) : IAsyncDisposable
{
    private CancellationTokenSource? _cancelacion;
    private Task? _bucle;
    private readonly SemaphoreSlim _enCurso = new(1, 1);

    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Último resultado (null si todavía no se ha sincronizado).</summary>
    public ResultadoSincronizacion? Ultimo { get; private set; }
    public DateTimeOffset? UltimaVez { get; private set; }

    /// <summary>Se dispara al terminar cada sincronización (en un hilo de fondo: usar Invoke en WinForms).</summary>
    public event EventHandler<ResultadoSincronizacion>? Resultado;

    public void Iniciar()
    {
        if (_bucle is not null) return;
        _cancelacion = new CancellationTokenSource();
        var ct = _cancelacion.Token;
        _bucle = Task.Run(async () =>
        {
            using var temporizador = new PeriodicTimer(Intervalo);
            do
            {
                await SincronizarAhoraAsync(ct);
            } while (await temporizador.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }, ct);
    }

    /// <summary>Sincroniza ya (botón "Sincronizar"). Si ya hay una sincronización en curso, no hace nada.</summary>
    public async Task<ResultadoSincronizacion?> SincronizarAhoraAsync(CancellationToken ct = default)
    {
        if (!await _enCurso.WaitAsync(0, ct)) return null;
        try
        {
            await using var alcance = alcances.CreateAsyncScope();
            var r = await alcance.ServiceProvider.GetRequiredService<SincronizadorCaja>().SincronizarAsync(ct);
            Ultimo = r;
            UltimaVez = DateTimeOffset.Now;
            Resultado?.Invoke(this, r);
            return r;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            // Base de datos local caída u otro error inesperado: se registra y se reintenta en la próxima vuelta.
            log.LogError(ex, "Error al sincronizar la caja");
            var r = new ResultadoSincronizacion(false, 0, 0, 0, 0, [$"Error al sincronizar: {ex.Message}"]);
            Ultimo = r;
            Resultado?.Invoke(this, r);
            return r;
        }
        finally
        {
            _enCurso.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancelacion is null) return;
        await _cancelacion.CancelAsync();
        try { if (_bucle is not null) await _bucle; }
        catch (OperationCanceledException) { }
        _cancelacion.Dispose();
    }
}
