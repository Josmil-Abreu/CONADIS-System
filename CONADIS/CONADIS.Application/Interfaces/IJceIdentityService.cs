using System;
using System.Collections.Generic;
using System.Text;

namespace CONADIS.Application.Interfaces
{
    public record CiudadanoJceDto(
      string Cedula,
      string Nombres,
      string Apellidos,
      DateOnly FechaNacimiento,
      bool EsValida
  );

    public interface IJceIdentityService
    {
        Task<CiudadanoJceDto> ValidarCedulaAsync(string cedula, CancellationToken ct = default);
    }
}
