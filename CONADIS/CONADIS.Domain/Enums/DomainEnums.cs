using System;
using System.Collections.Generic;
using System.Text;

namespace CONADIS.Domain.Enums;

public enum EstadoExpediente
{
    Solicitado = 1,
    EnEvaluacion = 2,
    Aprobado = 3,
    Rechazado = 4,
    EnApelacion = 5,
    Carnetizado = 6,
    Vencido = 7,
    Anulado = 8
}

public enum TipoDiscapacidad
{
    Fisica = 1,
    Visual = 2,
    Auditiva = 3,
    Intelectual = 4,
    Psicosocial = 5,
    Multiple = 6
}

public enum GradoDiscapacidad
{
    Leve = 1,
    Moderado = 2,
    Severo = 3,
    Profundo = 4
}

public enum EstadoEvaluacion
{
    Borrador = 1,
    Completa = 2,
    Consolidada = 3
}

public enum EstadoCarnet
{
    Vigente = 1,
    Vencido = 2,
    Anulado = 3
}

public enum Sexo
{
    Masculino = 1,
    Femenino = 2
}