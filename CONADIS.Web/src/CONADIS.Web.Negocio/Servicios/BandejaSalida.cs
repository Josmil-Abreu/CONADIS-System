using System.Data;
using System.Text.Json;
using CONADIS.Web.Negocio.Datos;
using CONADIS.Web.Negocio.Externos;
using CONADIS.Web.Negocio.Modelos;
using log4net;

namespace CONADIS.Web.Negocio.Servicios;

/// <summary>Resultado de "enviar o dejar en cola": o llegó (Valor) o quedó guardado para enviarse después (EnvioId).</summary>
public sealed record ResultadoEnvio<T>(T? Valor, long? EnvioId)
{
    public bool EnCola => EnvioId is not null;
}

public sealed record EnvioPendiente(long EnvioId, string Tipo, string Descripcion, string Estado, int Intentos, string UltimoError,
    DateTimeOffset FechaCreacion, DateTimeOffset? FechaEnvio);

/// <summary>
/// Bandeja de salida del portal (tolerancia a caídas). Si Integración o el Core no responden, la operación del
/// ciudadano se guarda en web.Envio y el TrabajadorBandejaSalida la reenvía cada 30 s hasta que vuelvan.
/// No se pierde nada y no se duplica nada (cada envío conserva su clave de idempotencia).
/// </summary>
public sealed class BandejaSalida(EjecutorSp db, ClienteSnd snd)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(BandejaSalida));

    /// <summary>Intenta enviar ya; si el sistema central no está disponible, deja la petición en la bandeja.</summary>
    public async Task<ResultadoEnvio<T>> EnviarOEncolarAsync<T>(CuentaSesion c, PeticionPortal p, Func<Task<T>> enviar, string? ip,
        CancellationToken ct = default)
    {
        try
        {
            return new ResultadoEnvio<T>(await enviar(), null);
        }
        catch (SndNoDisponibleException ex)
        {
            Log.Warn($"Sin conexión con Integración ({ex.Message}). '{p.Descripcion}' queda en la bandeja de salida.");
            return new ResultadoEnvio<T>(default, await EncolarAsync(c.CuentaId, p, ip, ct));
        }
    }

    public async Task<long> EncolarAsync(int cuentaId, PeticionPortal p, string? ip, CancellationToken ct = default)
    {
        var f = (await db.ConsultarAsync("web.ppEnvio_Encolar",
        [
            Parametro.Entero("@CuentaId", cuentaId), Parametro.TextoAnsi("@Tipo", p.Tipo, 30), Parametro.TextoAnsi("@Metodo", p.Metodo, 6),
            Parametro.Texto("@Ruta", p.Ruta, 300), Parametro.Texto("@Carga", JsonSerializer.Serialize(p.Cuerpo, ClienteSnd.Json)),
            Parametro.TextoAnsi("@ClaveIdempotencia", p.Clave, 150), Parametro.Texto("@Descripcion", p.Descripcion, 200)
        ], ct)).Rows[0];
        var id = f.EnteroLargo("EnvioId");
        await Evento(cuentaId, "EnvioEnCola", $"{p.Descripcion} · envío N.º {id} (se enviará al volver la conexión)", ip, ct);
        return id;
    }

    public Task<List<EnvioPendiente>> PendientesAsync(int cuentaId, CancellationToken ct = default)
        => db.ConsultarListaAsync("web.ppEnvio_ListarPorCuenta", f => new EnvioPendiente(f.EnteroLargo("EnvioId"), f.Texto("Tipo"),
            f.Texto("Descripcion"), f.Texto("Estado"), f.Entero("Intentos"), f.Texto("UltimoError"), f.FechaHora("FechaCreacion"),
            f.FechaHoraONula("FechaEnvio")), [Parametro.Entero("@CuentaId", cuentaId)], ct);

    /// <summary>Reenvía lo pendiente. Devuelve cuántos se entregaron. Si Integración sigue caída, para y reintenta en la próxima vuelta.</summary>
    public async Task<int> ProcesarAsync(CancellationToken ct = default)
    {
        var lote = await db.ConsultarAsync("web.ppEnvio_Tomar", [Parametro.Entero("@Maximo", 20)], ct);
        var entregados = 0;
        var sinConexion = false;
        foreach (DataRow f in lote.Rows)
        {
            var id = f.EnteroLargo("EnvioId");
            var cuentaId = f.Entero("CuentaId");
            var descripcion = f.Texto("Descripcion");
            if (sinConexion)
            {
                await Fallo(id, "Sin conexión con Integración.", false, ct);
                continue;
            }

            try
            {
                var contexto = new ContextoPortal(f.Texto("Cedula"));
                var respuesta = await snd.ReenviarAsync(f.Texto("Metodo"), f.Texto("Ruta"), f.Texto("Carga"), contexto, f.Texto("ClaveIdempotencia"), ct);
                if (f.Texto("Tipo") == "ValidarIdentidad") await AplicarIdentidadAsync(cuentaId, respuesta, ct);
                await db.EjecutarAsync("web.ppEnvio_Completar", [Parametro.EnteroLargo("@EnvioId", id), Parametro.Texto("@Respuesta", respuesta)], ct);
                await Evento(cuentaId, "EnvioCompletado", $"{descripcion} · envío N.º {id} entregado. {Resumen(respuesta)}", null, ct);
                Log.Info($"Envío {id} entregado tras {f.Entero("Intentos")} intento(s): {descripcion}.");
                entregados++;
            }
            catch (SndNoDisponibleException ex)
            {
                sinConexion = true;   // no insistir con el resto en esta vuelta
                await Fallo(id, ex.Message, false, ct);
            }
            catch (ReglaNegocioException ex)
            {
                await Fallo(id, ex.Message, true, ct);
                await Evento(cuentaId, "EnvioRechazado", $"{descripcion} · envío N.º {id}: {ex.Message}", null, ct);
                Log.Warn($"Envío {id} rechazado: {ex.Message}");
            }
        }
        return entregados;
    }

    /// <summary>Respuesta de la JCE para una cuenta creada sin conexión: si es válida, actualiza el nombre.</summary>
    private async Task AplicarIdentidadAsync(int cuentaId, string respuesta, CancellationToken ct)
    {
        var i = JsonSerializer.Deserialize<IdentidadJce>(respuesta, ClienteSnd.Json);
        if (i?.Estado == "Validada")
            await db.EjecutarAsync("web.ppCuenta_ActualizarIdentidad",
                [Parametro.Entero("@CuentaId", cuentaId), Parametro.Texto("@NombreCompleto", $"{i.Nombres} {i.Apellidos}".Trim(), 200)], ct);
        else if (i?.Estado == "Invalida")
            throw new ReglaNegocioException(0, $"La JCE no valida la cédula de la cuenta: {i.Motivo}");
        else
            throw new SndNoDisponibleException("La JCE aún no responde.");
    }

    private Task Fallo(long id, string error, bool permanente, CancellationToken ct)
        => db.EjecutarAsync("web.ppEnvio_RegistrarFallo",
            [Parametro.EnteroLargo("@EnvioId", id), Parametro.Texto("@Error", error, 500), Parametro.Booleano("@Permanente", permanente)], ct);

    private Task Evento(int cuentaId, string evento, string detalle, string? ip, CancellationToken ct)
        => db.EjecutarAsync("web.ppEvento_Registrar",
        [
            Parametro.Entero("@CuentaId", cuentaId), Parametro.Texto("@Evento", evento, 50),
            Parametro.TextoAnsi("@Ip", ip, 45), Parametro.Texto("@Detalle", detalle, 300)
        ], ct);

    /// <summary>Número de trámite de la respuesta, si lo trae (para mostrarlo en "Mi actividad").</summary>
    private static string Resumen(string respuesta)
    {
        try
        {
            using var d = JsonDocument.Parse(respuesta);
            return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("numeroTramite", out var n)
                ? $"Trámite {n.GetString()}." : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
