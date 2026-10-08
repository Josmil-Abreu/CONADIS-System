using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Application.Dtos;
using CONADIS.Application.Interfaces;
using CONADIS.Application.Services;
using CONADIS.Domain.Entities;
using Moq;
using Xunit;

namespace CONADIS.UnitTests.Application;

public class SolicitudExpedienteServiceTests
{
    private readonly Mock<IExpedienteRepository> _expedienteRepoMock;
    private readonly Mock<IJceIdentityService> _jceServiceMock;
    private readonly SolicitudExpedienteService _service;

    public SolicitudExpedienteServiceTests()
    {
        _expedienteRepoMock = new Mock<IExpedienteRepository>();
        _jceServiceMock = new Mock<IJceIdentityService>();
        _service = new SolicitudExpedienteService(_expedienteRepoMock.Object, _jceServiceMock.Object);
    }

    [Fact]
    public async Task RegistrarSolicitudAsync_ConCedulaValidaJce_DebeRetornarExito()
    {
        // Arrange
        var dto = new RegistrarSolicitudDto(
            "40200000000",
            "Juan",
            "Pérez",
            new DateOnly(1990, 5, 15),
            1,
            "8095550101",
            "juan@email.com",
            "Santo Domingo",
            "Santo Domingo Este",
            "Alma Rosa",
            "Calle Principal #12"
        );

        _jceServiceMock
            .Setup(j => j.ValidarCedulaAsync(dto.Cedula, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CiudadanoJceDto(dto.Cedula, "Juan", "Pérez", dto.FechaNacimiento, true));

        // Act
        var resultado = await _service.RegistrarSolicitudAsync(dto);

        // Assert
        Assert.True(resultado.IsSuccess);
        Assert.NotNull(resultado.Value);
        Assert.Equal("40200000000", resultado.Value.Cedula);

        // Verificar que el repositorio recibió la orden de guardar 1 vez
        _expedienteRepoMock.Verify(r => r.AgregarAsync(It.IsAny<Expediente>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegistrarSolicitudAsync_ConCedulaInvalidaJce_DebeRetornarFallo()
    {
        // Arrange
        var dto = new RegistrarSolicitudDto(
            "00000000000",
            "Desconocido",
            "Desconocido",
            new DateOnly(2000, 1, 1),
            1,
            "8090000000",
            "test@email.com",
            "Santo Domingo",
            "Distrito Nacional",
            "Centro",
            "Calle Falsa 123"
        );

        _jceServiceMock
            .Setup(j => j.ValidarCedulaAsync(dto.Cedula, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CiudadanoJceDto(dto.Cedula, "", "", dto.FechaNacimiento, false));

        // Act
        var resultado = await _service.RegistrarSolicitudAsync(dto);

        // Assert
        Assert.True(resultado.IsFailure);
        Assert.Contains("no pudo ser validada con la JCE", resultado.Error);

        // Verificar que NUNCA se intentó guardar en la base de datos
        _expedienteRepoMock.Verify(r => r.AgregarAsync(It.IsAny<Expediente>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}