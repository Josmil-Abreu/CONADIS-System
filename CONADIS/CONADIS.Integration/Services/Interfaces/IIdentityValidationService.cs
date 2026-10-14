namespace CONADIS.Integration.Services.Interfaces;

using CONADIS.Domain.Common;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Servicio de orquestación de validación de identidad.
/// Se encarga de validar identidades contra la base de datos de la JCE.
/// </summary>
public interface IIdentityValidationService
{
    /// <summary>Valida una cédula contra la JCE</summary>
    Task<Result<bool>> ValidateCedulaAsync(string cedula, CancellationToken ct = default);

    /// <summary>Obtiene información del ciudadano</summary>
    Task<Result<string>> GetCitizenInfoAsync(string cedula, CancellationToken ct = default);

    /// <summary>Verifica el estado electoral</summary>
    Task<Result<bool>> CheckElectoralStatusAsync(string cedula, CancellationToken ct = default);
}
