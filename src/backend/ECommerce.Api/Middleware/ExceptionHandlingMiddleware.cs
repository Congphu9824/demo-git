using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ECommerce.Application.Diagnostics;
using ECommerce.Application.Exceptions;

namespace ECommerce.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        var spanId = Activity.Current?.SpanId.ToString() ?? string.Empty;

        var (statusCode, title, message) = exception switch
        {
            CustomerNotFoundException or ProductNotFoundException => (
                (int)HttpStatusCode.NotFound,
                "Resource Not Found",
                exception.Message
            ),
            InsufficientStockException or ArgumentException => (
                (int)HttpStatusCode.BadRequest,
                "Bad Request / Business Rule Violation",
                exception.Message
            ),
            DatabaseSimulatedException => (
                (int)HttpStatusCode.ServiceUnavailable,
                "Database Unavailable",
                exception.Message
            ),
            PaymentProcessingException => (
                (int)HttpStatusCode.InternalServerError,
                "Payment Processing Failure",
                exception.Message
            ),
            ExternalServiceException => (
                (int)HttpStatusCode.BadGateway,
                "External Dependency Failure",
                exception.Message
            ),
            _ => (
                (int)HttpStatusCode.InternalServerError,
                "Internal Server Error",
                exception.Message
            )
        };

        // 1. OpenTelemetry Activity Tagging (Marks Span as RED Error in Tempo and enables TraceQL { status = error })
        if (Activity.Current != null)
        {
            Activity.Current.RecordException(exception);
            Activity.Current.SetStatus(ActivityStatusCode.Error, message);
            Activity.Current.SetTag("error", true);
            Activity.Current.SetTag("error.type", exception.GetType().FullName);
            Activity.Current.SetTag("http.response.status_code", statusCode);
        }

        // 2. Prometheus Custom Metrics (Aggregates error counts by exception_type and status_code)
        ObservabilityMetrics.ApiExceptionsTotal.Add(1,
            new KeyValuePair<string, object?>("exception_type", exception.GetType().Name),
            new KeyValuePair<string, object?>("status_code", statusCode.ToString()),
            new KeyValuePair<string, object?>("http_method", context.Request.Method),
            new KeyValuePair<string, object?>("path", context.Request.Path.Value ?? "/"));

        // 3. Loki Structured Logging (Includes TraceId for automatic Derived Field button in Grafana)
        _logger.LogError(exception,
            "HTTP Request Failed [{StatusCode}] {Title}: {Message} | ExceptionType: {ExceptionType} | Path: {Path} | Method: {Method} | TraceId: {TraceId}",
            statusCode, title, message, exception.GetType().Name, context.Request.Path.Value ?? "/", context.Request.Method, traceId);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        var response = new
        {
            statusCode,
            title,
            exceptionType = exception.GetType().Name,
            detail = message,
            traceId,
            spanId,
            timestamp = DateTime.UtcNow
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
