using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Domain.Entities;
using CONADIS.Domain.Enums;
using CONADIS.Domain.ValueObjects;
using Xunit;

namespace CONADIS.UnitTests.Domain;

public class ExpedienteTests
{
    [Fact]
    public void Aprobar_ExpedienteSinEvaluacionConsolidada_DebeFallar()
    {
        // Arrange
        var expediente = Expediente.Crear("EXP-2026-00001", Guid.NewGuid()).Value;

        // Transicionamos el expediente a EnEvaluacion
        expediente.IniciarEvaluacion();

        // Creamos la evaluación (su estado inicial es Borrador)
        var evaluacion = new EvaluacionCIF(expediente.Id, Guid.NewGuid());

        // Act
        var resultado = expediente.Aprobar(
            evaluacion,
            GradoDiscapacidad.Moderado,
            TipoDiscapacidad.Fisica,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5))
        );

        // Assert
        Assert.True(resultado.IsFailure);
        Assert.Contains("evaluación CIF debe estar consolidada", resultado.Error);
    }
    

    [Fact]
    public void Carnetizar_ExpedienteEnEstadoSolicitado_DebeFallarPorTransicionInvalida()
    {
        // Arrange
        var expediente = Expediente.Crear("EXP-2026-00002", Guid.NewGuid()).Value;
        var carnet = new Carnet(expediente.Id, "CARNET-001", DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5)), "TOKEN_TEST");

        // Act
        var resultado = expediente.Carnetizar(carnet);

        // Assert
        Assert.True(resultado.IsFailure);
        Assert.Equal(EstadoExpediente.Solicitado, expediente.Estado);
    }
}