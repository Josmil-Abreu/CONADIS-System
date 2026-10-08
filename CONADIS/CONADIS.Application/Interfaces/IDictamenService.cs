using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Domain.Common;
using CONADIS.Application.Dtos;

namespace CONADIS.Application.Interfaces;

public interface IDictamenService
{
    Task<Result<string>> ProcesarDictamenAsync(EmitirDictamenDto dto, CancellationToken ct = default);
}