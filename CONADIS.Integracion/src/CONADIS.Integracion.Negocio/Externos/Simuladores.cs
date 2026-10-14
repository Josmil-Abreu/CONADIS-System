using System.Security.Cryptography;
using System.Text;
using CONADIS.Integracion.Negocio.Modelos;
using CONADIS.Integracion.Negocio.Seguridad;

namespace CONADIS.Integracion.Negocio.Externos;

/// <summary>
/// Simulador de la JCE. Reglas (para poder demostrar cada caso):
///   - Cédula con formato o dígito verificador incorrecto -> no existe (Inválida).
///   - Cédula que empieza por 999 -> cancelada (persona fallecida) (Inválida).
///   - Cédula que empieza por 888 -> la JCE tarda 10 s (para demostrar el tiempo de espera).
///   - Cualquier otra cédula válida -> Válida, con nombres "oficiales" fijos para esa cédula.
///   - Simulacion:JceDisponible = false -> el servicio no responde (para demostrar el cortacircuito).
/// </summary>
public sealed class SimuladorJce(OpcionesSimulacion opciones) : IServicioJce
{
    private static readonly string[] NombresF = ["María", "Ana", "Rosa", "Carmen", "Luisa", "Altagracia", "Yolanda", "Juana"];
    private static readonly string[] NombresM = ["José", "Juan", "Luis", "Carlos", "Pedro", "Rafael", "Manuel", "Ramón"];
    private static readonly string[] Apellidos = ["Rodríguez", "Pérez", "Martínez", "Gómez", "Sánchez", "Díaz", "Reyes", "Castillo", "Núñez", "Peña"];

    public async Task<RespuestaJce> ConsultarCedulaAsync(string cedula, CancellationToken ct = default)
    {
        var c = ValidadorCedula.Normalizar(cedula);
        await Simulacion.EsperarAsync(opciones, opciones.JceDisponible, "JCE", ct);
        if (c.StartsWith("888")) await Task.Delay(TimeSpan.FromSeconds(10), ct);

        if (!ValidadorCedula.EsValida(c)) return new RespuestaJce(false, null, null, null, null, "La cédula no existe en el registro civil.");
        if (c.StartsWith("999")) return new RespuestaJce(false, null, null, null, null, "La cédula está cancelada (titular fallecido).");

        var semilla = Simulacion.Semilla(c);
        var sexo = c[9] % 2 == 0 ? 2 : 1;
        var nombres = sexo == 2 ? NombresF[semilla % NombresF.Length] : NombresM[semilla % NombresM.Length];
        var apellidos = $"{Apellidos[semilla / 7 % Apellidos.Length]} {Apellidos[semilla / 13 % Apellidos.Length]}";
        var nacimiento = new DateOnly(1950, 1, 1).AddDays(semilla % (365 * 55));
        return new RespuestaJce(true, nombres, apellidos, nacimiento, sexo, null);
    }
}

/// <summary>
/// Simulador del MSP/SNS. Sin número de certificado (o que empieza por 000) -> NoEncontrado;
/// código CIE-10 que empieza por Z (factores, no enfermedades) -> NoCoincide; resto -> Validado.
/// </summary>
public sealed class SimuladorMsp(OpcionesSimulacion opciones) : IServicioMsp
{
    public async Task<string> ValidarDiagnosticoAsync(ConsultaDiagnostico consulta, CancellationToken ct = default)
    {
        await Simulacion.EsperarAsync(opciones, opciones.MspDisponible, "MSP", ct);
        if (string.IsNullOrWhiteSpace(consulta.NumeroCertificado) || consulta.NumeroCertificado.StartsWith("000"))
            return EstadosDiagnostico.NoEncontrado;
        if (consulta.CodigoCie10?.Trim().StartsWith('Z') == true || consulta.CodigoCie10?.Trim().StartsWith('z') == true)
            return EstadosDiagnostico.NoCoincide;
        return EstadosDiagnostico.Validado;
    }
}

