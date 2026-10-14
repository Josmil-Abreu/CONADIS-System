using System.Data;
using log4net;
using CONADIS.Integracion.Negocio.Contratos;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Logging;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Registra cada llamada que entra a Integración o sale hacia otro servicio (snd.Bitacora).
/// Registrar nunca hace fallar la operación principal: si la bitácora no se puede escribir,
/// se deja constancia en Log4Net y se sigue.
/// </summary>
public sealed class ServicioBitacora(EjecutorSp sp)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioBitacora));

    public async Task RegistrarAsync(
        string direccion, string operacion, bool exitoso, int duracionMs,
        int? codigoResultado = null, int? clienteId = null, string? servicio = null,
        string? detalle = null, CancellationToken ct = default)
    {
        try
        {
            await sp.EjecutarAsync("snd.ppBitacora_Registrar", ct,
                Parametro.Guid("@CorrelacionId", Correlacion.Id),
                Parametro.Entero("@ClienteId", clienteId),
                Parametro.Texto("@ServicioCodigo", servicio, 20),
                Parametro.Texto("@Direccion", direccion, 10),
                Parametro.Texto("@Operacion", Recortar(operacion, 100), 100),
                Parametro.Entero("@CodigoResultado", codigoResultado),
                Parametro.Booleano("@Exitoso", exitoso),
                Parametro.Entero("@DuracionMs", duracionMs),
                Parametro.Texto("@Detalle", detalle));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"No se pudo registrar en la bitácora: {direccion} {operacion}.", ex);
        }
    }

    public Task<List<BitacoraDto>> ListarAsync(DateTime desde, DateTime hasta, string? servicio = null,
        bool soloErrores = false, Guid? correlacionId = null, int top = 500, CancellationToken ct = default)
    {
        Validaciones.Rango(desde, hasta);
        return sp.ConsultarListaAsync("snd.ppBitacora_Listar", Mapear, ct,
            Parametro.Fecha("@Desde", desde),
            Parametro.Fecha("@Hasta", hasta),
            Parametro.Texto("@ServicioCodigo", servicio, 20),
            Parametro.Booleano("@SoloErrores", soloErrores),
            Parametro.Guid("@CorrelacionId", correlacionId),
            Parametro.Entero("@Top", top));
    }

    private static BitacoraDto Mapear(DataRow f) => new(
        f.EnteroLargo("BitacoraId"), f.Fecha("Fecha"), f.GuidONulo("CorrelacionId"),
        f.TextoONulo("ClienteCodigo"), f.TextoONulo("ServicioCodigo"), f.Texto("Direccion"),
        f.Texto("Operacion"), f.EnteroONulo("CodigoResultado"), f.Booleano("Exitoso"),
        f.Entero("DuracionMs"), f.TextoONulo("Detalle"));

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
