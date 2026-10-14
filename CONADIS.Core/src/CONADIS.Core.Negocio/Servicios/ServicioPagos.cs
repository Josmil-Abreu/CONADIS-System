using System.Data;
using CONADIS.Core.Negocio.Datos;
using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;
using log4net;

namespace CONADIS.Core.Negocio.Servicios;

/// <summary>
/// Órdenes de cobro y registro de pagos que llegan desde la Caja o el portal (vía Integración).
/// El precio sale siempre del catálogo (RINV-07) y un pago repetido nunca se registra dos veces (RIC-05).
/// </summary>
public sealed class ServicioPagos(EjecutorSp db)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioPagos));

    public Task<OrdenCobro?> ObtenerOrdenAsync(string numeroOrden, CancellationToken ct = default)
        => db.ConsultarUnoAsync("core.ppOrden_Obtener", MapearOrden,
            [Parametro.TextoAnsi("@NumeroOrden", numeroOrden.Trim().ToUpperInvariant(), 20)], ct);

    public async Task<OrdenCobro> CrearOrdenAsync(string tasaCodigo, int cantidad, string documento, string nombrePagador,
        string? referencia, ContextoAccion contexto, CancellationToken ct = default)
    {
        var doc = ValidadorCedula.Normalizar(documento);
        if (doc.Length == 11 && !ValidadorCedula.EsValida(doc))
            throw new ReglaNegocioException(0, "La cédula del pagador no es válida.");
        if (doc.Length is not (9 or 11))
            throw new ReglaNegocioException(0, "El documento debe ser una cédula (11 dígitos) o un RNC (9 dígitos).");
        if (string.IsNullOrWhiteSpace(nombrePagador))
            throw new ReglaNegocioException(0, "Escribe el nombre de quien paga.");

        var tabla = await db.ConsultarAsync("core.ppOrden_Crear",
        [
            Parametro.TextoAnsi("@TasaCodigo", tasaCodigo.Trim().ToUpperInvariant(), 20),
            Parametro.Entero("@Cantidad", cantidad),
            Parametro.TextoAnsi("@Documento", doc, 11),
            Parametro.Texto("@NombrePagador", nombrePagador.Trim(), 150),
            Parametro.TextoAnsi("@Referencia", string.IsNullOrWhiteSpace(referencia) ? null : referencia.Trim(), 20),
            Parametro.Texto("@Usuario", contexto.Usuario, 100)
        ], ct);
        return MapearOrden(tabla.Rows[0]);
    }

    /// <summary>
    /// Registra un pago. Devuelve "Aplicado" o "Duplicado" (si el TransaccionId ya existía).
    /// Duplicado NO es un error: es la respuesta correcta cuando la Caja reenvía un cobro tras una caída.
    /// </summary>
    public async Task<string> RegistrarPagoAsync(NuevoPago p, CancellationToken ct = default)
    {
        if (p.TransaccionId == Guid.Empty) throw new ReglaNegocioException(0, "Falta el identificador de la transacción.");
        if (p.Origen is not ("CAJA" or "WEB")) throw new ReglaNegocioException(0, "Origen del pago no válido (CAJA o WEB).");
        if (!Valores.MediosPago.Contains(p.MedioPago)) throw new ReglaNegocioException(0, "Medio de pago no válido.");
        if (p.Total < 0 || p.Cantidad < 1) throw new ReglaNegocioException(0, "Monto o cantidad no válidos.");
        if (string.IsNullOrWhiteSpace(p.NumeroComprobante)) throw new ReglaNegocioException(0, "Falta el número de comprobante.");

        var tabla = await db.ConsultarAsync("core.ppPago_Registrar",
        [
            Parametro.Guid("@TransaccionId", p.TransaccionId),
            Parametro.TextoAnsi("@Origen", p.Origen, 10),
            Parametro.TextoAnsi("@NumeroComprobante", p.NumeroComprobante.Trim(), 20),
            Parametro.TextoAnsi("@TasaCodigo", p.TasaCodigo.Trim().ToUpperInvariant(), 20),
            Parametro.Entero("@Cantidad", p.Cantidad),
            Parametro.Decimal("@Total", p.Total),
            Parametro.Texto("@MedioPago", p.MedioPago, 20),
            Parametro.TextoAnsi("@Documento", ValidadorCedula.Normalizar(p.Documento), 11),
            Parametro.Texto("@NombrePagador", p.NombrePagador.Trim(), 150),
            Parametro.TextoAnsi("@NumeroOrden", string.IsNullOrWhiteSpace(p.NumeroOrden) ? null : p.NumeroOrden.Trim(), 20),
            Parametro.TextoAnsi("@Referencia", string.IsNullOrWhiteSpace(p.Referencia) ? null : p.Referencia.Trim(), 20),
            Parametro.Texto("@Caja", p.Caja, 50),
            Parametro.Texto("@Cajero", p.Cajero, 50),
            Parametro.FechaHora("@FechaPago", p.FechaPago)
        ], ct);

        var estado = tabla.Rows[0].Texto("Estado");
        if (estado == EstadosPago.Duplicado)
            Log.Info($"Pago {p.TransaccionId} recibido de nuevo desde {p.Origen}: ya estaba registrado (idempotencia).");
        else
            Log.Info($"Pago {p.TransaccionId} ({p.Origen}, RD${p.Total:N2}) registrado.");
        return estado;
    }

    private static OrdenCobro MapearOrden(DataRow f) => new(f.Texto("NumeroOrden"), f.Texto("TasaCodigo"), f.Texto("Concepto"),
        f.Texto("Documento"), f.Texto("NombrePagador"), f.TextoONulo("Referencia"), f.Entero("Cantidad"),
        f.Decimal("MontoUnitario"), f.Decimal("Total"), f.Texto("Estado"), f.FechaHora("FechaCreacion"),
        f.FechaHoraONula("FechaPago"), f.TextoONulo("NumeroComprobante"));
}