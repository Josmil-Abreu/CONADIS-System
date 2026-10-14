using System.Data;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Modelos;

namespace CONADIS.Integracion.Negocio.Repositorios;

/// <summary>integ.Mensaje: la cola de mensajes (outbox) que procesa el servicio de Windows.</summary>
public sealed class RepositorioMensajes(EjecutorSp db)
{
    public async Task<ResultadoEncolar> EncolarAsync(string tipo, string destino, string cargaJson, string? claveUnica = null,
        string? correlacionId = null, CancellationToken ct = default)
    {
        var f = (await db.ConsultarAsync("integ.ppMensaje_Encolar",
        [
            Parametro.TextoAnsi("@Tipo", tipo, 60), Parametro.TextoAnsi("@Destino", destino, 20),
            Parametro.Texto("@Carga", cargaJson), Parametro.TextoAnsi("@ClaveUnica", claveUnica, 100),
            Parametro.TextoAnsi("@CorrelacionId", correlacionId, 64)
        ], ct)).Rows[0];
        return new ResultadoEncolar(f.EnteroLargo("MensajeId"), f.Booleano("Encolado"));
    }

    /// <summary>Toma (y bloquea a nombre del trabajador) los mensajes listos para procesar.</summary>
    public Task<List<MensajeCola>> TomarAsync(string trabajador, int maximo = 20, int segundosBloqueo = 120, string? destino = null,
        CancellationToken ct = default)
        => db.ConsultarListaAsync("integ.ppMensaje_Tomar", f => new MensajeCola(f.EnteroLargo("MensajeId"), f.Texto("Tipo"),
                f.Texto("Destino"), f.Texto("Carga"), f.Entero("Intentos"), f.TextoONulo("CorrelacionId")),
            [
                Parametro.Texto("@Trabajador", trabajador, 100), Parametro.Entero("@Maximo", maximo),
                Parametro.Entero("@SegundosBloqueo", segundosBloqueo), Parametro.TextoAnsi("@Destino", destino, 20)
            ], ct);

    public Task CompletarAsync(long mensajeId, CancellationToken ct = default)
        => db.EjecutarAsync("integ.ppMensaje_Completar", [Parametro.EnteroLargo("@MensajeId", mensajeId)], ct);

    public async Task<ResultadoFallo> RegistrarFalloAsync(long mensajeId, string error, bool permanente = false, CancellationToken ct = default)
    {
        var f = (await db.ConsultarAsync("integ.ppMensaje_RegistrarFallo",
        [
            Parametro.EnteroLargo("@MensajeId", mensajeId), Parametro.Texto("@Error", RepositorioBitacora.Cortar(error, 1000), 1000),
            Parametro.Booleano("@Permanente", permanente)
        ], ct)).Rows[0];
        return new ResultadoFallo(f.Texto("Estado"), f.Entero("Intentos"), f.FechaHora("ProximoIntento"));
    }

    public Task ReintentarAsync(long mensajeId, string usuario, CancellationToken ct = default)
        => db.EjecutarAsync("integ.ppMensaje_Reintentar", [Parametro.EnteroLargo("@MensajeId", mensajeId), Parametro.Texto("@Usuario", usuario, 100)], ct);

    public Task<List<MensajeResumen>> ListarAsync(string? estado = null, string? destino = null, int maximo = 200, CancellationToken ct = default)
        => db.ConsultarListaAsync("integ.ppMensaje_Listar", f => new MensajeResumen(f.EnteroLargo("MensajeId"), f.Texto("Tipo"),
                f.Texto("Destino"), f.Texto("Estado"), f.Entero("Intentos"), f.Entero("MaxIntentos"), f.FechaHora("ProximoIntento"),
                f.TextoONulo("BloqueadoPor"), f.TextoONulo("UltimoError"), f.TextoONulo("CorrelacionId"),
                f.FechaHora("FechaCreacion"), f.FechaHoraONula("FechaCompletado")),
            [Parametro.TextoAnsi("@Estado", estado, 15), Parametro.TextoAnsi("@Destino", destino, 20), Parametro.Entero("@Maximo", maximo)], ct);

    public Task<List<ResumenCola>> ResumenAsync(CancellationToken ct = default)
        => db.ConsultarListaAsync("integ.ppMensaje_Resumen", f => new ResumenCola(f.Texto("Destino"), f.Texto("Estado"),
            f.Entero("Cantidad"), f.FechaHora("MasAntiguo")), ct: ct);

    public async Task<int> PurgarAsync(int diasCompletados = 30, CancellationToken ct = default)
        => (await db.ConsultarAsync("integ.ppMensaje_Purgar", [Parametro.Entero("@DiasCompletados", diasCompletados)], ct)).Rows[0].Entero("Eliminados");
}

