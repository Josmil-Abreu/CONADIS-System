using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Domain.Common;
using CONADIS.Domain.Entities;
using CONADIS.Domain.Enums;
using CONADIS.Application.Dtos;
using CONADIS.Application.Interfaces;

namespace CONADIS.Application.Services;

public class DictamenService : IDictamenService
{
    private readonly IExpedienteRepository _expedienteRepository;
    private readonly IFirmaDigitalService _firmaDigitalService;

    public DictamenService(IExpedienteRepository expedienteRepository, IFirmaDigitalService firmaDigitalService)
    {
        _expedienteRepository = expedienteRepository;
        _firmaDigitalService = firmaDigitalService;
    }

    public async Task<Result<string>> ProcesarDictamenAsync(EmitirDictamenDto dto, CancellationToken ct = default)
    {
        var expediente = await _expedienteRepository.ObtenerPorIdAsync(dto.ExpedienteId, ct);
        if (expediente == null)
            return Result.Failure<string>("Expediente no encontrado.");

        if (expediente.EvaluacionVigente == null)
            return Result.Failure<string>("El expediente debe tener una evaluación consolidada para dictaminar.");

        if (!dto.EsAprobado)
        {
            var rechazoRes = expediente.Rechazar(dto.MotivoRechazo ?? "No cumple con el baremo mínimo.");
            if (rechazoRes.IsFailure) return Result.Failure<string>(rechazoRes.Error);

            await _expedienteRepository.ActualizarAsync(expediente, ct);
            return Result.Success("Expediente Rechazado.");
        }

        var grado = (GradoDiscapacidad)(dto.GradoFinal ?? (int)expediente.EvaluacionVigente.GradoPropuesto!);
        var tipo = (TipoDiscapacidad)(dto.TipoFinal ?? (int)expediente.EvaluacionVigente.TipoPropuesto!);
        var vencimiento = dto.FechaVencimiento ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5));

        var aprobaRes = expediente.Aprobar(expediente.EvaluacionVigente, grado, tipo, vencimiento);
        if (aprobaRes.IsFailure) return Result.Failure<string>(aprobaRes.Error);

        var numCarnet = $"CARNET-{DateTime.UtcNow.Year}-{Random.Shared.Next(10000, 99999)}";
        var tokenSimulado = await _firmaDigitalService.GenerarTokenQrSimuladoAsync(numCarnet, "00000000000", vencimiento, ct);

        var carnet = new Carnet(expediente.Id, numCarnet, DateOnly.FromDateTime(DateTime.UtcNow), vencimiento, tokenSimulado);

        var carnetRes = expediente.Carnetizar(carnet);
        if (carnetRes.IsFailure) return Result.Failure<string>(carnetRes.Error);

        await _expedienteRepository.ActualizarAsync(expediente, ct);
        return Result.Success(numCarnet);
    }
}