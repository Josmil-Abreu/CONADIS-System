using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Domain.Common;
using CONADIS.Application.Dtos;

namespace CONADIS.Application.Interfaces;

public interface IEvaluacionCifService
{
    Task<Result> RegistrarYConsolidarAsync(RegistrarEvaluacionDto dto, CancellationToken ct = default);
}