/// <summary>integ.PagoRecibido: pagos de la Caja y del portal, guardados junto con su mensaje al Core.</summary>
public sealed class RepositorioPagos(EjecutorSp db)
{
    public async Task<ResultadoRecepcionPago> RecibirAsync(PagoEntrante p, int clienteId, string hashCarga, string cargaJson,
        string? correlacionId, CancellationToken ct = default)
    {
        var f = (await db.ConsultarAsync("integ.ppPago_Recibir",
        [
            Parametro.Guid("@TransaccionId", p.TransaccionId), Parametro.Entero("@ClienteId", clienteId),
            Parametro.TextoAnsi("@Origen", p.Origen, 10), Parametro.TextoAnsi("@NumeroOrden", p.NumeroOrden, 20),
            Parametro.TextoAnsi("@NumeroComprobante", p.NumeroComprobante, 20), Parametro.Decimal("@Total", p.Total),
            Parametro.TextoAnsi("@HashCarga", hashCarga, 64), Parametro.Texto("@Carga", cargaJson),
            Parametro.TextoAnsi("@CorrelacionId", correlacionId, 64)
        ], ct)).Rows[0];
        return new ResultadoRecepcionPago(Mapear(f), f.Booleano("EsNuevo"));
    }

    public Task<PagoRecibido?> ObtenerAsync(Guid transaccionId, int clienteId, CancellationToken ct = default)
        => db.ConsultarUnoAsync("integ.ppPago_Obtener", Mapear,
            [Parametro.Guid("@TransaccionId", transaccionId), Parametro.Entero("@ClienteId", clienteId)], ct);

    public Task RegistrarRespuestaCoreAsync(Guid transaccionId, string estado, string? detalle = null, CancellationToken ct = default)
        => db.EjecutarAsync("integ.ppPago_RegistrarRespuestaCore",
        [
            Parametro.Guid("@TransaccionId", transaccionId), Parametro.TextoAnsi("@Estado", estado, 15),
            Parametro.Texto("@Detalle", RepositorioBitacora.Cortar(detalle, 500), 500)
        ], ct);

    public Task<DataTable> ListarAsync(DateTimeOffset desde, DateTimeOffset hasta, int? clienteId = null, string? estado = null,
        CancellationToken ct = default)
        => db.ConsultarAsync("integ.ppPago_Listar",
        [
            Parametro.FechaHora("@Desde", desde), Parametro.FechaHora("@Hasta", hasta),
            Parametro.Entero("@ClienteId", clienteId), Parametro.TextoAnsi("@Estado", estado, 15)
        ], ct);

    private static PagoRecibido Mapear(DataRow f) => new(f.Guid("TransaccionId"), f.Texto("Origen"), f.TextoONulo("NumeroOrden"),
        f.Texto("NumeroComprobante"), f.Decimal("Total"), f.Texto("Estado"), f.TextoONulo("Detalle"),
        f.FechaHora("FechaRecepcion"), f.FechaHoraONula("FechaConfirmacion"));
}

/// <summary>integ.Idempotencia: claves Idempotency-Key de los POST (RIC-05).</summary>
public sealed class RepositorioIdempotencia(EjecutorSp db)
{
    public async Task<ResultadoIdempotencia> IniciarAsync(int clienteId, string clave, string ruta, string hashPeticion,
        CancellationToken ct = default)
    {
        var f = (await db.ConsultarAsync("integ.ppIdempotencia_Iniciar",
        [
            Parametro.Entero("@ClienteId", clienteId), Parametro.TextoAnsi("@Clave", clave, 100),
            Parametro.Texto("@Ruta", RepositorioBitacora.Cortar(ruta, 200), 200), Parametro.TextoAnsi("@HashPeticion", hashPeticion, 64)
        ], ct)).Rows[0];
        return new ResultadoIdempotencia(f.Texto("Resultado"), f.EnteroONulo("CodigoHttp"), f.TextoONulo("Respuesta"));
    }

    public Task CompletarAsync(int clienteId, string clave, int codigoHttp, string? respuesta, CancellationToken ct = default)
        => db.EjecutarAsync("integ.ppIdempotencia_Completar",
        [
            Parametro.Entero("@ClienteId", clienteId), Parametro.TextoAnsi("@Clave", clave, 100),
            Parametro.Entero("@CodigoHttp", codigoHttp), Parametro.Texto("@Respuesta", respuesta)
        ], ct);

    public Task LiberarAsync(int clienteId, string clave, CancellationToken ct = default)
        => db.EjecutarAsync("integ.ppIdempotencia_Liberar", [Parametro.Entero("@ClienteId", clienteId), Parametro.TextoAnsi("@Clave", clave, 100)], ct);

    public async Task<int> PurgarAsync(CancellationToken ct = default)
        => (await db.ConsultarAsync("integ.ppIdempotencia_Purgar", ct: ct)).Rows[0].Entero("Eliminados");
}

/// <summary>seg.ClienteApi: sistemas autorizados a llamar a Integración.</summary>
public sealed class RepositorioClientes(EjecutorSp db)
{
    public Task<ClienteAutenticado?> ObtenerPorHashAsync(string hashClave, CancellationToken ct = default)
        => db.ConsultarUnoAsync("seg.ppCliente_ObtenerPorHash", f => new ClienteAutenticado(f.Entero("ClienteId"), f.Texto("Nombre"),
                f.Texto("Tipo"), f.Texto("Ambitos").Split(' ', StringSplitOptions.RemoveEmptyEntries), f.Booleano("VerTipoGrado"),
                f.FechaONula("ConvenioHasta"), f.Booleano("ConvenioVigente")),
            [Parametro.TextoAnsi("@HashClave", hashClave, 64)], ct);

    public Task<DataTable> ListarAsync(CancellationToken ct = default) => db.ConsultarAsync("seg.ppCliente_Listar", ct: ct);
}
