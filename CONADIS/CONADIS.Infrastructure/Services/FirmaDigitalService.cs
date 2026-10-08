using System;
using System.Collections.Generic;
using System.Text;
using CONADIS.Application.Interfaces;

namespace CONADIS.Infrastructure.Services;

public class FirmaDigitalService : IFirmaDigitalService
{
    public Task<string> GenerarTokenQrSimuladoAsync(string numeroCarnet, string cedula, DateOnly fechaVencimiento, CancellationToken ct = default)
    {
        // Simula la creación de un token JWS/QR firmado digitalmente
        var payload = $"{numeroCarnet}|{cedula}|{fechaVencimiento:yyyy-MM-dd}|CONADIS-FIRMA-OFICIAL";
        var base64Token = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        return Task.FromResult($"https://verificar.conadis.gob.do/verify?t={base64Token}");
    }
}