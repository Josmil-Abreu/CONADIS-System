using System.Data;
using log4net;
using CONADIS.Integracion.Negocio.Contratos;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Seguridad;
using CONADIS.Integracion.Negocio.Simuladores;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Operación de Integración: servicios de destino, parámetros, clientes, logs,
/// mensajes muertos y el interruptor de los simuladores para las demostraciones.
/// </summary>
public sealed class ServicioAdministracion(EjecutorSp sp, ServicioCola cola, EstadoSimuladores simuladores)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioAdministracion));

    /* ------------------------------ Servicios ------------------------------ */

    public Task<List<ServicioDto>> ListarServiciosAsync(CancellationToken ct = default)
        => sp.ConsultarListaAsync("snd.ppServicio_Listar", MapearServicio, ct);

    public async Task<ServicioDto> ObtenerServicioAsync(string codigo, CancellationToken ct = default)
        => await sp.ConsultarUnoAsync("snd.ppServicio_Obtener", MapearServicio, ct, Parametro.Texto("@Codigo", codigo, 20))
           ?? throw new RecursoNoEncontradoException($"No existe el servicio '{codigo}'.");

    public async Task ConfigurarServicioAsync(string codigo, string? urlBase, bool esSimulado, int umbralFallos,
        int segundosApertura, bool activo, CancellationToken ct = default)
    {
        if (umbralFallos is < 1 or > 100) throw new SolicitudInvalidaException("El umbral de fallos debe estar entre 1 y 100.");
        if (segundosApertura is < 5 or > 3600) throw new SolicitudInvalidaException("El tiempo de apertura debe estar entre 5 y 3600 segundos.");
        if (urlBase is not null && !Uri.TryCreate(urlBase, UriKind.Absolute, out _))
            throw new SolicitudInvalidaException("La dirección del servicio no es una URL válida.");

        await sp.EjecutarAsync("snd.ppServicio_Configurar", ct,
            Parametro.Texto("@Codigo", codigo, 20),
            Parametro.Texto("@UrlBase", urlBase, 300),
            Parametro.Booleano("@EsSimulado", esSimulado),
            Parametro.Entero("@UmbralFallos", umbralFallos),
            Parametro.Entero("@SegundosApertura", segundosApertura),
            Parametro.Booleano("@Activo", activo));
        Log.Info($"Servicio {codigo} reconfigurado (simulado={esSimulado}, activo={activo}).");
    }

    /* ------------------------------ Parámetros ----------------------------- */

    public Task<List<ParametroDto>> ListarParametrosAsync(CancellationToken ct = default)
        => sp.ConsultarListaAsync("cat.ppParametro_Listar",
            f => new ParametroDto(f.Texto("Clave"), f.Texto("Valor"), f.Texto("Descripcion")), ct);

    public async Task<int> ObtenerParametroEnteroAsync(string clave, int porDefecto, CancellationToken ct = default)
    {
        var p = await sp.ConsultarUnoAsync("cat.ppParametro_Obtener",
            f => new ParametroDto(f.Texto("Clave"), f.Texto("Valor"), f.Texto("Descripcion")), ct,
            Parametro.Texto("@Clave", clave, 50));
        return int.TryParse(p?.Valor, out var valor) ? valor : porDefecto;
    }

    public async Task ActualizarParametroAsync(string clave, string valor, CancellationToken ct = default)
    {
        Validaciones.Requerido(valor, "valor", 200);
        await sp.EjecutarAsync("cat.ppParametro_Actualizar", ct,
            Parametro.Texto("@Clave", clave, 50),
            Parametro.Texto("@Valor", valor.Trim(), 200));
        Log.Info($"Parámetro {clave} cambiado a '{valor.Trim()}'.");
    }

    /* ------------------------------- Clientes ------------------------------ */

    public Task<List<ClienteResumenDto>> ListarClientesAsync(CancellationToken ct = default)
        => sp.ConsultarListaAsync("seg.ppCliente_Listar", f => new ClienteResumenDto(
            f.Entero("ClienteId"), f.Texto("Codigo"), f.Texto("Nombre"), f.Booleano("Activo"),
            f.Fecha("FechaCreacion"), f.FechaONula("UltimoUso")), ct);

    /// <summary>Da de alta un cliente o le rota la clave. La clave en claro se devuelve solo esta vez.</summary>
    public async Task<ClienteRegistradoDto> RegistrarClienteAsync(string codigo, string nombre, CancellationToken ct = default)
    {
        var cod = Validaciones.Requerido(codigo, "código", 20).ToUpperInvariant();
        var nom = Validaciones.Requerido(nombre, "nombre", 100);
        var clave = ApiKey.Generar();
        var clienteId = Parametro.Salida("@ClienteId", SqlDbType.Int);

        await sp.EjecutarAsync("seg.ppCliente_Registrar", ct,
            Parametro.Texto("@Codigo", cod, 20),
            Parametro.Texto("@Nombre", nom, 100),
            Parametro.Hash("@ApiKeyHash", ApiKey.Hashear(clave)),
            clienteId);

        Log.Info($"API key emitida para el cliente {cod}.");   // la clave nunca va al log
        return new ClienteRegistradoDto(clienteId.Entero() ?? 0, cod, clave);
    }

    public Task CambiarEstadoClienteAsync(string codigo, bool activo, CancellationToken ct = default)
        => sp.EjecutarAsync("seg.ppCliente_CambiarEstado", ct,
            Parametro.Texto("@Codigo", codigo, 20),
            Parametro.Booleano("@Activo", activo));

    /* ------------------------------ Mensajes ------------------------------- */

    public Task<List<MensajeResumenDto>> ListarMensajesAsync(string? estado, DateTime? desde, DateTime? hasta, int top = 200, CancellationToken ct = default)
        => cola.ListarAsync(estado, desde, hasta, top, ct);

    public async Task<MensajeDetalleDto> ObtenerMensajeAsync(Guid mensajeGuid, CancellationToken ct = default)
        => await cola.ObtenerAsync(mensajeGuid, ct)
           ?? throw new RecursoNoEncontradoException("No existe un mensaje con ese identificador.");

    public async Task ReintentarMensajeAsync(Guid mensajeGuid, CancellationToken ct = default)
    {
        var mensaje = await ObtenerMensajeAsync(mensajeGuid, ct);
        await cola.ReintentarAsync(mensaje.MensajeId, ct);
        Log.Info($"Mensaje {mensajeGuid} devuelto a la cola manualmente.");
    }

    /* -------------------------------- Logs --------------------------------- */

    public Task<List<LogDto>> ListarLogsAsync(DateTime? desde, DateTime? hasta, string? nivel, int top = 200, CancellationToken ct = default)
        => sp.ConsultarListaAsync("dbo.ppLog_Listar", f => new LogDto(
                f.Entero("Id"), f.Fecha("Date"), f.Texto("Thread"), f.Texto("Level"), f.Texto("Logger"),
                f.Texto("Message"), f.TextoONulo("Exception")),
            ct,
            Parametro.Fecha("@Desde", desde),
            Parametro.Fecha("@Hasta", hasta),
            Parametro.Texto("@Nivel", nivel, 50),
            Parametro.Entero("@Top", top));

    /* ----------------------------- Simuladores ----------------------------- */

    public IReadOnlyCollection<string> SimuladoresCaidos => simuladores.Caidos;
    public void SimularCaida(string servicio) => simuladores.Caer(servicio);
    public void RestaurarSimulador(string servicio) => simuladores.Restaurar(servicio);

    // -------------------------------------------------------------------------

    internal static ServicioDto MapearServicio(DataRow f) => new(
        f.Texto("Codigo"), f.Texto("Nombre"), f.TextoONulo("UrlBase"), f.Booleano("EsSimulado"),
        f.Texto("EstadoCircuito"), f.Entero("FallosConsecutivos"), f.Entero("UmbralFallos"),
        f.Entero("SegundosApertura"), f.FechaONula("AbiertoHasta"), f.FechaONula("UltimoExito"),
        f.FechaONula("UltimoFallo"), f.Booleano("Activo"));
}
