namespace CONADIS.Integration.HttpClients.Interfaces;

using CONADIS.Integration.Models.Responses;

/// <summary>
/// Cliente HTTP para la JCE (Junta Central Electoral).
/// Se encarga de validar identidades y datos de ciudadanos contra la base de datos de la JCE.
/// </summary>
public interface IJceApiClient
{
    /// <summary>Valida una cédula contra la base de datos de la JCE</summary>
    Task<JceValidationResponse> ValidateCedulaAsync(string cedula, CancellationToken ct = default);

    /// <summary>Obtiene información del ciudadano registrado en la JCE</summary>
    Task<JceCitizenInfoResponse> GetCitizenInfoAsync(string cedula, CancellationToken ct = default);

    /// <summary>Verifica el estado electoral de un ciudadano</summary>
    Task<JceElectoralStatusResponse> GetElectoralStatusAsync(string cedula, CancellationToken ct = default);
}
