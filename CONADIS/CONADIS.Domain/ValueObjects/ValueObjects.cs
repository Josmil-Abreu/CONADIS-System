using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Domain.Enums;

namespace CONADIS.Domain.ValueObjects;

public record Cedula(string Valor)
{
    public static bool EsValida(string cedula)
    {
        if (string.IsNullOrWhiteSpace(cedula)) return false;
        var limpia = cedula.Replace("-", "").Trim();
        return limpia.Length == 11 && limpia.All(char.IsDigit);
    }
}

public record NombreCompleto(string Nombres, string Apellidos)
{
    public string Completo => $"{Nombres} {Apellidos}".Trim();
}

public record Contacto(string Telefono, string Email, string CanalPreferido);

public record Direccion(string Provincia, string Municipio, string Sector, string Detalle);

public record CodificacionCIF(
    string Codigo,
    string Componente,
    int Calificador1,
    int? Calificador2 = null,
    string? Observacion = null
);