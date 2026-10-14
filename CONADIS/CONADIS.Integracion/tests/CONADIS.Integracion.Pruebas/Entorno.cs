using System.Transactions;
using Microsoft.Extensions.DependencyInjection;
using CONADIS.Integracion.Negocio;
using CONADIS.Integracion.Negocio.Contratos;
using CONADIS.Integracion.Negocio.Logging;
using CONADIS.Integracion.Negocio.Servicios;
using CONADIS.Integracion.Negocio.Simuladores;

namespace CONADIS.Integracion.Pruebas;

/// <summary>
/// Entorno de las pruebas de integración contra LocalDB.
/// Requisito: haber ejecutado los scripts 01..09 de CONADIS.Integracion/BaseDatos.
/// La cadena de conexión se puede cambiar con la variable de entorno CONADIS_INTEGRACION_DB.
///
/// Cada prueba trabaja dentro de una TransactionScope que nunca se confirma:
/// al terminar, todo lo que escribió (pagos, mensajes, bitácora...) se revierte.
/// </summary>
public sealed class Entorno : IDisposable
{
    public static readonly string CadenaConexion =
        Environment.GetEnvironmentVariable("CONADIS_INTEGRACION_DB")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=CONADIS_IntegracionDB;Integrated Security=true;TrustServerCertificate=true";

    /// <summary>API key de la Caja definida en 08_DatosIniciales.sql (solo desarrollo).</summary>
    public const string ApiKeyCaja = "demo-caja-conadis-2026";
    public const string ApiKeyWeb = "demo-web-conadis-2026";

    private readonly ServiceProvider _proveedor;
    private readonly TransactionScope _transaccion;
    private readonly IDisposable _correlacion;

    public Guid CorrelacionId { get; } = Guid.NewGuid();

    public Entorno()
    {
        _proveedor = new ServiceCollection().AddIntegracionNegocio(CadenaConexion).BuildServiceProvider();

        var simuladores = Obtener<EstadoSimuladores>();
        simuladores.LatenciaMinimaMs = 0;
        simuladores.LatenciaMaximaMs = 0;

        _correlacion = Correlacion.Iniciar(CorrelacionId);
        _transaccion = new TransactionScope(TransactionScopeOption.RequiresNew,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);
    }

    public T Obtener<T>() where T : notnull => _proveedor.GetRequiredService<T>();

    public EstadoSimuladores Simuladores => Obtener<EstadoSimuladores>();

    public async Task<ClienteApi> ClienteAsync(string apiKey = ApiKeyCaja)
        => await Obtener<ServicioSeguridad>().ValidarApiKeyAsync(apiKey)
           ?? throw new InvalidOperationException(
               "La API key de demostración no es válida. ¿Se ejecutó 08_DatosIniciales.sql en CONADIS_IntegracionDB?");

    /// <summary>Cambia el umbral del cortacircuito de un servicio (solo dentro de la transacción de la prueba).</summary>
    public Task UmbralCircuitoAsync(string servicio, int umbral)
        => Obtener<ServicioAdministracion>().ConfigurarServicioAsync(servicio, null, true, umbral, 60, true);

    /// <summary>Genera una cédula con dígito verificador correcto.</summary>
    public static string CedulaValida(string diezDigitos = "0011234567")
        => diezDigitos + Digitos.DigitoCedula(diezDigitos);

    public static CobroCajaDto Cobro(Guid? transaccion = null, decimal total = 500m, string tasa = "DUP-CARNET") => new(
        transaccion ?? Guid.NewGuid(), "Caja 01 - Sede Central", "cajero.prueba", "E320000000001",
        tasa, "Duplicado de carnet", 1, total, total, MediosPago.Efectivo, CedulaValida(), "Persona de Prueba",
        null, null, DateTime.UtcNow);

    public void Dispose()
    {
        _transaccion.Dispose();   // sin Complete(): todo se revierte
        _correlacion.Dispose();
        _proveedor.Dispose();
    }
}
