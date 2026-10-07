using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Domain.Common;
using CONADIS.Domain.Enums;
using CONADIS.Domain.ValueObjects;

namespace CONADIS.Domain.Entities;

public class PersonaConDiscapacidad
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Cedula? Cedula { get; private set; }
    public NombreCompleto Nombre { get; private set; } = default!;
    public DateOnly FechaNacimiento { get; private set; }
    public Sexo Sexo { get; private set; }
    public Contacto Contacto { get; private set; } = default!;
    public Direccion Direccion { get; private set; } = default!;
    public bool IdentidadValidadaJce { get; private set; }
    public DateTimeOffset? FechaValidacionJce { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private PersonaConDiscapacidad() { } // EF Core

    public static Result<PersonaConDiscapacidad> Crear(
        Cedula? cedula,
        NombreCompleto nombre,
        DateOnly fechaNacimiento,
        Sexo sexo,
        Contacto contacto,
        Direccion direccion)
    {
        if (cedula != null && !Cedula.EsValida(cedula.Valor))
            return Result.Failure<PersonaConDiscapacidad>("Formato de cédula inválido.");

        var persona = new PersonaConDiscapacidad
        {
            Cedula = cedula,
            Nombre = nombre,
            FechaNacimiento = fechaNacimiento,
            Sexo = sexo,
            Contacto = contacto,
            Direccion = direccion,
            IdentidadValidadaJce = false
        };

        return Result.Success(persona);
    }

    public void RegistrarValidacionJce(bool esValida)
    {
        IdentidadValidadaJce = esValida;
        FechaValidacionJce = DateTimeOffset.UtcNow;
    }

    public void ActualizarContacto(Contacto nuevoContacto)
    {
        Contacto = nuevoContacto;
    }

    public bool EsMenorDeEdad(DateOnly fechaReferencia)
    {
        var edad = fechaReferencia.Year - FechaNacimiento.Year;
        if (FechaNacimiento > fechaReferencia.AddYears(-edad)) edad--;
        return edad < 18;
    }
}