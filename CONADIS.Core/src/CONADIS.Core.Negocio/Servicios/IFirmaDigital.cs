namespace CONADIS.Core.Negocio.Servicios;

/// <summary>
/// Puerto hacia el servicio de firma digital (RF-INT-03). El Core no firma por sí mismo:
/// le pide la firma al módulo de Integración, que habla con el proveedor acreditado (simulado).
/// La implementación real se conecta en la API (Paso 6); las pruebas usan una firma falsa.
/// </summary>
public interface IFirmaDigital
{
	/// <summary>Devuelve la firma del contenido. Lanza excepción si el servicio no está disponible.</summary>
	Task<string> FirmarAsync(string contenido, CancellationToken ct = default);
}

/// <summary>El servicio de firma no respondió: el carnet NO se emite (RF-CORE-03, E3 del CU-01).</summary>
public sealed class FirmaNoDisponibleException(string mensaje, Exception? interna = null) : Exception(mensaje, interna);