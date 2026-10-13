using CONADIS.Application.Dtos;
using CONADIS.Domain.Common;

namespace CONADIS.Application.Interfaces;

public interface ISolicitudExpedienteService
{
    Task<Result<ExpedienteDetalleDto>> RegistrarSolicitudAsync(RegistrarSolicitudDto dto, CancellationToken ct = default);
    Task<Result<ExpedienteDetalleDto>> ObtenerPorNumeroAsync(string numeroExpediente, CancellationToken ct = default);
}