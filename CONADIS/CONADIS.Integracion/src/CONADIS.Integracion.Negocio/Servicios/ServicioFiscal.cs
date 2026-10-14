using System.Text.Json;
using log4net;
using CONADIS.Integracion.Negocio.Conectores;
using CONADIS.Integracion.Negocio.Contratos;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Comprobantes fiscales electrónicos (e-CF) de la Caja ante la DGII.
/// Si la DGII no responde al enviar, el comprobante queda en la cola (contingencia)
/// y el Servicio de Windows lo envía cuando vuelva.
/// </summary>
public sealed class ServicioFiscal(
    ServicioResiliencia resiliencia, ServicioCache cache, ServicioCola cola, IConectorDgii dgii)
    : ServicioConsultaBase(resiliencia, cache)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioFiscal));
    private readonly ServicioResiliencia _resiliencia = resiliencia;

    public async Task<ResultadoConsulta<bool>> ValidarRncAsync(string? rnc, CancellationToken ct = default)
    {
        var limpio = Validaciones.Rnc(rnc);
        return await ConsultarAsync(CodigosServicio.Dgii, "DGII.ValidarRnc", $"dgii:rnc:{limpio}",
            c => dgii.ValidarRncAsync(limpio, c),
            valido => valido,
            ct);
    }

    public Task<DocumentoFiscalDto> FirmarAsync(FacturaFiscalDto factura, CancellationToken ct = default)
    {
        Validar(factura);
        return _resiliencia.EjecutarAsync(CodigosServicio.Dgii, "DGII.FirmarEcf", c => dgii.FirmarEcfAsync(factura, c), ct);
    }

    public async Task<RespuestaFiscalDto> EnviarAsync(ClienteApi cliente, FacturaFiscalDto factura, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        Validar(factura);

        try
        {
            return await _resiliencia.EjecutarAsync(CodigosServicio.Dgii, "DGII.EnviarEcf", c => dgii.EnviarEcfAsync(factura, c), ct);
        }
        catch (ServicioExternoNoDisponibleException)
        {
            // Contingencia: el e-NCF es la clave de idempotencia, así que reenviar la misma
            // factura mientras la DGII sigue caída no crea mensajes repetidos.
            var encolado = await cola.EncolarAsync(cliente.ClienteId, CodigosServicio.Dgii, TiposMensaje.EcfEnviar,
                JsonSerializer.Serialize(factura, ServicioCache.Json), factura.Encf, ct);

            Log.Warn($"DGII no disponible: e-CF {factura.Encf} en cola (mensaje {encolado.MensajeGuid}).");
            return new RespuestaFiscalDto(factura.Encf, EstadosDgii.EnCola, encolado.MensajeGuid.ToString(),
                "La DGII no está disponible: el comprobante quedó en cola y se enviará automáticamente.");
        }
    }

    private static void Validar(FacturaFiscalDto? f)
    {
        if (f is null) throw new SolicitudInvalidaException("Falta el comprobante.");
        if (f.FacturaId == Guid.Empty) throw new SolicitudInvalidaException("El identificador de la factura es obligatorio.");
        Validaciones.Requerido(f.Encf, "e-NCF", 13);
        Validaciones.Requerido(f.TipoEcf, "tipo de e-CF", 3);
        Validaciones.Rnc(f.RncEmisor);
        if (f.DocumentoComprador is not null) Validaciones.Documento(f.DocumentoComprador);
        Validaciones.Monto(f.Total, "total");
        if (f.Lineas is null || f.Lineas.Count == 0)
            throw new SolicitudInvalidaException("El comprobante debe tener al menos una línea.");
        if (decimal.Round(f.Subtotal + f.Itbis, 2) != f.Total)
            throw new SolicitudInvalidaException("El total del comprobante no coincide con subtotal + ITBIS.");
    }
}

/// <summary>
/// Eventos de la Caja para el Core (apertura, cierre, anulación...). Se entregan por la
/// cola: la Caja no espera al Core y el MensajeId evita duplicados si lo reenvía.
/// </summary>
public sealed class ServicioEventos(ServicioCola cola)
{
    public Task<MensajeEncoladoDto> PublicarAsync(ClienteApi cliente, EventoDto evento, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(evento);
        if (evento.MensajeId == Guid.Empty)
            throw new SolicitudInvalidaException("El identificador del evento (MensajeId) es obligatorio.");
        var tipo = Validaciones.Requerido(evento.Tipo, "tipo de evento", 50);
        Validaciones.Json(evento.DatosJson, "datos del evento");

        JsonElement? datos = null;
        if (evento.DatosJson is not null)
        {
            using var documento = JsonDocument.Parse(evento.DatosJson);
            datos = documento.RootElement.Clone();
        }

        var payload = JsonSerializer.Serialize(new
        {
            evento.MensajeId,
            evento.RecursoId,
            Tipo = tipo,
            Origen = cliente.Codigo,
            Datos = datos,
            Fecha = DateTime.UtcNow
        }, ServicioCache.Json);

        return cola.EncolarAsync(cliente.ClienteId, CodigosServicio.Core, TiposMensaje.EventoCaja, payload,
            evento.MensajeId.ToString(), ct);
    }
}
