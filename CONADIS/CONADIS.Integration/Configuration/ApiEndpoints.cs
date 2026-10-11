namespace CONADIS.Integration.Configuration;

/// <summary>
/// Configuración centralizada de endpoints de las APIs externas.
/// Permite cambiar URLs sin modificar código.
/// </summary>
public static class ApiEndpoints
{
    /// <summary>Endpoints de la pasarela de pago</summary>
    public static class PaymentGateway
    {
        public const string InitiatePayment = "/payments/initiate";
        public const string GetPaymentStatus = "/payments/{transactionId}/status";
        public const string RefundPayment = "/payments/{transactionId}/refund";
    }

    /// <summary>Endpoints del servicio de firmas digitales (DGII)</summary>
    public static class DigitalSignature
    {
        public const string GenerateSignature = "/signatures/generate";
        public const string ValidateSignature = "/signatures/validate";
        public const string GetCertificateInfo = "/certificates/info";
    }

    /// <summary>Endpoints de la JCE</summary>
    public static class Jce
    {
        public const string ValidateCedula = "/citizens/validate/{cedula}";
        public const string GetCitizenInfo = "/citizens/{cedula}/info";
        public const string GetElectoralStatus = "/citizens/{cedula}/electoral-status";
    }

    /// <summary>Endpoints del MSP</summary>
    public static class Msp
    {
        public const string RequestDiagnostic = "/diagnostics/request";
        public const string GetDiagnosticStatus = "/diagnostics/{requestId}/status";
        public const string GetCertificate = "/certificates/{certificateId}";
    }
}
