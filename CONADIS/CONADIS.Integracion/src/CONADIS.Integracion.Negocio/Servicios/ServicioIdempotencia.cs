using System.Data;
using CONADIS.Integracion.Negocio.Contratos;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Seguridad;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>
/// Encabezado Idempotency-Key: si un cliente repite una petición (por ejemplo, porque
/// perdió la conexión antes de ver la respuesta), recibe la misma respuesta y la
/// operación no se ejecuta dos veces. La API lo usa así:
///   var r = await IniciarAsync(...);
///   Nuevo     -> procesar; CompletarAsync con la respuesta (o LiberarAsync si hubo error técnico)
///   Repetido  -> devolver r.CodigoHttp y r.RespuestaJson tal cual
///   EnProceso -> 409 (la primera petición aún no termina)
///   Conflicto -> 422 (misma clave con otro cuerpo)
/// </summary>
public sealed class ServicioIdempotencia(EjecutorSp sp)
{
    public async Task<ResultadoIdempotencia> IniciarAsync(int clienteId, string clave, string? cuerpo, CancellationToken ct = default)
    {
        Validaciones.Requerido(clave, "Idempotency-Key", 100);

        var resultado = Parametro.Salida("@Resultado", SqlDbType.NVarChar, 15);
        var codigo = Parametro.Salida("@CodigoHttp", SqlDbType.Int);
        var respuesta = Parametro.Salida("@RespuestaJson", SqlDbType.NVarChar, -1);

        await sp.EjecutarAsync("snd.ppIdempotencia_Iniciar", ct,
            Parametro.Entero("@ClienteId", clienteId),
            Parametro.Texto("@Clave", clave.Trim(), 100),
            Parametro.Hash("@HashSolicitud", Huella.Calcular(cuerpo)),
            resultado, codigo, respuesta);

        return new ResultadoIdempotencia(resultado.Texto() ?? ResultadosIdempotencia.Nuevo, codigo.Entero(), respuesta.Texto());
    }

    public Task CompletarAsync(int clienteId, string clave, int codigoHttp, string? respuestaJson, CancellationToken ct = default)
        => sp.EjecutarAsync("snd.ppIdempotencia_Completar", ct,
            Parametro.Entero("@ClienteId", clienteId),
            Parametro.Texto("@Clave", clave.Trim(), 100),
            Parametro.Entero("@CodigoHttp", codigoHttp),
            Parametro.Texto("@RespuestaJson", respuestaJson));

    public Task LiberarAsync(int clienteId, string clave, CancellationToken ct = default)
        => sp.EjecutarAsync("snd.ppIdempotencia_Liberar", ct,
            Parametro.Entero("@ClienteId", clienteId),
            Parametro.Texto("@Clave", clave.Trim(), 100));
}
