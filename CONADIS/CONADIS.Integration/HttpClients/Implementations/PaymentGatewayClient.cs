namespace CONADIS.Integration.HttpClients.Implementations;

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CONADIS.Integration.Configuration;
using CONADIS.Integration.Exceptions;
using CONADIS.Integration.HttpClients.Interfaces;
using CONADIS.Integration.Models.Requests;
using CONADIS.Integration.Models.Responses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Implementación real del cliente HTTP para la pasarela de pago</summary>
public class PaymentGatewayClient : IPaymentGatewayClient
{
    private readonly HttpClient _httpClient;
    private readonly PaymentGatewaySettings _settings;
    private readonly ILogger<PaymentGatewayClient> _logger;

    public PaymentGatewayClient(
        HttpClient httpClient,
        IOptions<IntegrationSettings> options,
        ILogger<PaymentGatewayClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value.PaymentGateway;
        _logger = logger;

        // Configurar cliente base
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);
    }

    public async Task<PaymentResponse> InitiatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Iniciando pago: TransactionId={TransactionId}, Amount={Amount}",
                request.TransactionId, request.Amount);

            var endpoint = ApiEndpoints.PaymentGateway.InitiatePayment;
            var response = await _httpClient.PostAsJsonAsync(endpoint, request, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                throw new PaymentException(
                    $"Error al iniciar pago: {response.StatusCode}",
                    request.TransactionId);
            }

            var result = await response.Content.ReadFromJsonAsync<PaymentResponse>(cancellationToken: ct)
                ?? throw new PaymentException("Respuesta vacía del servidor", request.TransactionId);

            _logger.LogInformation("Pago iniciado exitosamente: TransactionId={TransactionId}, Status={Status}",
                result.TransactionId, result.Status);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de conexión al iniciar pago");
            throw new PaymentException("Error de conexión con la pasarela de pago", request.TransactionId, ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout al iniciar pago");
            throw new PaymentException("Timeout en la pasarela de pago", request.TransactionId, ex);
        }
    }

    public async Task<PaymentStatusResponse> GetPaymentStatusAsync(string transactionId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Consultando estado de pago: TransactionId={TransactionId}", transactionId);

            var endpoint = ApiEndpoints.PaymentGateway.GetPaymentStatus.Replace("{transactionId}", transactionId);
            var response = await _httpClient.GetAsync(endpoint, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new PaymentException($"Error al consultar estado: {response.StatusCode}", transactionId);
            }

            var result = await response.Content.ReadFromJsonAsync<PaymentStatusResponse>(cancellationToken: ct)
                ?? throw new PaymentException("Respuesta vacía del servidor", transactionId);

            _logger.LogInformation("Estado de pago obtenido: TransactionId={TransactionId}, Status={Status}",
                result.TransactionId, result.Status);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de conexión al consultar estado");
            throw new PaymentException("Error de conexión con la pasarela de pago", transactionId, ex);
        }
    }

    public async Task<PaymentResponse> RefundAsync(string transactionId, decimal amount, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Procesando reembolso: TransactionId={TransactionId}, Amount={Amount}",
                transactionId, amount);

            var endpoint = ApiEndpoints.PaymentGateway.RefundPayment.Replace("{transactionId}", transactionId);
            var refundRequest = new { amount };
            var response = await _httpClient.PostAsJsonAsync(endpoint, refundRequest, cancellationToken: ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new PaymentException($"Error al procesar reembolso: {response.StatusCode}", transactionId);
            }

            var result = await response.Content.ReadFromJsonAsync<PaymentResponse>(cancellationToken: ct)
                ?? throw new PaymentException("Respuesta vacía del servidor", transactionId);

            _logger.LogInformation("Reembolso procesado: TransactionId={TransactionId}, Status={Status}",
                result.TransactionId, result.Status);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de conexión al reembolsar");
            throw new PaymentException("Error de conexión con la pasarela de pago", transactionId, ex);
        }
    }
}