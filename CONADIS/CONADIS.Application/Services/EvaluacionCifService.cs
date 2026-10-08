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

public class EvaluacionCifService : IEvaluacionCifService
{
    private readonly IExpedienteRepository _expedienteRepository;

    public EvaluacionCifService(IExpedienteRepository expedienteRepository)
    {
        _expedienteRepository = expedienteRepository;
    }

    public async Task<Result> RegistrarYConsolidarAsync(RegistrarEvaluacionDto dto, CancellationToken ct = default)
    {
        var expediente = await _expedienteRepository.ObtenerPorIdAsync(dto.ExpedienteId, ct);
        if (expediente == null)
            return Result.Failure("El expediente especificado no existe.");

        var evaluacion = new EvaluacionCIF(dto.ExpedienteId, dto.CitaId);

        foreach (var item in dto.CodificacionesCIF)
        {
            var codRes = evaluacion.AgregarCodificacion(new CodificacionCIF(item.Codigo, item.Componente, item.Calificador));
            if (codRes.IsFailure) return codRes;
        }

        evaluacion.RegistrarWhodas(dto.PuntajeWhodas);
        evaluacion.CalcularGradoPropuesto((GradoDiscapacidad)dto.GradoPropuesto, (TipoDiscapacidad)dto.TipoPropuesto);

        var consolidarRes = evaluacion.Consolidar();
        if (consolidarRes.IsFailure) return consolidarRes;

        var asignarRes = expediente.AsignarEvaluacion(evaluacion);
        if (asignarRes.IsFailure) return asignarRes;

        await _expedienteRepository.ActualizarAsync(expediente, ct);
        return Result.Success();
    }
}