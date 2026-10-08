using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Application.Interfaces;

namespace CONADIS.Infrastructure.Services;

public class JceIdentityService : IJceIdentityService
{
    public Task<CiudadanoJceDto> ValidarCedulaAsync(string cedula, CancellationToken ct = default)
    {
        // Simulación: Si la cédula tiene 11 dígitos y no empieza por 000, es válida
        var esValida = !string.IsNullOrWhiteSpace(cedula) && cedula.Length == 11 && !cedula.StartsWith("000");

        if (!esValida)
        {
            return Task.FromResult(new CiudadanoJceDto(cedula, "", "", new DateOnly(2000, 1, 1), false));
        }

        // Datos simulados devueltos por el padrón de la JCE
        return Task.FromResult(new CiudadanoJceDto(
            cedula,
            "Juan Alberto",
            "Pérez Gómez",
            new DateOnly(1988, 8, 24),
            true
        ));
    }
}