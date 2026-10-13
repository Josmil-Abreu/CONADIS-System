namespace CONADIS.Integration.Services.Implementations;

using CONADIS.Domain.Common;
using CONADIS.Integration.Exceptions;
using CONADIS.Integration.HttpClients.Interfaces;
using CONADIS.Integration.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

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
                var errorMsg = response.ErrorMessage ?? "Cédula no válida";
                return Result<bool>.Failure($"[INVALID_CEDULA] {errorMsg}");
            }

            return Result<bool>.Success(true);
        }
        catch (IdentityValidationException ex)
        {
            _logger.LogError(ex, "Error al validar cédula");
            var code = ex.ErrorCode ?? "VALIDATION_ERROR";
            return Result<bool>.Failure($"[{code}] {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al validar cédula");
            return Result<bool>.Failure("[UNKNOWN_ERROR] Error inesperado al validar identidad");
        }
    }

    public async Task<Result<string>> GetCitizenInfoAsync(string cedula, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Obteniendo información del ciudadano: {Cedula}", cedula);

            var response = await _client.GetCitizenInfoAsync(cedula, ct);

            return Result<string>.Success(
                $"{response.FullName} - Fecha de Nacimiento: {response.DateOfBirth?.ToString("yyyy-MM-dd")}");
        }
        catch (IdentityValidationException ex)
        {
            _logger.LogError(ex, "Error al obtener información del ciudadano");
            var code = ex.ErrorCode ?? "INFO_ERROR";
            return Result<string>.Failure($"[{code}] {ex.Message}");
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
                return Result<bool>.Failure($"[CANNOT_VOTE] No puede votar - Estado: {response.Status}");
            }

            return Result<bool>.Success(true);
        }
        catch (IdentityValidationException ex)
        {
            _logger.LogError(ex, "Error al verificar estado electoral");
            var code = ex.ErrorCode ?? "ELECTORAL_ERROR";
            return Result<bool>.Failure($"[{code}] {ex.Message}");
        }
    }
}