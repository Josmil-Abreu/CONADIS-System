using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CONADIS.Integracion.Negocio.Conectores;
using CONADIS.Integracion.Negocio.Contratos;

namespace CONADIS.Integracion.Negocio.Simuladores;

/*
 * Simuladores de los servicios externos. Su comportamiento es determinista (la misma
 * entrada da siempre la misma salida) para que las pruebas y las demostraciones sean
 * repetibles. Ninguno produce documentos con validez legal o fiscal.
 */

/// <summary>JCE simulada: la cédula es válida si su dígito verificador es correcto.</summary>
public sealed class SimuladorJce(EstadoSimuladores estado) : IConectorJce
{
    private static readonly string[] Nombres = ["Juan Pedro", "María Altagracia", "José Miguel", "Ana Rosa", "Luis Manuel", "Carmen Julia"];
    private static readonly string[] Apellidos = ["Pérez García", "Rodríguez Santos", "Martínez Peña", "Gómez Reyes", "Díaz Castillo", "Núñez Rosario"];

    public async Task<ValidacionJceDto> ValidarCedulaAsync(string cedula, CancellationToken ct)
    {
        await estado.AntesDeLlamarAsync(CodigosServicio.Jce, ct);

        if (!Digitos.CedulaValida(cedula))
            return new ValidacionJceDto(cedula, false, null, null, null, null, "La cédula no existe en el padrón (dígito verificador incorrecto).");

        // Datos estables derivados de la cédula
        var semilla = int.Parse(cedula.AsSpan(3, 7));
        var nacimiento = new DateOnly(1950, 1, 1).AddDays(semilla % 20_000);
        return new ValidacionJceDto(
            cedula, true,
            Nombres[semilla % Nombres.Length],
            Apellidos[(semilla / 7) % Apellidos.Length],
            nacimiento,
            semilla % 2 == 0 ? 2 : 1,
            null);
    }
}

/// <summary>MSP simulado: valida el formato CIE-10 y conoce los diagnósticos más comunes de discapacidad.</summary>
public sealed partial class SimuladorMsp(EstadoSimuladores estado) : IConectorMsp
{
    [GeneratedRegex(@"^[A-Z][0-9]{2}(\.[0-9A-Z]{1,4})?$")]
    private static partial Regex FormatoCie10();

    private static readonly Dictionary<string, string> Conocidos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["F70"] = "Retraso mental leve",
        ["F71"] = "Retraso mental moderado",
        ["F84.0"] = "Autismo en la niñez",
        ["F20"] = "Esquizofrenia",
        ["G80"] = "Parálisis cerebral infantil",
        ["G35"] = "Esclerosis múltiple",
        ["G12.2"] = "Enfermedades de las neuronas motoras",
        ["H54.0"] = "Ceguera de ambos ojos",
        ["H54.2"] = "Disminución de la agudeza visual de ambos ojos",
        ["H90.3"] = "Hipoacusia neurosensorial bilateral",
        ["H91.9"] = "Hipoacusia no especificada",
        ["Q90"] = "Síndrome de Down",
        ["Q05"] = "Espina bífida",
        ["T05.4"] = "Amputación traumática de ambas piernas",
        ["S78"] = "Amputación traumática de la cadera y el muslo",
        ["M05"] = "Artritis reumatoide seropositiva"
    };

    public async Task<ValidacionMspDto> ValidarDiagnosticoAsync(string cedula, string codigoCie10, CancellationToken ct)
    {
        await estado.AntesDeLlamarAsync(CodigosServicio.Msp, ct);

        var codigo = codigoCie10.Trim().ToUpperInvariant();
        if (!FormatoCie10().IsMatch(codigo))
            return new ValidacionMspDto(codigo, false, null, "El código no tiene formato CIE-10 (ejemplo: G80 o H90.3).");

        // Busca el código exacto y, si no, su categoría de 3 caracteres
        if (Conocidos.TryGetValue(codigo, out var descripcion) || Conocidos.TryGetValue(codigo[..3], out descripcion))
            return new ValidacionMspDto(codigo, true, descripcion, null);

        return new ValidacionMspDto(codigo, true, "Diagnóstico registrado en el MSP (simulado)", null);
    }
}

/// <summary>DGII simulada: valida RNC, "firma" e-CF (sin validez fiscal) y los acepta.</summary>
public sealed class SimuladorDgii(EstadoSimuladores estado) : IConectorDgii
{
    public async Task<bool> ValidarRncAsync(string rnc, CancellationToken ct)
    {
        await estado.AntesDeLlamarAsync(CodigosServicio.Dgii, ct);
        return Digitos.RncValido(rnc);
    }

    public async Task<DocumentoFiscalDto> FirmarEcfAsync(FacturaFiscalDto f, CancellationToken ct)
    {
        await estado.AntesDeLlamarAsync(CodigosServicio.Dgii, ct);

        var xml = new XElement("ECF",
            new XAttribute("Simulado", true),
            new XAttribute("SinValidezFiscal", true),
            new XElement("Encabezado",
                new XElement("TipoeCF", f.TipoEcf),
                new XElement("eNCF", f.Encf),
                new XElement("RNCEmisor", f.RncEmisor),
                new XElement("RNCComprador", f.DocumentoComprador ?? ""),
                new XElement("FechaEmision", f.FechaEmision.ToString("dd-MM-yyyy")),
                new XElement("MontoTotal", f.Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))),
            new XElement("Detalle",
                f.Lineas.Select((l, i) => new XElement("Item",
                    new XElement("Numero", i + 1),
                    new XElement("Descripcion", l.Descripcion),
                    new XElement("Cantidad", l.Cantidad),
                    new XElement("Precio", l.PrecioUnitario.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))))).ToString();

