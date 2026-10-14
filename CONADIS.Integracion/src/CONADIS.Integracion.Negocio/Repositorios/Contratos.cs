using CONADIS.Integracion.Negocio.Modelos;

namespace CONADIS.Integracion.Negocio.Repositorios;

// Interfaces de los repositorios que usa la resiliencia. Existen para que las pruebas unitarias
// puedan reemplazar la base de datos por versiones en memoria.

public interface IRepositorioBitacora
{
    Task RegistrarAsync(EntradaBitacora entrada, CancellationToken ct = default);
}

public interface IRepositorioCircuito
{
    Task<EstadoCircuito> PuedeLlamarAsync(string servicio, CancellationToken ct = default);
    Task RegistrarResultadoAsync(string servicio, bool exito, string? error = null, CancellationToken ct = default);
}

public interface IRepositorioCache
{
    Task<EntradaCache?> ObtenerAsync(string servicio, string clave, CancellationToken ct = default);
    Task GuardarAsync(string servicio, string clave, string resultado, string? datos, int? dias = null, CancellationToken ct = default);
}

public interface IParametros
{
    Task<int> EnteroAsync(string clave, int porDefecto, CancellationToken ct = default);
    Task<string?> TextoAsync(string clave, CancellationToken ct = default);
}

public interface IRepositorioAlertas
{
    Task RegistrarAsync(string severidad, string servicio, string mensaje, CancellationToken ct = default);
}
