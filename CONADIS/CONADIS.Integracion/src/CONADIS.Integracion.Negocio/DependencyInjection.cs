using Microsoft.Extensions.DependencyInjection;
using CONADIS.Integracion.Negocio.Conectores;
using CONADIS.Integracion.Negocio.Datos;
using CONADIS.Integracion.Negocio.Servicios;
using CONADIS.Integracion.Negocio.Simuladores;

namespace CONADIS.Integracion.Negocio;

public static class DependencyInjection
{
    /// <summary>
    /// Registra toda la lógica de Integración. Uso en Program.cs de la API o del Worker:
    ///   builder.Services.AddIntegracionNegocio(builder.Configuration.GetConnectionString("IntegracionDb"));
    /// Todos los servicios son sin estado, así que se registran como singleton.
    /// </summary>
    public static IServiceCollection AddIntegracionNegocio(this IServiceCollection services, string? cadenaConexion)
    {
        services.AddSingleton(new EjecutorSp(cadenaConexion));

        // Simuladores de los servicios externos. Para conectar uno real, registrar aquí
        // otra implementación de su interfaz (por ejemplo, un cliente HTTP).
        services.AddSingleton<EstadoSimuladores>();
        services.AddSingleton<IConectorJce, SimuladorJce>();
        services.AddSingleton<IConectorMsp, SimuladorMsp>();
        services.AddSingleton<IConectorDgii, SimuladorDgii>();
        services.AddSingleton<IConectorFirma, SimuladorFirma>();
        services.AddSingleton<IConectorPasarela, SimuladorPasarela>();
        services.AddSingleton<IConectorCore, SimuladorCore>();

        services.AddSingleton<ServicioBitacora>();
        services.AddSingleton<ServicioResiliencia>();
        services.AddSingleton<ServicioCache>();
        services.AddSingleton<ServicioCola>();
        services.AddSingleton<ServicioIdempotencia>();
        services.AddSingleton<ServicioSeguridad>();
        services.AddSingleton<ServicioIdentidad>();
        services.AddSingleton<ServicioDiagnostico>();
        services.AddSingleton<ServicioFirma>();
        services.AddSingleton<ServicioPagos>();
        services.AddSingleton<ServicioFiscal>();
        services.AddSingleton<ServicioEventos>();
        services.AddSingleton<ServicioAdministracion>();
        services.AddSingleton<ServicioReportes>();
        services.AddSingleton<ServicioSalud>();
        services.AddSingleton<ProcesadorCola>();

        return services;
    }
}
