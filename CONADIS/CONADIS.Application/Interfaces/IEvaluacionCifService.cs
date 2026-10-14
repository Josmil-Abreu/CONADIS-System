using CONADIS.Application.Dtos;
using CONADIS.Domain.Common;

namespace CONADIS.Application.Interfaces;

public interface IEvaluacionCifService
{
    Task<Result> RegistrarYConsolidarAsync(RegistrarEvaluacionDto dto, CancellationToken ct = default);
}