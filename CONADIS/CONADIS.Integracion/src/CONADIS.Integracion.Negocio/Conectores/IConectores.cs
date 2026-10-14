using CONADIS.Integracion.Negocio.Contratos;

namespace CONADIS.Integracion.Negocio.Conectores;

/*
 * Un conector es la forma de hablar con UN servicio de destino.
 * Hoy todos los implementa un simulador (carpeta Simuladores). Cuando exista el
 * servicio real se escribe otro conector (por ejemplo, un cliente HTTP) con la misma
 * interfaz y se registra en su lugar: los servicios de negocio no cambian.
 *
 * Reglas para todo conector:
 *   - Si el servicio no responde o falla técnicamente -> ServicioExternoNoDisponibleException.
 *   - Si el servicio responde que NO (regla suya)     -> RechazoServicioExternoException.
 * El cortacircuito, la bitácora y la caché los pone ServicioResiliencia, no el conector.
 */

public interface IConectorJce
{
    Task<ValidacionJceDto> ValidarCedulaAsync(string cedula, CancellationToken ct);
}

public interface IConectorMsp
{
    Task<ValidacionMspDto> ValidarDiagnosticoAsync(string cedula, string codigoCie10, CancellationToken ct);
}

public interface IConectorDgii
{
    Task<bool> ValidarRncAsync(string rnc, CancellationToken ct);
    Task<DocumentoFiscalDto> FirmarEcfAsync(FacturaFiscalDto factura, CancellationToken ct);
    Task<RespuestaFiscalDto> EnviarEcfAsync(FacturaFiscalDto factura, CancellationToken ct);
}

public interface IConectorFirma
{
    Task<FirmaDto> FirmarAsync(SolicitudFirmaDto solicitud, CancellationToken ct);
}

public interface IConectorPasarela
{
    Task<AutorizacionPagoDto> AutorizarAsync(SolicitudAutorizacionDto solicitud, CancellationToken ct);
    Task<bool> VerificarAsync(string referencia, decimal monto, CancellationToken ct);
}

public interface IConectorCore
{
    /// <summary>Entrega un mensaje de la cola al Core. Devuelve la respuesta del Core en JSON.</summary>
    Task<string> EntregarAsync(MensajeColaDto mensaje, CancellationToken ct);
}
