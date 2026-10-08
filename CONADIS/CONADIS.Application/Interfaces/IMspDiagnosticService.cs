using System;
using System.Collections.Generic;
using System.Text;

namespace CONADIS.Application.Interfaces;

public record DiagnosticoMspDto(
    string Cedula,
    string CodigoCie10,
    string Diagnostico,
    bool EsValido
);

public interface IMspDiagnosticService
{
    Task<DiagnosticoMspDto> ValidarDiagnosticoAsync(string cedula, string codigoCie10, CancellationToken ct = default);
}