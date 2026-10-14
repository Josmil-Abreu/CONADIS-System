using CONADIS.Caja.Negocio.Datos;
using CONADIS.Caja.Negocio.Modelos;
using CONADIS.Caja.Negocio.Seguridad;

namespace CONADIS.Caja.Negocio.Servicios;

/// <summary>
/// Reglas del cobro que no necesitan base de datos (se validan antes de llamar al SP, que vuelve a validarlas).
/// </summary>
public static class ReglasCobro
{
    public static bool EsRnc(string? documento) => Solo(documento).Length == 9;

    public static string Solo(string? documento) => new((documento ?? "").Where(char.IsDigit).ToArray());

    /// <summary>
    /// Elige el tipo de e-CF (RF-CAJ-02):
    ///  - Gubernamental marcado → E45 (requiere RNC).
    ///  - Crédito fiscal pedido y RNC activo en la DGII → E31.
    ///  - Resto → E32 (consumo). Si se pidió crédito fiscal sin poder verificar el RNC (sin conexión) se rechaza:
    ///    el cajero puede emitir E32 o esperar a que vuelva la conexión.
    /// </summary>
    public static string ElegirTipo(SolicitudCobro s, RespuestaRnc? rnc, bool sinConexion)
    {
        if (!s.QuiereCreditoFiscal && !s.EsGubernamental) return TiposEcf.Consumo;
        if (!EsRnc(s.DocumentoCliente))
            throw new ReglaNegocioException(58101, "El comprobante de crédito fiscal o gubernamental requiere un RNC de 9 dígitos.");
        if (sinConexion)
            throw new ReglaNegocioException(58102, "Sin conexión con la DGII no se puede verificar el RNC. Emita un comprobante de consumo (E32) o intente más tarde.");
        if (rnc is null || !rnc.Existe)
            throw new ReglaNegocioException(58103, "El RNC no existe en la DGII.");
        if (!rnc.Activo)
            throw new ReglaNegocioException(58104, "El RNC no está activo en la DGII.");
        return s.EsGubernamental ? TiposEcf.Gubernamental : TiposEcf.CreditoFiscal;
    }

    /// <summary>Valida la solicitud antes de enviarla al SP. Devuelve el documento normalizado.</summary>
    public static string Validar(SolicitudCobro s)
    {
        if (s.Lineas is null || s.Lineas.Count == 0)
            throw new ReglaNegocioException(58105, "Agregue al menos un servicio a cobrar.");
        if (s.Lineas.Any(l => l.Cantidad <= 0 || l.Cantidad > 100))
            throw new ReglaNegocioException(58106, "La cantidad de cada servicio debe estar entre 1 y 100.");
        if (s.Lineas.GroupBy(l => l.TasaCodigo.Trim().ToUpperInvariant()).Any(g => g.Count() > 1))
            throw new ReglaNegocioException(58107, "Un mismo servicio aparece dos veces: ajuste la cantidad.");
        if (string.IsNullOrWhiteSpace(s.NombreCliente) || s.NombreCliente.Trim().Length < 3)
            throw new ReglaNegocioException(58108, "Escriba el nombre del cliente.");

        var doc = Solo(s.DocumentoCliente);
        if (doc.Length == 11 && !ValidadorCedula.EsValida(doc))
            throw new ReglaNegocioException(58109, "La cédula no es válida.");
        if (doc.Length is not (9 or 11))
            throw new ReglaNegocioException(58110, "El documento debe ser una cédula (11 dígitos) o un RNC (9 dígitos).");

        switch (s.MedioPago)
        {
            case MediosPagoCaja.Efectivo:
                if (s.MontoRecibido is null or <= 0)
                    throw new ReglaNegocioException(58111, "Indique el monto recibido en efectivo.");
                break;
            case MediosPagoCaja.Tarjeta:
                if (string.IsNullOrWhiteSpace(s.Autorizacion))
                    throw new ReglaNegocioException(58112, "Indique el número de autorización del POS.");
                break;
            case MediosPagoCaja.Transferencia:
                if (string.IsNullOrWhiteSpace(s.Referencia))
                    throw new ReglaNegocioException(58113, "Indique la referencia de la transferencia.");
                break;
            default:
                throw new ReglaNegocioException(58114, "Medio de pago no válido.");
        }
        return doc;
    }

    /// <summary>Calcula el total (igual que el SP) para mostrarlo antes de cobrar.</summary>
    public static (decimal Subtotal, decimal Itbis, decimal Total) Calcular(IEnumerable<LineaCobro> lineas, IReadOnlyDictionary<string, TasaLocal> tasas)
    {
        decimal sub = 0, itbis = 0;
        foreach (var l in lineas)
        {
            if (!tasas.TryGetValue(l.TasaCodigo, out var t) || !t.Activa)
                throw new ReglaNegocioException(58004, $"El servicio {l.TasaCodigo} no existe o no está activo.");
            var bruto = t.Monto * l.Cantidad;
            sub += bruto;
            itbis += Math.Round(bruto * t.ItbisPorcentaje / 100m, 2, MidpointRounding.AwayFromZero);
        }
        return (sub, itbis, sub + itbis);
    }

    public static decimal Cambio(decimal total, decimal recibido)
    {
        if (recibido < total) throw new ReglaNegocioException(58006, "El monto recibido es menor que el total.");
        return recibido - total;
    }

    /// <summary>Convierte la respuesta de la DGII al estado que se guarda en la factura.</summary>
    public static string EstadoDesdeDgii(string? estado) => estado switch
    {
        EstadosDgii.Aceptado => EstadosDgii.Aceptado,
        EstadosDgii.AceptadoCondicional => EstadosDgii.AceptadoCondicional,
        EstadosDgii.Rechazado => EstadosDgii.Rechazado,
        _ => EstadosDgii.Contingencia
    };

    /// <summary>Estado local de una línea según lo que responde Integración / el Core.</summary>
    public static string EstadoLinea(string? estadoIntegracion) => estadoIntegracion switch
    {
        "Aplicado" => "Aplicado",
        "Duplicado" => "Duplicado",
        "Rechazado" => "Rechazado",
        _ => "Recibido"
    };
}
