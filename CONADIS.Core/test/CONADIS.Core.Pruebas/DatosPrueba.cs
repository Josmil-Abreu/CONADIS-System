using CONADIS.Core.Negocio.Modelos;
using CONADIS.Core.Negocio.Seguridad;
using CONADIS.Core.Negocio.Servicios;

namespace CONADIS.Core.Pruebas;

/// <summary>Ayudas para las pruebas de integración.</summary>
internal static class DatosPrueba
{
	public const string Contrasena = "Conadis2026!";
	public const string CentroSede = "Centro de Valoración Sede Central";

	/// <summary>
	/// Cédula válida (dígito verificador correcto) y distinta en cada ejecución,
	/// para que las pruebas puedan repetirse sin chocar con datos de ejecuciones anteriores.
	/// Empieza por 4 para no confundirse con cédulas reales de ejemplo.
	/// </summary>
	public static string CedulaNueva()
	{
		var diez = "4" + Random.Shared.NextInt64(0, 1_000_000_000).ToString("D9");
		return diez + ValidadorCedula.CalcularDigito(diez);
	}

	/// <summary>Inicia sesión con un usuario de demostración y devuelve su sesión.</summary>
	public static async Task<UsuarioSesion> SesionAsync(string usuario)
	{
		var r = await new ServicioAutenticacion(BaseDatosPrueba.Ejecutor()).IniciarSesionAsync(usuario, Contrasena, "127.0.0.1");
		Assert.True(r.Exito, r.Mensaje);
		return r.Usuario!;
	}

	/// <summary>Un día laborable al azar entre 3 y 60 días adelante, a las 10:00 (reparte las citas de prueba).</summary>
	public static DateTimeOffset FechaCitaLaborable()
	{
		var dia = DateTime.Today.AddDays(Random.Shared.Next(3, 60));
		while (dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) dia = dia.AddDays(1);
		return new DateTimeOffset(dia.AddHours(10));
	}
}

/// <summary>Firma simulada para las pruebas (la real la da el módulo de Integración).</summary>
internal sealed class FirmaFalsa : IFirmaDigital
{
	public Task<string> FirmarAsync(string contenido, CancellationToken ct = default)
		=> Task.FromResult("FIRMA-DE-PRUEBA-" + ApiKey.Hashear(contenido));
}

/// <summary>Simula que el servicio de firma está caído.</summary>
internal sealed class FirmaCaida : IFirmaDigital
{
	public Task<string> FirmarAsync(string contenido, CancellationToken ct = default)
		=> throw new HttpRequestException("Servicio de firma no disponible (simulado).");
}