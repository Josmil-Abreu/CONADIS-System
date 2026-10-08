using System;
using System.Collections.Generic;
using System.Text;

namespace CONADIS.Application.Dtos
{
    public record ExpedienteDetalleDto(
       Guid ExpedienteId,
       string NumeroExpediente,
       string Cedula,
       string NombreCompleto,
       string Estado,
       DateTime FechaSolicitud,
       string? GradoDiscapacidad,
       string? TipoDiscapacidad,
       string? NumeroCarnet
   );
}
