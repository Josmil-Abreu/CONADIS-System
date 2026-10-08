using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Domain.Common;
using CONADIS.Domain.Entities;
using CONADIS.Domain.Enums;
using CONADIS.Domain.ValueObjects;
using CONADIS.Application.Dtos;
using CONADIS.Application.Interfaces;

namespace CONADIS.Application.Services;

public class SolicitudExpedienteService : ISolicitudExpedienteService
{
    private readonly IExpedienteRepository _expedienteRepository;
    private readonly IJceIdentityService _jceIdentityService;

    public SolicitudExpedienteService(
        IExpedienteRepository expedienteRepository,
        IJceIdentityService jceIdentityService)
    {
        _expedienteRepository = expedienteRepository;
        _jceIdentityService = jceIdentityService;
    }

    public async Task<Result<ExpedienteDetalleDto>> RegistrarSolicitudAsync(RegistrarSolicitudDto dto, CancellationToken ct = default)
    {
        // 1. Validar cédula con la JCE (RF-INT-01)
        var validacionJce = await _jceIdentityService.ValidarCedulaAsync(dto.Cedula, ct);
        if (!validacionJce.EsValida)
        {
            return Result.Failure<ExpedienteDetalleDto>($"La cédula '{dto.Cedula}' no pudo ser validada con la JCE.");
        }

        // 2. Crear objetos de valor y persona
        var cedula = new Cedula(dto.Cedula);
        var nombre = new NombreCompleto(validacionJce.Nombres, validacionJce.Apellidos);
        var contacto = new Contacto(dto.Telefono, dto.Email, "Email");
        var direccion = new Direccion(dto.Provincia, dto.Municipio, dto.Sector, dto.DireccionDetalle);

        var personaResult = PersonaConDiscapacidad.Crear(
            cedula,
            nombre,
            validacionJce.FechaNacimiento,
            (Sexo)dto.Sexo,
            contacto,
            direccion
        );

        if (personaResult.IsFailure)
        {
            return Result.Failure<ExpedienteDetalleDto>(personaResult.Error);
        }

        var persona = personaResult.Value;
        persona.RegistrarValidacionJce(true);

        // 3. Crear el expediente único
        var correlativo = Random.Shared.Next(10000, 99999);
        var numeroExpediente = $"EXP-{DateTime.UtcNow.Year}-{correlativo}";

        var expedienteResult = Expediente.Crear(numeroExpediente, persona.Id);
        if (expedienteResult.IsFailure)
        {
            return Result.Failure<ExpedienteDetalleDto>(expedienteResult.Error);
        }

        var expediente = expedienteResult.Value;

        // 4. Guardar en repositorio
        await _expedienteRepository.AgregarAsync(expediente, ct);

        // 5. Retornar DTO con el resultado
        var detalle = new ExpedienteDetalleDto(
            expediente.Id,
            expediente.NumeroExpediente,
            persona.Cedula?.Valor ?? "",
            persona.Nombre.Completo,
            expediente.Estado.ToString(),
            expediente.FechaSolicitud,
            expediente.Grado?.ToString(),
            expediente.Tipo?.ToString(),
            expediente.CarnetVigente?.NumeroCarnet
        );

        return Result.Success(detalle);
    }

    public async Task<Result<ExpedienteDetalleDto>> ObtenerPorNumeroAsync(string numeroExpediente, CancellationToken ct = default)
    {
        var expediente = await _expedienteRepository.ObtenerPorNumeroAsync(numeroExpediente, ct);
        if (expediente == null)
        {
            return Result.Failure<ExpedienteDetalleDto>($"No se encontró ningún expediente con el número '{numeroExpediente}'.");
        }

        var detalle = new ExpedienteDetalleDto(
            expediente.Id,
            expediente.NumeroExpediente,
            "",
            "",
            expediente.Estado.ToString(),
            expediente.FechaSolicitud,
            expediente.Grado?.ToString(),
            expediente.Tipo?.ToString(),
            expediente.CarnetVigente?.NumeroCarnet
        );

        return Result.Success(detalle);
    }
}