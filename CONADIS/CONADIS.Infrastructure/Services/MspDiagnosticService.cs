using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Application.Interfaces;

namespace CONADIS.Infrastructure.Services;

public class MspDiagnosticService : IMspDiagnosticService
{
    public Task<DiagnosticoMspDto> ValidarDiagnosticoAsync(string cedula, string codigoCie10, CancellationToken ct = default)
    {
        // Simulación de respuesta del Ministerio de Salud Pública
        return Task.FromResult(new DiagnosticoMspDto(
            cedula,
            codigoCie10,
            "Parálisis Cerebral Infantil / Trastorno del Desarrollo",
            true
        ));
    }
}