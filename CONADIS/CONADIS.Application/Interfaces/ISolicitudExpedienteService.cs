using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Domain.Common;
using CONADIS.Application.Dtos;

namespace CONADIS.Application.Interfaces;

public interface ISolicitudExpedienteService
{
    Task<Result<ExpedienteDetalleDto>> RegistrarSolicitudAsync(RegistrarSolicitudDto dto, CancellationToken ct = default);
    Task<Result<ExpedienteDetalleDto>> ObtenerPorNumeroAsync(string numeroExpediente, CancellationToken ct = default);
}