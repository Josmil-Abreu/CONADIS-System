using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Domain.Enums;

namespace CONADIS.Domain.Entities;

public class Carnet
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ExpedienteId { get; private set; }
    public string NumeroCarnet { get; private set; } = string.Empty;
    public DateOnly FechaEmision { get; private set; }
    public DateOnly FechaVencimiento { get; private set; }
    public string TokenVerificacion { get; private set; } = string.Empty; // Token JWS para el código QR
    public EstadoCarnet Estado { get; private set; } = EstadoCarnet.Vigente;

    private Carnet() { }

    public Carnet(Guid expedienteId, string numeroCarnet, DateOnly fechaEmision, DateOnly fechaVencimiento, string tokenVerificacion)
    {
        ExpedienteId = expedienteId;
        NumeroCarnet = numeroCarnet;
        FechaEmision = fechaEmision;
        FechaVencimiento = fechaVencimiento;
        TokenVerificacion = tokenVerificacion;
        Estado = EstadoCarnet.Vigente;
    }

    public void Anular() => Estado = EstadoCarnet.Anulado;
    public void MarcarVencido() => Estado = EstadoCarnet.Vencido;
}