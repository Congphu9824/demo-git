using System.Diagnostics.Metrics;
using ECommerce.Application.Common;

namespace ECommerce.Application.Diagnostics;

public class ObservabilityMetrics
{
    public static readonly Meter Instance = new(TelemetryConstants.MeterName, TelemetryConstants.ServiceVersion);

    // Business & Order Metrics
    public static readonly Counter<long> OrdersCreatedTotal = Instance.CreateCounter<long>(
        name: "orders_created_total",
        unit: "{orders}",
        description: "Total number of orders successfully created and placed");

    public static readonly Counter<long> OrdersFailedTotal = Instance.CreateCounter<long>(
        name: "orders_failed_total",
        unit: "{orders}",
        description: "Total number of orders that failed during processing");

    public static readonly Histogram<double> OrderProcessingDuration = Instance.CreateHistogram<double>(
        name: "order_processing_duration",
        unit: "ms",
        description: "Duration of the complete order processing pipeline in milliseconds");

    // Payment Metrics
    public static readonly Counter<long> PaymentsTotal = Instance.CreateCounter<long>(
        name: "payments_total",
        unit: "{payments}",
        description: "Total number of payment attempts initiated");

    public static readonly Counter<long> PaymentsSuccessTotal = Instance.CreateCounter<long>(
        name: "payments_success_total",
        unit: "{payments}",
        description: "Total number of successful payment transactions");

    public static readonly Counter<long> PaymentsFailedTotal = Instance.CreateCounter<long>(
        name: "payments_failed_total",
        unit: "{payments}",
        description: "Total number of failed payment transactions");

    public static readonly Histogram<double> PaymentDuration = Instance.CreateHistogram<double>(
        name: "payment_duration",
        unit: "ms",
        description: "Payment processing latency in milliseconds");

    // Database Metrics
    public static readonly Histogram<double> DatabaseQueryDuration = Instance.CreateHistogram<double>(
        name: "database_query_duration",
        unit: "ms",
        description: "Database operation latency in milliseconds");

    public static readonly Counter<long> DatabaseErrorsTotal = Instance.CreateCounter<long>(
        name: "database_errors_total",
        unit: "{errors}",
        description: "Total number of database errors encountered");

    // External API Metrics
    public static readonly Counter<long> ExternalApiRequestsTotal = Instance.CreateCounter<long>(
        name: "external_api_requests_total",
        unit: "{requests}",
        description: "Total requests made to external partner APIs");

    public static readonly Counter<long> ExternalApiErrorsTotal = Instance.CreateCounter<long>(
        name: "external_api_errors_total",
        unit: "{errors}",
        description: "Total failures returned by external partner APIs");

    public static readonly Histogram<double> ExternalApiDuration = Instance.CreateHistogram<double>(
        name: "external_api_duration",
        unit: "ms",
        description: "External API call duration in milliseconds");

    // Demo Incident Metric
    public static readonly Counter<long> DemoIncidentTotal = Instance.CreateCounter<long>(
        name: "demo_incident_total",
        unit: "{incidents}",
        description: "Total number of simulated demo incidents triggered or processed");

    // API & Unhandled Exception Metrics
    public static readonly Counter<long> ApiExceptionsTotal = Instance.CreateCounter<long>(
        name: "api_exceptions_total",
        unit: "{exceptions}",
        description: "Total number of exceptions caught by the global error handling middleware");
}

