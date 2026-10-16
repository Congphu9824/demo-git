using System.Diagnostics;
using ECommerce.Application.Diagnostics;
using ECommerce.Application.Exceptions;
using ECommerce.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.ExternalServices;

public class SimulatedPaymentGateway : IExternalPaymentGateway
{
    private readonly IDemoIncidentService _demoIncidentService;
    private readonly ILogger<SimulatedPaymentGateway> _logger;

    public SimulatedPaymentGateway(
        IDemoIncidentService demoIncidentService,
        ILogger<SimulatedPaymentGateway> logger)
    {
        _demoIncidentService = demoIncidentService;
        _logger = logger;
    }

    public async Task<(bool Success, string TransactionId, string Message)> ExecuteTransactionAsync(
        Guid orderId,
        decimal amount,
        string paymentMethod,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = ObservabilityActivitySource.StartActivity("ExternalPaymentAPI");
        activity?.SetTag("external.service", "StripePaymentGateway");
        activity?.SetTag("http.method", "POST");
        activity?.SetTag("http.url", "https://api.stripe.com/v1/payment_intents");
        activity?.SetTag("order.id", orderId.ToString());
        activity?.SetTag("payment.amount", amount);
        activity?.SetTag("payment.method", paymentMethod);

        ObservabilityMetrics.ExternalApiRequestsTotal.Add(1, new KeyValuePair<string, object?>("service", "StripeGateway"));

        // Incident Simulation: External API failure / 500 / timeout
        if (_demoIncidentService.ExternalApiErrorEnabled)
        {
            activity?.SetTag("demo.incident", "ExternalApiError");
            ObservabilityMetrics.DemoIncidentTotal.Add(1, new KeyValuePair<string, object?>("incident_type", "ExternalApiError"));

            // Simulate delayed failure / timeout
            await Task.Delay(Random.Shared.Next(400, 800), cancellationToken);

            stopwatch.Stop();
            var durationMs = stopwatch.Elapsed.TotalMilliseconds;
            ObservabilityMetrics.ExternalApiDuration.Record(durationMs, new KeyValuePair<string, object?>("service", "StripeGateway"), new KeyValuePair<string, object?>("status", "500"));
            ObservabilityMetrics.ExternalApiErrorsTotal.Add(1, new KeyValuePair<string, object?>("service", "StripeGateway"), new KeyValuePair<string, object?>("error_code", "500_INTERNAL_GATEWAY_ERROR"));

            activity?.SetTag("http.status_code", 500);
            var apiEx = new ExternalServiceException("StripePaymentGateway", "HTTP 500 Internal Server Error: Gateway connection timeout or downstream processor unavailable.");
            activity?.RecordException(apiEx);

            _logger.LogError(apiEx, "ExternalApiFailed: Payment gateway request failed for Order {OrderId} after {DurationMs:F2}ms. Status: HTTP 500 Internal Server Error",
                orderId, durationMs);

            return (false, string.Empty, "External payment gateway returned HTTP 500: Processing error.");
        }

        // Realistic normal gateway latency (100 - 300ms)
        var normalDelay = Random.Shared.Next(100, 300);
        await Task.Delay(normalDelay, cancellationToken);

        stopwatch.Stop();
        var elapsed = stopwatch.Elapsed.TotalMilliseconds;
        ObservabilityMetrics.ExternalApiDuration.Record(elapsed, new KeyValuePair<string, object?>("service", "StripeGateway"), new KeyValuePair<string, object?>("status", "200"));

        activity?.SetTag("http.status_code", 200);

        var transactionId = $"ch_{Guid.NewGuid():N}";
        _logger.LogInformation("ExternalApiSuccess: External Stripe gateway approved transaction {TransactionId} for Order {OrderId} in {ElapsedMs:F2}ms",
            transactionId, orderId, elapsed);

        return (true, transactionId, "Approved");
    }
}
