using log4net;
using CONADIS.Integracion.Negocio.Contratos;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Seguridad;

namespace CONADIS.Integracion.Negocio.Servicios;

/// <summary>Autenticación de los módulos cliente por su API key.</summary>
public sealed class ServicioSeguridad(EjecutorSp sp)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ServicioSeguridad));

    /// <summary>Devuelve el cliente si la clave es válida y está activo; null en cualquier otro caso.</summary>
    public async Task<ClienteApi?> ValidarApiKeyAsync(string? apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 200) return null;

        var cliente = await sp.ConsultarUnoAsync("seg.ppCliente_ValidarApiKey",
            f => new ClienteApi(f.Entero("ClienteId"), f.Texto("Codigo"), f.Texto("Nombre")), ct,
            Parametro.Hash("@ApiKeyHash", ApiKey.Hashear(apiKey.Trim())));

        if (cliente is null) Log.Warn("Intento de acceso con una API key inválida o de un cliente inactivo.");
        return cliente;
    }
}

/// <summary>Estado de salud de Integración: base de datos y cortacircuito de cada servicio.</summary>
public sealed class ServicioSalud(EjecutorSp sp, ServicioAdministracion administracion)
{
    public async Task<SaludDto> ObtenerAsync(CancellationToken ct = default)
    {
        var baseDisponible = await sp.ProbarConexionAsync(ct);
        IReadOnlyList<ServicioDto> servicios = baseDisponible ? await administracion.ListarServiciosAsync(ct) : [];
        return new SaludDto("Integracion", baseDisponible, servicios, DateTime.UtcNow);
    }
}
