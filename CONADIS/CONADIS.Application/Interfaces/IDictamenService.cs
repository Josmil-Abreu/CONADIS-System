using CONADIS.Application.Dtos;
using CONADIS.Domain.Common;

namespace CONADIS.Application.Interfaces;

public interface IDictamenService
{
    Task<Result<string>> ProcesarDictamenAsync(EmitirDictamenDto dto, CancellationToken ct = default);
}