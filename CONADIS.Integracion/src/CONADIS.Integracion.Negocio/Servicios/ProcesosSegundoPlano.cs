using System.Text.Json;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Externos;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Repositorios;
using CONADIS.Integracion.Negocio.Resiliencia;
using log4net;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Procesa la cola de mensajes (outbox). Lo ejecuta el servicio de Windows cada pocos segundos.
/// Un mensaje que falla se reprograma con espera creciente; al agotar los intentos queda "Fallido"
/// y se alerta al administrador. Ningún mensaje se pierde.
/// </summary>
public sealed class ProcesadorCola(RepositorioMensajes mensajes, RepositorioPagos pagos, ClienteCore core, EjecutorResiliente ejecutor,
    IRepositorioAlertas alertas)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ProcesadorCola));

    /// <returns>Cantidad de mensajes tomados en este lote.</returns>
    public async Task<int> ProcesarLoteAsync(string trabajador, int maximo = 20, CancellationToken ct = default)
    {
        var lote = await mensajes.TomarAsync(trabajador, maximo, ct: ct);
        foreach (var m in lote)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                switch (m.Tipo)
                {
                    case TiposMensaje.CoreRegistrarPago:
                        await EntregarPagoAsync(m, ct);
                        break;
                    default:
                        await mensajes.RegistrarFalloAsync(m.MensajeId, $"Tipo de mensaje desconocido: {m.Tipo}.", permanente: true, ct);
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error($"Error procesando el mensaje {m.MensajeId} ({m.Tipo}).", ex);
                await mensajes.RegistrarFalloAsync(m.MensajeId, ex.Message, ct: ct);
            }
        }
        return lote.Count;
    }

    private async Task EntregarPagoAsync(MensajeCola m, CancellationToken ct)
    {
        var pago = JsonSerializer.Deserialize<PagoEntrante>(m.Carga, ClienteCore.Json)
                   ?? throw new InvalidOperationException("La carga del pago está vacía.");
        var contexto = new ContextoLlamada("ServicioWindows", pago.Cajero is null ? "portal" : $"cajero:{pago.Cajero}",
            "Entrega de pago al Core", m.CorrelacionId);

        // Sin reintentos inmediatos: la propia cola reintenta con espera creciente
        var llamada = await ejecutor.EjecutarAsync(ServiciosExternos.Core, new OpcionesLlamada("RegistrarPago", Reintentar: false), contexto,
            t => core.PostAsync("api/v1/pagos", m.Carga, contexto, t), r => r.Estado.ToString(), ct: ct);

        if (!llamada.Exito)
        {
            // Core caído = fallo transitorio: el pago no se da por perdido; se reintenta hasta que el Core vuelva.
            var fallo = await mensajes.RegistrarFalloAsync(m.MensajeId, $"Core no disponible: {llamada.Error}", ct: ct, transitorio: true);
            Log.Warn($"Pago {pago.TransaccionId}: Core no disponible; reintento a las {fallo.ProximoIntento:HH:mm:ss} ({fallo.Estado}).");
            return;
        }

        var r = llamada.Valor!;
        if (r.EsExito)
        {
            var estado = JsonDocument.Parse(r.Cuerpo).RootElement.GetProperty("estado").GetString() ?? EstadosPagoRecibido.Aplicado;
            await pagos.RegistrarRespuestaCoreAsync(pago.TransaccionId, estado, ct: ct);
            await mensajes.CompletarAsync(m.MensajeId, ct);
            Log.Info($"Pago {pago.TransaccionId} entregado al Core: {estado}.");
        }
        else
        {
            // 4xx: el Core rechazó el pago por una regla de negocio. Reintentar no lo arreglaría.
            var detalle = Detalle(r.Cuerpo);
            await pagos.RegistrarRespuestaCoreAsync(pago.TransaccionId, EstadosPagoRecibido.Rechazado, detalle, ct);
            await mensajes.CompletarAsync(m.MensajeId, ct);
            await alertas.RegistrarAsync(Severidades.Media, ServiciosExternos.Core,
                $"El Core rechazó el pago {pago.TransaccionId} (comprobante {pago.NumeroComprobante}): {detalle}", ct);
            Log.Warn($"Pago {pago.TransaccionId} rechazado por el Core ({r.Estado}): {detalle}");
        }
    }

    private static string Detalle(string cuerpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            return doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() ?? cuerpo : cuerpo;
        }
        catch (JsonException)
        {
            return cuerpo.Length > 300 ? cuerpo[..300] : cuerpo;
        }
    }
}

