namespace CONADIS.Integration.Services.Implementations;

using System;
using System.Threading;
using System.Threading.Tasks;
using CONADIS.Domain.Common;
using CONADIS.Integration.Exceptions;
using CONADIS.Integration.HttpClients.Interfaces;
using CONADIS.Integration.Services.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>Implementación del servicio de orquestación de validación de identidad</summary>
public class IdentityValidationService : IIdentityValidationService
{
    private readonly IJceApiClient _client;
    private readonly ILogger<IdentityValidationService> _logger;

    public IdentityValidationService(
        IJceApiClient client,
        ILogger<IdentityValidationService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<Result<bool>> ValidateCedulaAsync(string cedula, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Validando cédula: {Cedula}", cedula);

            var response = await _client.ValidateCedulaAsync(cedula, ct);

            if (!response.IsValid)
            {
                return Result<bool>.Failure(
                    response.ErrorMessage ?? "Cédula no válida",
                    "INVALID_CEDULA");
            }

            return Result<bool>.Success(true);
        }
        catch (IdentityValidationException ex)
        {
            _logger.LogError(ex, "Error al validar cédula");
            return Result<bool>.Failure(ex.Message, ex.ErrorCode ?? "VALIDATION_ERROR");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al validar cédula");
            return Result<bool>.Failure("Error inesperado al validar identidad", "UNKNOWN_ERROR");
        }
    }

    public async Task<Result<string>> GetCitizenInfoAsync(string cedula, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Obteniendo información del ciudadano: {Cedula}", cedula);

            var response = await _client.GetCitizenInfoAsync(cedula, ct);

            return Result<string>.Success(
                $"{response.FullName} - {response.DateOfBirth?.ToString("yyyy-MM-dd")}");
        }
        catch (IdentityValidationException ex)
        {
            _logger.LogError(ex, "Error al obtener información del ciudadano");
            return Result<string>.Failure(ex.Message, ex.ErrorCode ?? "INFO_ERROR");
        }
    }

    public async Task<Result<bool>> CheckElectoralStatusAsync(string cedula, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Verificando estado electoral: {Cedula}", cedula);

            var response = await _client.GetElectoralStatusAsync(cedula, ct);

            if (!response.CanVote)
            {
                return Result<bool>.Failure(
                    $"No puede votar - Estado: {response.Status}",
                    "CANNOT_VOTE");
            }

            return Result<bool>.Success(true);
        }
        catch (IdentityValidationException ex)
        {
            _logger.LogError(ex, "Error al verificar estado electoral");
            return Result<bool>.Failure(ex.Message, ex.ErrorCode ?? "ELECTORAL_ERROR");
        }
    }
}
