using System;
using System.Collections.Generic;
using System.Text;

namespace CONADIS.Application.Dtos
{
    public record RegistrarSolicitudDto(
       string Cedula,
       string Nombres,
       string Apellidos,
       DateOnly FechaNacimiento,
       int Sexo, // 1: Masculino, 2: Femenino
       string Telefono,
       string Email,
       string Provincia,
       string Municipio,
       string Sector,
       string DireccionDetalle
   );
}