/// <summary>
/// Simulador de la DGII. RNC: 9 dígitos; empieza por 0 -> no existe; termina en 9 -> inactivo.
/// e-CF: número con formato E + tipo (2 dígitos) + secuencia (10 dígitos); total negativo -> rechazado.
/// </summary>
public sealed class SimuladorDgii(OpcionesSimulacion opciones) : IServicioDgii
{
    public async Task<RespuestaRnc> ConsultarRncAsync(string rnc, CancellationToken ct = default)
    {
        await Simulacion.EsperarAsync(opciones, opciones.DgiiDisponible, "DGII", ct);
        var r = new string((rnc ?? string.Empty).Where(char.IsDigit).ToArray());
        if (r.Length != 9 || r[0] == '0') return new RespuestaRnc(false, false, null);
        return new RespuestaRnc(true, r[8] != '9', $"Empresa de Demostración {r} SRL");
    }

    public async Task<RespuestaEcf> EnviarComprobanteAsync(ComprobanteFiscal c, CancellationToken ct = default)
    {
        await Simulacion.EsperarAsync(opciones, opciones.DgiiDisponible, "DGII", ct);
        if (c.Encf.Length != 13 || c.Encf[0] != 'E' || !c.Encf[1..].All(char.IsDigit))
            return new RespuestaEcf(EstadosEcf.Rechazado, null, null, "El número de e-CF no tiene el formato E + tipo + 10 dígitos.");
        if (c.Total < 0 || c.Itbis < 0)
            return new RespuestaEcf(EstadosEcf.Rechazado, null, null, "Montos negativos.");

        var codigo = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(c.Encf + c.RncEmisor)))[..6];
        return new RespuestaEcf(EstadosEcf.Aceptado, codigo, Guid.NewGuid().ToString("N"), "Comprobante aceptado.");
    }
}

/// <summary>
/// Simulador de la pasarela de pagos. Token "tok_rechazada" -> rechazada (fondos insuficientes);
/// "tok_error" -> la pasarela falla; cualquier otro token -> aprobada.
/// </summary>
public sealed class SimuladorPasarela(OpcionesSimulacion opciones) : IPasarelaPagos
{
    public async Task<RespuestaPasarela> CobrarAsync(CobroEnLinea cobro, CancellationToken ct = default)
    {
        await Simulacion.EsperarAsync(opciones, opciones.PasarelaDisponible, "Pasarela", ct);
        if (cobro.TokenTarjeta == "tok_error") throw new HttpRequestException("La pasarela respondió 502 (simulado).");
        if (cobro.Monto <= 0) return new RespuestaPasarela(false, null, null, "Monto no válido.");
        if (cobro.TokenTarjeta == "tok_rechazada") return new RespuestaPasarela(false, null, null, "Fondos insuficientes.");

        return new RespuestaPasarela(true, RandomNumberGenerator.GetInt32(100000, 999999).ToString(),
            "PSR-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), null);
    }
}

/// <summary>
/// "Envía" correos y SMS escribiendo un archivo de texto por mensaje en la carpeta buzón
/// (como el servicio de Windows de clase que creaba archivos). Así se puede ver qué recibiría el ciudadano.
/// </summary>
public sealed class EnviadorArchivo(OpcionesSimulacion opciones) : IEnviadorNotificaciones
{
    public async Task EnviarAsync(NotificacionCore n, CancellationToken ct = default)
    {
        Directory.CreateDirectory(opciones.CarpetaBuzon);
        var archivo = Path.Combine(opciones.CarpetaBuzon, $"{DateTime.Now:yyyyMMdd-HHmmss}_{n.Canal}_{n.NotificacionId}.txt");
        var contenido = new StringBuilder()
            .AppendLine($"Canal:  {n.Canal}")
            .AppendLine($"Para:   {n.Destinatario}")
            .AppendLine($"Asunto: {n.Asunto}")
            .AppendLine($"Fecha:  {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}")
            .AppendLine()
            .AppendLine(n.Mensaje)
            .ToString();
        await File.WriteAllTextAsync(archivo, contenido, Encoding.UTF8, ct);
    }
}

internal static class Simulacion
{
    public static async Task EsperarAsync(OpcionesSimulacion o, bool disponible, string servicio, CancellationToken ct)
    {
        if (o.LatenciaMs > 0) await Task.Delay(o.LatenciaMs, ct);
        if (!disponible) throw new HttpRequestException($"{servicio} no disponible (simulado).");
    }

    /// <summary>Número fijo derivado del dato (el mismo dato siempre da el mismo resultado).</summary>
    public static int Semilla(string dato) => BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(dato)), 0) & int.MaxValue;
}
