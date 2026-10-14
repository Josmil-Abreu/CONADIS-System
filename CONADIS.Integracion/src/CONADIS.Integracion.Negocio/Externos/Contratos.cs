using CONADIS.Integracion.Negocio.Modelos;

namespace CONADIS.Integracion.Negocio.Externos;

// Puertos (interfaces) hacia los servicios del Estado y proveedores (SRS 3.1.3: "todos los adaptadores
// de integración se definen como interfaces ... de modo que puedan sustituirse por simulaciones").
// En este proyecto académico todas las implementaciones son simuladas (Simuladores.cs); para conectar
// el servicio real de la JCE, por ejemplo, basta con otra clase que implemente IServicioJce.

/// <summary>Junta Central Electoral: validación de identidad (RF-INT-01).</summary>
public interface IServicioJce
{
    Task<RespuestaJce> ConsultarCedulaAsync(string cedula, CancellationToken ct = default);
}

/// <summary>Ministerio de Salud / SNS: validación del diagnóstico de origen (RF-INT-02).</summary>
public interface IServicioMsp
{
    /// <returns>Validado, NoCoincide o NoEncontrado.</returns>
    Task<string> ValidarDiagnosticoAsync(ConsultaDiagnostico consulta, CancellationToken ct = default);
}

/// <summary>DGII: consulta de RNC y recepción de comprobantes fiscales electrónicos (e-CF).</summary>
public interface IServicioDgii
{
    Task<RespuestaRnc> ConsultarRncAsync(string rnc, CancellationToken ct = default);
    Task<RespuestaEcf> EnviarComprobanteAsync(ComprobanteFiscal comprobante, CancellationToken ct = default);
}

/// <summary>Proveedor de firma digital acreditado (RF-INT-03).</summary>
public interface IProveedorFirma
{
    Task<FirmaDocumento> FirmarAsync(string contenido, CancellationToken ct = default);
    bool Verificar(string contenido, string firma);
    /// <summary>Fecha de vencimiento del certificado de firma (para alertar 30 días antes).</summary>
    DateOnly VencimientoCertificado { get; }
}

/// <summary>Pasarela de pagos en línea.</summary>
public interface IPasarelaPagos
{
    Task<RespuestaPasarela> CobrarAsync(CobroEnLinea cobro, CancellationToken ct = default);
}

/// <summary>Envío de notificaciones al ciudadano (correo y SMS).</summary>
public interface IEnviadorNotificaciones
{
    Task EnviarAsync(NotificacionCore notificacion, CancellationToken ct = default);
}

/// <summary>Disponibilidad simulada de cada servicio (sección "Simulacion" de appsettings.json).</summary>
public sealed class OpcionesSimulacion
{
    public bool JceDisponible { get; set; } = true;
    public bool MspDisponible { get; set; } = true;
    public bool DgiiDisponible { get; set; } = true;
    public bool FirmaDisponible { get; set; } = true;
    public bool PasarelaDisponible { get; set; } = true;
    /// <summary>Latencia simulada de cada llamada externa (milisegundos).</summary>
    public int LatenciaMs { get; set; } = 80;
    /// <summary>Carpeta donde se "envían" los correos y SMS (un archivo por mensaje).</summary>
    public string CarpetaBuzon { get; set; } = "buzon";
}
