namespace CONADIS.Integration.Configuration;

/// <summary>Configuración general de la capa de integración</summary>
public class IntegrationSettings
{
    /// <summary>Configuración de la pasarela de pago</summary>
    public PaymentGatewaySettings PaymentGateway { get; set; } = new();

    /// <summary>Configuración del servicio de firmas digitales (DGII)</summary>
    public DigitalSignatureSettings DigitalSignature { get; set; } = new();

    /// <summary>Configuración de la API de la JCE</summary>
    public JceSettings Jce { get; set; } = new();

    /// <summary>Configuración de la API del MSP</summary>
    public MspSettings Msp { get; set; } = new();

    /// <summary>Configuración de políticas de reintentos</summary>
    public RetryPolicySettings RetryPolicy { get; set; } = new();

    /// <summary>Indica si usar mocks en lugar de APIs reales</summary>
    public bool UseMocks { get; set; } = true;
}

/// <summary>Configuración de la pasarela de pago</summary>
public class PaymentGatewaySettings
{
    /// <summary>URL base de la API de pagos</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>API Key de autenticación</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Secret Key de autenticación</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Timeout en segundos para las solicitudes</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Versión de la API a usar</summary>
    public string ApiVersion { get; set; } = "v1";
}

/// <summary>Configuración del servicio de firmas digitales</summary>
public class DigitalSignatureSettings
{
    /// <summary>URL base de la API de firmas (DGII)</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Ruta del certificado digital</summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>Contraseña del certificado</summary>
    public string CertificatePassword { get; set; } = string.Empty;

    /// <summary>Timeout en segundos para las solicitudes</summary>
    public int TimeoutSeconds { get; set; } = 45;

    /// <summary>API Key de autenticación</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Versión de la API a usar</summary>
    public string ApiVersion { get; set; } = "v1";
}

/// <summary>Configuración de la JCE</summary>
public class JceSettings
{
    /// <summary>URL base de la API de la JCE</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>API Key de autenticación</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Timeout en segundos para las solicitudes</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Versión de la API a usar</summary>
    public string ApiVersion { get; set; } = "v1";
}

/// <summary>Configuración del MSP</summary>
public class MspSettings
{
    /// <summary>URL base de la API del MSP</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>API Key de autenticación</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Timeout en segundos para las solicitudes</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Versión de la API a usar</summary>
    public string ApiVersion { get; set; } = "v1";
}

/// <summary>Configuración de políticas de reintentos con backoff exponencial</summary>
public class RetryPolicySettings
{
    /// <summary>Número máximo de reintentos</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Retraso inicial en milisegundos</summary>
    public int InitialDelayMs { get; set; } = 100;

    /// <summary>Multiplicador de backoff exponencial</summary>
    public double BackoffMultiplier { get; set; } = 2.0;
}
