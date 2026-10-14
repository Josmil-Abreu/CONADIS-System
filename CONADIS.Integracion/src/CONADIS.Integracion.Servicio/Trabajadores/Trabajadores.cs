using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Servicios;

namespace CONADIS.Integracion.Servicio.Trabajadores;

/// <summary>Valores de la sección "Servicio" de appsettings.json.</summary>
public sealed class OpcionesServicio
{
    /// <summary>Nombre con el que este proceso bloquea mensajes (vacío = nombre del equipo + id del proceso).</summary>
    public string NombreTrabajador { get; set; } = string.Empty;
    public int SegundosCola { get; set; } = 5;
    public int MensajesPorLote { get; set; } = 20;
    public int SegundosNotificaciones { get; set; } = 30;
    public int MinutosRevalidarIdentidades { get; set; } = 5;
    /// <summary>Hora (0-23) del proceso diario: vencimientos, limpieza y certificado de firma.</summary>
    public int HoraProcesoDiario { get; set; } = 1;
}

/// <summary>
/// Vacía la cola de mensajes (outbox): entrega al Core los pagos recibidos de la Caja y del portal.
/// Si el Core está caído, cada mensaje se reprograma con espera creciente y nada se pierde.
/// </summary>
public sealed class TrabajadorCola(IServiceScopeFactory fabrica, OpcionesServicio opciones, ILogger<TrabajadorCola> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var nombre = string.IsNullOrWhiteSpace(opciones.NombreTrabajador)
            ? $"{Environment.MachineName}-{Environment.ProcessId}"
            : opciones.NombreTrabajador;
        log.LogInformation("Procesador de la cola iniciado como {Trabajador} (cada {Segundos} s).", nombre, opciones.SegundosCola);

        while (!ct.IsCancellationRequested)
        {
            var tomados = 0;
            try
            {
                using var alcance = fabrica.CreateScope();
                tomados = await alcance.ServiceProvider.GetRequiredService<ProcesadorCola>().ProcesarLoteAsync(nombre, opciones.MensajesPorLote, ct);
                if (tomados > 0) log.LogInformation("Cola: {Cantidad} mensaje(s) procesado(s).", tomados);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (BaseDatosNoDisponibleException)
            {
                log.LogWarning("La base de Integración no responde; se reintentará en {Segundos} s.", opciones.SegundosCola);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error inesperado procesando la cola.");
            }

            // Si el lote vino lleno probablemente hay más: se sigue sin esperar
            if (tomados < opciones.MensajesPorLote)
                await Esperar(TimeSpan.FromSeconds(opciones.SegundosCola), ct);
        }
    }

    internal static async Task Esperar(TimeSpan tiempo, CancellationToken ct)
    {
        try { await Task.Delay(tiempo, ct); }
        catch (OperationCanceledException) { }
    }
}

/// <summary>
/// Tareas periódicas (como el Timer del servicio de Windows visto en clase, pero con varios intervalos):
///   - cada 30 s: enviar notificaciones al ciudadano;
///   - cada 5 min: revalidar identidades pendientes con la JCE;
///   - una vez al día (a la hora configurada): carnets vencidos, limpieza y certificado de firma.
/// Un fallo en una tarea no detiene las demás.
/// </summary>
public sealed class TrabajadorProgramado(IServiceScopeFactory fabrica, OpcionesServicio opciones, ILogger<TrabajadorProgramado> log)
    : BackgroundService
{
    private sealed record Tarea(string Nombre, Func<TareasProgramadas, CancellationToken, Task<string?>> Ejecutar)
    {
        public DateTimeOffset Proxima { get; set; } = DateTimeOffset.Now;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var notificaciones = new Tarea("Notificaciones", async (t, c) => Resumen(await t.EnviarNotificacionesAsync(ct: c), "notificación(es) enviada(s)"));
        var identidades = new Tarea("Identidades", async (t, c) => Resumen(await t.RevalidarIdentidadesAsync(c), "identidad(es) revalidada(s)"));
        var diario = new Tarea("ProcesoDiario", ProcesoDiarioAsync) { Proxima = ProximaHoraDiaria(DateTimeOffset.Now) };

        log.LogInformation("Tareas programadas iniciadas. Proceso diario: {Proxima:yyyy-MM-dd HH:mm}.", diario.Proxima);

        while (!ct.IsCancellationRequested)
        {
            foreach (var tarea in new[] { notificaciones, identidades, diario })
            {
                if (ct.IsCancellationRequested || tarea.Proxima > DateTimeOffset.Now) continue;

                await EjecutarAsync(tarea, ct);
                tarea.Proxima = tarea == notificaciones ? DateTimeOffset.Now.AddSeconds(opciones.SegundosNotificaciones)
                    : tarea == identidades ? DateTimeOffset.Now.AddMinutes(opciones.MinutosRevalidarIdentidades)
                    : ProximaHoraDiaria(DateTimeOffset.Now.AddMinutes(1));
            }
            await TrabajadorCola.Esperar(TimeSpan.FromSeconds(5), ct);
        }
    }

    private async Task EjecutarAsync(Tarea tarea, CancellationToken ct)
    {
        try
        {
            using var alcance = fabrica.CreateScope();
            var resultado = await tarea.Ejecutar(alcance.ServiceProvider.GetRequiredService<TareasProgramadas>(), ct);
            if (resultado is not null) log.LogInformation("{Tarea}: {Resultado}.", tarea.Nombre, resultado);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is ServicioNoDisponibleException or BaseDatosNoDisponibleException)
        {
            log.LogWarning("{Tarea}: {Mensaje} Se intentará en la próxima vuelta.", tarea.Nombre, ex.Message);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "{Tarea}: error inesperado.", tarea.Nombre);
        }
    }

    private static async Task<string?> ProcesoDiarioAsync(TareasProgramadas t, CancellationToken ct)
    {
        var vencidos = await t.MarcarVencimientosAsync(ct);
        var (mensajes, cache, idempotencia) = await t.PurgarAsync(ct);
        var dias = await t.RevisarCertificadoFirmaAsync(ct);
        return $"{vencidos} carnet(s) vencido(s); limpieza: {mensajes} mensaje(s), {cache} en caché, {idempotencia} clave(s); " +
               $"el certificado de firma vence en {dias} día(s)";
    }

    private DateTimeOffset ProximaHoraDiaria(DateTimeOffset desde)
    {
        var hoy = new DateTimeOffset(desde.Date.AddHours(Math.Clamp(opciones.HoraProcesoDiario, 0, 23)), desde.Offset);
        return hoy > desde ? hoy : hoy.AddDays(1);
    }

    private static string? Resumen(int cantidad, string texto) => cantidad > 0 ? $"{cantidad} {texto}" : null;
}
