namespace CONADIS.Integration.Mocks;

using CONADIS.Integration.Models.Responses;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Interfaz para el servicio mock de JCE</summary>
public interface IJceMockService
{
    Task<JceValidationResponse> ValidateCedulaMockAsync(string cedula, CancellationToken ct = default);
    Task<JceCitizenInfoResponse> GetCitizenInfoMockAsync(string cedula, CancellationToken ct = default);
    Task<JceElectoralStatusResponse> GetElectoralStatusMockAsync(string cedula, CancellationToken ct = default);
}

/// <summary>Mock de la JCE para desarrollo y pruebas sin conectarse a la API real</summary>
public class JceMockService : IJceMockService
{
    private readonly ILogger<JceMockService> _logger;

    public JceMockService(ILogger<JceMockService> logger)
    {
        _logger = logger;
    }

    public async Task<JceValidationResponse> ValidateCedulaMockAsync(string cedula, CancellationToken ct = default)
    {
        // Simular latencia de red
        await Task.Delay(Random.Shared.Next(100, 300), ct);

        _logger.LogInformation("Mock JCE: Validando cédula {Cedula}", cedula);

        // Lógica mock: cédulas válidas tienen 11 dígitos
        bool isValid = cedula.Length == 11 && cedula.All(char.IsDigit);

        if (isValid)
        {
            return new JceValidationResponse
            {
                IsValid = true,
                Cedula = cedula,
                FirstName = "Juan",
                LastName = "Pérez",
                Status = "Activo"
            };
        }

        return new JceValidationResponse
        {
            IsValid = false,
            Cedula = cedula,
            ErrorMessage = "Cédula no encontrada en la base de datos"
        };
    }

    public async Task<JceCitizenInfoResponse> GetCitizenInfoMockAsync(string cedula, CancellationToken ct = default)
    {
        await Task.Delay(Random.Shared.Next(100, 300), ct);

        _logger.LogInformation("Mock JCE: Obteniendo información de ciudadano {Cedula}", cedula);

        if (cedula.Length != 11 || !cedula.All(char.IsDigit))
        {
            return new JceCitizenInfoResponse
            {
                Cedula = cedula,
                FullName = "No encontrado"
            };
        }

        return new JceCitizenInfoResponse
        {
            Cedula = cedula,
            FullName = "Juan Pedro Pérez García",
            DateOfBirth = new DateTime(1980, 05, 15),
            Gender = "M",
            Address = "Calle Principal 123, Santo Domingo",
            ElectoralStatus = "Activo"
        };
    }

    public async Task<JceElectoralStatusResponse> GetElectoralStatusMockAsync(string cedula, CancellationToken ct = default)
    {
        await Task.Delay(Random.Shared.Next(100, 300), ct);

        _logger.LogInformation("Mock JCE: Obteniendo estado electoral {Cedula}", cedula);

        if (cedula.Length != 11 || !cedula.All(char.IsDigit))
        {
            return new JceElectoralStatusResponse
            {
                Cedula = cedula,
                Status = "Inválido",
                CanVote = false
            };
        }

        return new JceElectoralStatusResponse
        {
            Cedula = cedula,
            Status = "Activo",
            CanVote = true,
            LastUpdated = DateTime.UtcNow.AddMonths(-1)
        };
    }
}
