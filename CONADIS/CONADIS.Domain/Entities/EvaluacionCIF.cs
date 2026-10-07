using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Domain.Common;
using CONADIS.Domain.Enums;
using CONADIS.Domain.ValueObjects;

namespace CONADIS.Domain.Entities;

public class EvaluacionCIF
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ExpedienteId { get; private set; }
    public Guid CitaId { get; private set; }
    public DateTime FechaEvaluacion { get; private set; } = DateTime.UtcNow;

    private readonly List<CodificacionCIF> _codificaciones = new();
    public IReadOnlyCollection<CodificacionCIF> Codificaciones => _codificaciones.AsReadOnly();

    public decimal PuntajeWhodas { get; private set; }
    public GradoDiscapacidad? GradoPropuesto { get; private set; }
    public TipoDiscapacidad? TipoPropuesto { get; private set; }
    public string VersionBaremo { get; private set; } = "BAR-2026.1";
    public EstadoEvaluacion Estado { get; private set; } = EstadoEvaluacion.Borrador;

    private EvaluacionCIF() { }

    public EvaluacionCIF(Guid expedienteId, Guid citaId)
    {
        ExpedienteId = expedienteId;
        CitaId = citaId;
    }

    public Result AgregarCodificacion(CodificacionCIF codificacion)
    {
        if (Estado == EstadoEvaluacion.Consolidada)
            return Result.Failure("No se pueden agregar códigos a una evaluación consolidada.");

        if (codificacion.Calificador1 < 0 || codificacion.Calificador1 > 4)
            return Result.Failure("El calificador principal CIF debe estar entre 0 y 4.");

        _codificaciones.Add(codificacion);
        return Result.Success();
    }

    public void RegistrarWhodas(decimal puntajeGlobal)
    {
        PuntajeWhodas = puntajeGlobal;
    }

    public void CalcularGradoPropuesto(GradoDiscapacidad grado, TipoDiscapacidad tipo)
    {
        GradoPropuesto = grado;
        TipoPropuesto = tipo;
    }

    public Result Consolidar()
    {
        if (_codificaciones.Count == 0)
            return Result.Failure("No se puede consolidar una evaluación sin codificaciones CIF.");

        Estado = EstadoEvaluacion.Consolidada;
        return Result.Success();
    }
}