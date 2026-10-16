using System.Diagnostics;
using ECommerce.Application.Diagnostics;
using ECommerce.Application.Exceptions;
using ECommerce.Application.Interfaces;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ECommerce.Application.Services;

public class PaymentService : IPaymentService
{
    private readonly IExternalPaymentGateway _paymentGateway;
    private readonly IDemoIncidentService _demoIncidentService;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IExternalPaymentGateway paymentGateway,
        IDemoIncidentService demoIncidentService,
        ILogger<PaymentService> logger)
    {
        _paymentGateway = paymentGateway;
        _demoIncidentService = demoIncidentService;
        _logger = logger;
    }

    public async Task<Payment> ProcessPaymentAsync(
        Guid orderId,
        decimal amount,
        string paymentMethod,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = ObservabilityActivitySource.StartActivity("ProcessPayment");
        activity?.SetTag("order.id", orderId.ToString());
        activity?.SetTag("payment.provider", "SimulatedStripeGateway");
        activity?.SetTag("payment.method", paymentMethod);
        activity?.SetTag("payment.amount", amount);

        ObservabilityMetrics.PaymentsTotal.Add(1, new KeyValuePair<string, object?>("payment_method", paymentMethod));

        _logger.LogInformation("PaymentStarted: Initiating payment for Order {OrderId} of amount {Amount:C} via {PaymentMethod}",
            orderId, amount, paymentMethod);

        // Incident Simulation: Slow Payment
        if (_demoIncidentService.SlowPaymentEnabled)
        {
            activity?.SetTag("demo.incident", "SlowPayment");
            activity?.SetTag("bottleneck.component", "Payment Gateway / Stripe");
            activity?.SetTag("bottleneck.operation", "Stripe Charge API");
            activity?.SetTag("bottleneck.duration_ms", 5000);
            activity?.SetTag("slow.reason", "Payment gateway response delay (Stripe timeout simulation: 5000ms)");
            activity?.SetTag("performance.status", "Bottleneck");

            // Propagate bottleneck information up to the root trace activity
            Activity.Current?.Parent?.SetTag("bottleneck.component", "Payment Gateway / Stripe");
            Activity.Current?.Parent?.SetTag("slow.reason", "Payment gateway response delayed by 5000ms");
            Activity.Current?.Parent?.SetTag("is_slow", true);

            ObservabilityMetrics.DemoIncidentTotal.Add(1, new KeyValuePair<string, object?>("incident_type", "SlowPayment"));
            
            _logger.LogWarning("PaymentSlow: Bottleneck detected at {Component}! Payment gateway took {DelayMs}ms for Order {OrderId}. Root Cause: {Reason}. TraceId: {TraceId}",
                "Payment Gateway / Stripe", 5000, orderId, "Stripe API latency simulation (5000ms)", Activity.Current?.TraceId);

            await Task.Delay(5000, cancellationToken);
        }
        else
        {
            // Normal realistic processing latency (100 - 200ms)
            var normalDelay = Random.Shared.Next(100, 200);
            await Task.Delay(normalDelay, cancellationToken);
        }

        // Incident Simulation: Controlled Payment Error
        if (_demoIncidentService.PaymentErrorEnabled)
        {
            stopwatch.Stop();
            var durationMs = stopwatch.Elapsed.TotalMilliseconds;
            ObservabilityMetrics.PaymentDuration.Record(durationMs, new KeyValuePair<string, object?>("status", "Failed"));
            ObservabilityMetrics.PaymentsFailedTotal.Add(1, new KeyValuePair<string, object?>("payment_method", paymentMethod));
            ObservabilityMetrics.DemoIncidentTotal.Add(1, new KeyValuePair<string, object?>("incident_type", "PaymentError"));

            var txId = $"tx_err_{Guid.NewGuid():N}";
            var errorEx = new PaymentProcessingException($"Payment gateway declined transaction {txId} for Order {orderId} due to payment incident simulation.", txId);
            
            activity?.SetTag("error.component", "Payment Gateway / Stripe");
            activity?.SetTag("error.reason", "Payment declined: simulated fraud check or card declined by gateway");
            activity?.RecordException(errorEx);

            Activity.Current?.Parent?.SetTag("error.component", "Payment Gateway / Stripe");
            Activity.Current?.Parent?.SetTag("error.reason", "Payment declined by gateway");

            _logger.LogError(errorEx, "PaymentFailed: Controlled failure injected at {Component}! Root Cause: {Reason}. Order {OrderId}, Transaction {TransactionId}. TraceId: {TraceId}",
                "Payment Gateway / Stripe", "Payment declined by simulated Stripe gateway", orderId, txId, Activity.Current?.TraceId);

            throw errorEx;
        }

        // Execute external payment gateway integration
        var (gatewaySuccess, transactionId, gatewayMsg) = await _paymentGateway.ExecuteTransactionAsync(orderId, amount, paymentMethod, cancellationToken);

        stopwatch.Stop();
        var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
        ObservabilityMetrics.PaymentDuration.Record(elapsedMs, new KeyValuePair<string, object?>("status", gatewaySuccess ? "Succeeded" : "Failed"));

        if (!gatewaySuccess)
        {
            ObservabilityMetrics.PaymentsFailedTotal.Add(1, new KeyValuePair<string, object?>("payment_method", paymentMethod));
            var paymentEx = new PaymentProcessingException($"Payment gateway rejected transaction {transactionId}: {gatewayMsg}", transactionId);
            activity?.RecordException(paymentEx);
            _logger.LogError("PaymentFailed: Payment rejected for Order {OrderId}, Transaction {TransactionId}. Reason: {Reason}",
                orderId, transactionId, gatewayMsg);
            throw paymentEx;
        }

        ObservabilityMetrics.PaymentsSuccessTotal.Add(1, new KeyValuePair<string, object?>("payment_method", paymentMethod));
        _logger.LogInformation("PaymentCompleted: Successfully processed payment {TransactionId} for Order {OrderId} in {ElapsedMs:F2}ms",
            transactionId, orderId, elapsedMs);

        return new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Amount = amount,
            PaymentMethod = paymentMethod,
            Status = PaymentStatus.Succeeded,
            TransactionId = transactionId,
            Provider = "SimulatedStripeGateway",
            DurationMs = elapsedMs,
            CreatedAt = DateTime.UtcNow
        };
    }
}
