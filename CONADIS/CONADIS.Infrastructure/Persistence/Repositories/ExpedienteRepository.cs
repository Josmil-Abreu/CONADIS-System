using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Application.Interfaces;
using CONADIS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CONADIS.Infrastructure.Persistence.Repositories;

public class ExpedienteRepository : IExpedienteRepository
{
    private readonly ConadisDbContext _context;

    public ExpedienteRepository(ConadisDbContext context)
    {
        _context = context;
    }

    public async Task<Expediente?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Expedientes
            .Include(e => e.EvaluacionVigente)
            .Include(e => e.CarnetVigente)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<Expediente?> ObtenerPorNumeroAsync(string numeroExpediente, CancellationToken ct = default)
    {
        return await _context.Expedientes
            .Include(e => e.EvaluacionVigente)
            .Include(e => e.CarnetVigente)
            .FirstOrDefaultAsync(e => e.NumeroExpediente == numeroExpediente, ct);
    }

    public async Task AgregarAsync(Expediente expediente, CancellationToken ct = default)
    {
        await _context.Expedientes.AddAsync(expediente, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task ActualizarAsync(Expediente expediente, CancellationToken ct = default)
    {
        _context.Expedientes.Update(expediente);
        await _context.SaveChangesAsync(ct);
    }
}