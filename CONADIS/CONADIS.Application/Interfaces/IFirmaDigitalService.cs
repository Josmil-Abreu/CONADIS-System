namespace CONADIS.Application.Interfaces;

public interface IFirmaDigitalService
{
    Task<string> GenerarTokenQrSimuladoAsync(string numeroCarnet, string cedula, DateOnly fechaVencimiento, CancellationToken ct = default);
}