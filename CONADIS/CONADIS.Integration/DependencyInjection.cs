namespace CONADIS.Integration;

using System;
using CONADIS.Integration.Configuration;
using CONADIS.Integration.HttpClients.Interfaces;
using CONADIS.Integration.HttpClients.Implementations;
using CONADIS.Integration.Mocks;
using CONADIS.Integration.Services.Interfaces;
using CONADIS.Integration.Services.Implementations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;

/// <summary>
/// Extensiones para registrar los servicios de integración en el contenedor de inyección de dependencias.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra todos los servicios de integración con la capa de aplicación.
    /// Configura políticas de reintentos, circuit breakers y mocks según la configuración.
    /// </summary>
    public static IServiceCollection AddIntegrationServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // Registrar opciones de configuración
        services.Configure<IntegrationSettings>(
            configuration?.GetSection("Integration") ?? new ConfigurationBuilder().Build().GetSection("Integration"));

        // Obtener configuración para determinar si usar mocks
        var settings = new IntegrationSettings();
        configuration?.GetSection("Integration").Bind(settings);

        if (settings.UseMocks)
        {
            RegisterMocks(services);
        }
        else
        {
            RegisterRealHttpClients(services);
        }

        // Registrar servicios de orquestación
        RegisterOrchestrationServices(services);

        return services;
    }

    /// <summary>Registra los clientes HTTP reales con políticas de reintentos y circuit breaker</summary>
    private static void RegisterRealHttpClients(IServiceCollection services)
    {
        // Política de reintentos con backoff exponencial
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromMilliseconds(100 * Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    // Log del reintento
                    Console.WriteLine($"Reintento {retryCount} después de {timespan.TotalMilliseconds}ms");
                });

        // Política de circuit breaker
        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, timespan) =>
                {
                    Console.WriteLine($"Circuit breaker abierto por {timespan.TotalSeconds}s");
                },
                onReset: () =>
                {
                    Console.WriteLine("Circuit breaker restablecido.");
                });

        // Combinar políticas
        var combinedPolicy = Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);

        // Registrar cliente de pagos
        services
            .AddHttpClient<IPaymentGatewayClient, PaymentGatewayClient>()
            .AddPolicyHandler(combinedPolicy);

        // Registrar cliente de firmas digitales
        services
            .AddHttpClient<IDigitalSignatureClient, DigitalSignatureClient>()
            .AddPolicyHandler(combinedPolicy);

        // Registrar cliente de JCE
        services
            .AddHttpClient<IJceApiClient, JceApiClient>()
            .AddPolicyHandler(combinedPolicy);

        // Registrar cliente de MSP
        services
            .AddHttpClient<IMspApiClient, MspApiClient>()
            .AddPolicyHandler(combinedPolicy);
    }

    /// <summary>Registra los mocks para desarrollo y pruebas</summary>
    private static void RegisterMocks(IServiceCollection services)
    {
        // Registrar mocks específicos
        services.AddScoped<IJceMockService, JceMockService>();
        services.AddScoped<IMspMockService, MspMockService>();
        services.AddScoped<IPaymentGatewayMockService, PaymentGatewayMockService>();

        // Registrar adaptadores que usan los mocks
        services.AddScoped<IPaymentGatewayClient>(provider =>
            new PaymentGatewayMockAdapter(
                provider.GetRequiredService<IPaymentGatewayMockService>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PaymentGatewayMockAdapter>>()));

        services.AddScoped<IDigitalSignatureClient>(provider =>
            new DigitalSignatureMockAdapter(
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DigitalSignatureMockAdapter>>()));

        services.AddScoped<IJceApiClient>(provider =>
            new JceApiMockAdapter(
                provider.GetRequiredService<IJceMockService>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JceApiMockAdapter>>()));

        services.AddScoped<IMspApiClient>(provider =>
            new MspApiMockAdapter(
                provider.GetRequiredService<IMspMockService>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MspApiMockAdapter>>()));
    }

    /// <summary>Registra los servicios de orquestación</summary>
    private static void RegisterOrchestrationServices(IServiceCollection services)
    {
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IDigitalSignatureService, DigitalSignatureService>();
        services.AddScoped<IIdentityValidationService, IdentityValidationService>();
        services.AddScoped<IDiagnosticService, DiagnosticService>();
    }
}