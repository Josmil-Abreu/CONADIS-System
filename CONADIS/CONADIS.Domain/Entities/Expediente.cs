using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Domain.Common;
using CONADIS.Domain.Enums;

namespace CONADIS.Domain.Entities;

public class Expediente
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string NumeroExpediente { get; private set; } = string.Empty; // EXP-AAAA-NNNNNNN
    public Guid PersonaId { get; private set; }
    public EstadoExpediente Estado { get; private set; } = EstadoExpediente.Solicitado;
    public GradoDiscapacidad? Grado { get; private set; }
    public TipoDiscapacidad? Tipo { get; private set; }
    public DateOnly? FechaVencimiento { get; private set; }
    public DateTime FechaSolicitud { get; private set; } = DateTime.UtcNow;

    public EvaluacionCIF? EvaluacionVigente { get; private set; }
    public Carnet? CarnetVigente { get; private set; }

    private Expediente() { }

    public static Result<Expediente> Crear(string numeroExpediente, Guid personaId)
    {
        if (string.IsNullOrWhiteSpace(numeroExpediente))
            return Result.Failure<Expediente>("El número de expediente es requerido.");

        var expediente = new Expediente
        {
            NumeroExpediente = numeroExpediente,
            PersonaId = personaId,
            Estado = EstadoExpediente.Solicitado
        };

        return Result.Success(expediente);
    }

    public Result IniciarEvaluacion()
    {
        if (Estado != EstadoExpediente.Solicitado && Estado != EstadoExpediente.EnApelacion)
            return Result.Failure("Transición inválida: El expediente no está en estado Solicitado o En Apelación.");

        Estado = EstadoExpediente.EnEvaluacion;
        return Result.Success();
    }

    public Result Aprobar(EvaluacionCIF evaluacion, GradoDiscapacidad grado, TipoDiscapacidad tipo, DateOnly? fechaVencimiento)
    {
        if (Estado != EstadoExpediente.EnEvaluacion)
            return Result.Failure("Solo se pueden aprobar expedientes que estén En Evaluación.");

        if (evaluacion.Estado != EstadoEvaluacion.Consolidada)
            return Result.Failure("La evaluación CIF debe estar consolidada antes de aprobar el dictamen.");

        EvaluacionVigente = evaluacion;
        Grado = grado;
        Tipo = tipo;
        FechaVencimiento = fechaVencimiento;
        Estado = EstadoExpediente.Aprobado;

        return Result.Success();
    }

    public Result Rechazar(string motivo)
    {
        if (Estado != EstadoExpediente.EnEvaluacion)
            return Result.Failure("Solo se pueden rechazar expedientes En Evaluación.");

        Estado = EstadoExpediente.Rechazado;
        return Result.Success();
    }

    public Result Carnetizar(Carnet carnet)
    {
        if (Estado != EstadoExpediente.Aprobado)
            return Result.Failure("No se puede emitir carnet si el expediente no está Aprobado.");

        if (CarnetVigente != null)
            CarnetVigente.Anular();

        CarnetVigente = carnet;
        Estado = EstadoExpediente.Carnetizado;
        return Result.Success();
    }

    public Result Apelar()
    {
        if (Estado != EstadoExpediente.Rechazado)
            return Result.Failure("Solo se pueden apelar expedientes en estado Rechazado.");

        Estado = EstadoExpediente.EnApelacion;
        return Result.Success();
    }

    public void MarcarVencido()
    {
        if (Estado == EstadoExpediente.Carnetizado)
        {
            Estado = EstadoExpediente.Vencido;
            CarnetVigente?.MarcarVencido();
        }
    }
}