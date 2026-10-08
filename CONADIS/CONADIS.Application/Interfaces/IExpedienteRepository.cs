using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Domain.Entities;

namespace CONADIS.Application.Interfaces;

public interface IExpedienteRepository
{
    Task<Expediente?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
    Task<Expediente?> ObtenerPorNumeroAsync(string numeroExpediente, CancellationToken ct = default);
    Task AgregarAsync(Expediente expediente, CancellationToken ct = default);
    Task ActualizarAsync(Expediente expediente, CancellationToken ct = default);
}