        var codigoSeguridad = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)))[..6];
        var qr = $"https://ecf.dgii.gov.do/testecf/ConsultaTimbre?RncEmisor={f.RncEmisor}&ENCF={f.Encf}" +
                 $"&MontoTotal={f.Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}&CodigoSeguridad={codigoSeguridad}";

        return new DocumentoFiscalDto(xml, codigoSeguridad, qr, EsPrueba: true);
    }

    public async Task<RespuestaFiscalDto> EnviarEcfAsync(FacturaFiscalDto f, CancellationToken ct)
    {
        await estado.AntesDeLlamarAsync(CodigosServicio.Dgii, ct);

        if (f.Total <= 0)
            throw new RechazoServicioExternoException(CodigosServicio.Dgii, "DGII: el monto total del comprobante debe ser mayor que cero.");
        if (f.DocumentoComprador is { Length: 9 } rnc && !Digitos.RncValido(rnc))
            throw new RechazoServicioExternoException(CodigosServicio.Dgii, $"DGII: el RNC del comprador {rnc} no es válido.");

        var trackId = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(f.Encf))).ToString();
        return new RespuestaFiscalDto(f.Encf, EstadosDgii.Aceptado, trackId, "Aceptado (simulado, sin validez fiscal).");
    }
}

/// <summary>Firma digital simulada con HMAC-SHA256 y una clave fija de demostración.</summary>
public sealed class SimuladorFirma(EstadoSimuladores estado) : IConectorFirma
{
    private static readonly byte[] ClaveDemo = Encoding.UTF8.GetBytes("CONADIS-FIRMA-SIMULADA-SIN-VALIDEZ");

    public async Task<FirmaDto> FirmarAsync(SolicitudFirmaDto s, CancellationToken ct)
    {
        await estado.AntesDeLlamarAsync(CodigosServicio.Firma, ct);

        var firma = Convert.ToBase64String(HMACSHA256.HashData(ClaveDemo,
            Encoding.UTF8.GetBytes($"{s.Usuario}|{s.Referencia}|{s.Contenido}")));
        return new FirmaDto(s.Referencia, firma, "HMAC-SHA256 (simulado)", DateTime.UtcNow, EsPrueba: true);
    }
}

/// <summary>
/// Pasarela de pagos simulada. La tarjeta debe pasar Luhn. Tarjetas de prueba:
///   4111 1111 1111 1111 -> aprobada
///   4000 0000 0000 0002 -> declinada (fondos insuficientes)
/// </summary>
public sealed class SimuladorPasarela(EstadoSimuladores estado) : IConectorPasarela
{
    public async Task<AutorizacionPagoDto> AutorizarAsync(SolicitudAutorizacionDto s, CancellationToken ct)
    {
        await estado.AntesDeLlamarAsync(CodigosServicio.Pasarela, ct);

        if (!Digitos.TarjetaValida(s.NumeroTarjeta))
            return new AutorizacionPagoDto(s.TransaccionId, false, null, "El número de tarjeta no es válido.");
        if (s.NumeroTarjeta.Replace(" ", "").Replace("-", "").EndsWith("0002", StringComparison.Ordinal))
            return new AutorizacionPagoDto(s.TransaccionId, false, null, "Pago declinado: fondos insuficientes.");

        return new AutorizacionPagoDto(s.TransaccionId, true, Referencia(s.TransaccionId, s.Monto), "Aprobado (simulado).");
    }

    public async Task<bool> VerificarAsync(string referencia, decimal monto, CancellationToken ct)
    {
        await estado.AntesDeLlamarAsync(CodigosServicio.Pasarela, ct);
        // Las referencias del simulador llevan el monto codificado: se puede verificar sin guardar nada.
        return referencia.StartsWith("PAS-", StringComparison.Ordinal)
               && referencia.EndsWith("-" + ((long)(monto * 100)).ToString(), StringComparison.Ordinal);
    }

    private static string Referencia(Guid transaccionId, decimal monto)
        => $"PAS-{transaccionId.ToString("N")[..10].ToUpperInvariant()}-{(long)(monto * 100)}";
}

/// <summary>
/// Core simulado mientras no exista su API. Acepta los mensajes de la cola.
/// Para demostrar un rechazo de negocio: un pago con TasaCodigo "RECHAZAR" se rechaza.
/// </summary>
public sealed class SimuladorCore(EstadoSimuladores estado) : IConectorCore
{
    public async Task<string> EntregarAsync(MensajeColaDto mensaje, CancellationToken ct)
    {
        await estado.AntesDeLlamarAsync(CodigosServicio.Core, ct);

        if (mensaje.Tipo == TiposMensaje.PagoRegistrar)
        {
            using var doc = JsonDocument.Parse(mensaje.Payload);
            if (doc.RootElement.TryGetProperty("TasaCodigo", out var tasa) && tasa.GetString() == "RECHAZAR")
                throw new RechazoServicioExternoException(CodigosServicio.Core, "Core: la tasa indicada no existe en el catálogo.");
        }

        return JsonSerializer.Serialize(new
        {
            estado = "Aplicado",
            mensaje = mensaje.MensajeGuid,
            tipo = mensaje.Tipo,
            simulado = true,
            fecha = DateTime.UtcNow
        });
    }
}