/// <summary>
/// Tareas periódicas que ejecuta el servicio de Windows:
///   - revalidar con la JCE las identidades pendientes (RF-INT-01, "reintento diferido");
///   - enviar las notificaciones que el Core dejó en cola (correo/SMS);
///   - marcar carnets vencidos (proceso diario);
///   - limpiar mensajes antiguos, caché vencida y claves de idempotencia;
///   - alertar si el certificado de firma está por vencer (RF-INT-03).
/// </summary>
public sealed class TareasProgramadas(PuenteCore core, ServicioIdentidad identidad, IEnviadorNotificaciones enviador,
    EjecutorResiliente ejecutor, RepositorioMensajes mensajes, RepositorioCache cache, RepositorioIdempotencia idempotencia,
    ServicioFirmaDigital firma)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(TareasProgramadas));

    /// <returns>Cuántas identidades quedaron resueltas (Validada o Inválida).</returns>
    public async Task<int> RevalidarIdentidadesAsync(CancellationToken ct = default)
    {
        var contexto = ContextoLlamada.Sistema("RevalidarIdentidades");
        var r = await core.GetAsync("api/v1/procesos/identidad-pendiente", contexto, "IdentidadPendiente", ct);
        var pendientes = ClienteCore.LeerOFallar<List<IdentidadPendienteCore>>(r, "identidades pendientes");

        var resueltas = 0;
        foreach (var p in pendientes)
        {
            var id = await identidad.ValidarAsync(p.Cedula, contexto, ct);
            if (id.Estado == EstadosIdentidad.Pendiente) continue;   // la JCE sigue sin responder: se intentará en la próxima vuelta

            var cuerpo = JsonSerializer.Serialize(new
            {
                cedula = p.Cedula,
                esValida = id.Estado == EstadosIdentidad.Validada,
                nombres = id.Nombres,
                apellidos = id.Apellidos,
                fechaNacimiento = id.FechaNacimiento,
                sexo = id.Sexo
            }, ClienteCore.Json);
            await core.PostAsync("api/v1/procesos/validacion-jce", cuerpo, contexto, "RegistrarValidacionJce", reintentar: true, ct);
            resueltas++;
        }

        if (resueltas > 0) Log.Info($"Identidades revalidadas con la JCE: {resueltas} de {pendientes.Count}.");
        return resueltas;
    }

    /// <returns>Cuántas notificaciones se enviaron.</returns>
    public async Task<int> EnviarNotificacionesAsync(int maximo = 50, CancellationToken ct = default)
    {
        var contexto = ContextoLlamada.Sistema("Notificaciones");
        var r = await core.GetAsync($"api/v1/procesos/notificaciones?maximo={maximo}", contexto, "NotificacionesPendientes", ct);
        var pendientes = ClienteCore.LeerOFallar<List<NotificacionCore>>(r, "notificaciones pendientes");

        var enviadas = 0;
        foreach (var n in pendientes)
        {
            var servicio = n.Canal == "SMS" ? ServiciosExternos.Sms : ServiciosExternos.Correo;
            var envio = await ejecutor.EjecutarAsync(servicio, new OpcionesLlamada("Enviar", Reintentar: false), contexto,
                async t => { await enviador.EnviarAsync(n, t); return true; }, ct: ct);

            await core.PostAsync($"api/v1/procesos/notificaciones/{n.NotificacionId}/resultado",
                JsonSerializer.Serialize(new { exito = envio.Exito }, ClienteCore.Json), contexto, "ResultadoNotificacion", reintentar: true, ct);
            if (envio.Exito) enviadas++;
        }
        return enviadas;
    }

    public async Task<int> MarcarVencimientosAsync(CancellationToken ct = default)
    {
        var r = await core.PostAsync("api/v1/procesos/vencimientos", null, ContextoLlamada.Sistema("Vencimientos"), "MarcarVencidos",
            reintentar: true, ct);
        return ClienteCore.LeerOFallar<JsonElement>(r, "vencimientos").GetProperty("carnetsVencidos").GetInt32();
    }

    /// <summary>Limpieza de mantenimiento. Devuelve (mensajes, caché, idempotencia) eliminados.</summary>
    public async Task<(int Mensajes, int Cache, int Idempotencia)> PurgarAsync(CancellationToken ct = default)
        => (await mensajes.PurgarAsync(30, ct), await cache.PurgarAsync(ct), await idempotencia.PurgarAsync(ct));

    public Task<int> RevisarCertificadoFirmaAsync(CancellationToken ct = default) => firma.RevisarVencimientoAsync(ct);
}

/// <summary>Inicio de sesión de los operadores de la consola de Integración (misma política que el Core).</summary>
public sealed class ServicioOperadores(EjecutorSp db)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioOperadores));
    private const string MensajeGenerico = "Usuario o contraseña incorrectos.";

    public async Task<ResultadoLoginOperador> IniciarSesionAsync(string? usuario, string? contrasena, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrEmpty(contrasena))
            return ResultadoLoginOperador.Fallido("Escribe tu usuario y tu contraseña.");

        var tabla = await db.ConsultarAsync("seg.ppOperador_ObtenerParaLogin", [Parametro.Texto("@Username", usuario.Trim(), 50)], ct);
        if (tabla.Rows.Count == 0)
        {
            Seguridad.HasherContrasena.SimularVerificacion(contrasena);
            return ResultadoLoginOperador.Fallido(MensajeGenerico);
        }

        var f = tabla.Rows[0];
        var id = f.Entero("OperadorId");
        if (!f.Booleano("Activo")) return ResultadoLoginOperador.Fallido("Tu cuenta está desactivada.");
        if (f.FechaHoraONula("BloqueadoHasta") is { } hasta && hasta > DateTimeOffset.Now)
            return ResultadoLoginOperador.Fallido($"Cuenta bloqueada hasta las {hasta.ToLocalTime():HH:mm}.");

        if (!Seguridad.HasherContrasena.Verificar(contrasena, f.Texto("PasswordHash")))
        {
            await db.ConsultarAsync("seg.ppOperador_RegistrarIntentoFallido", [Parametro.Entero("@OperadorId", id)], ct);
            Log.Warn($"Contraseña incorrecta para el operador '{usuario}'.");
            return ResultadoLoginOperador.Fallido(MensajeGenerico);
        }

        await db.EjecutarAsync("seg.ppOperador_RegistrarAccesoExitoso", [Parametro.Entero("@OperadorId", id)], ct);
        return ResultadoLoginOperador.Correcto(new OperadorSesion(id, f.Texto("Username"), f.Texto("NombreCompleto"), f.Texto("Rol")));
    }
}
