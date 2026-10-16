using System.Diagnostics;
using ECommerce.Application.DTOs;
using ECommerce.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DemoController : ControllerBase
{
    private readonly IDemoIncidentService _incidentService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DemoController> _logger;

    public DemoController(
        IDemoIncidentService incidentService,
        IServiceProvider serviceProvider,
        ILogger<DemoController> logger)
    {
        _incidentService = incidentService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Current status of all demo incidents and active simulation jobs
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(DemoStatusDto), StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        return Ok(_incidentService.GetStatus());
    }

    /// <summary>
    /// Reset all active demo incidents to clean baseline
    /// </summary>
    [HttpPost("reset")]
    [ProducesResponseType(typeof(DemoStatusDto), StatusCodes.Status200OK)]
    public IActionResult Reset()
    {
        _incidentService.ResetAll();
        return Ok(_incidentService.GetStatus());
    }

    // --- Slow Payment ---
    [HttpPost("slow-payment/enable")]
    public IActionResult EnableSlowPayment()
    {
        _incidentService.SetSlowPayment(true);
        return Ok(new { message = "Slow Payment incident enabled (latency ~5000ms inside PaymentService)", status = _incidentService.GetStatus() });
    }

    [HttpPost("slow-payment/disable")]
    public IActionResult DisableSlowPayment()
    {
        _incidentService.SetSlowPayment(false);
        return Ok(new { message = "Slow Payment incident disabled (normal latency 100-200ms)", status = _incidentService.GetStatus() });
    }

    // --- Slow Database ---
    [HttpPost("slow-database/enable")]
    public IActionResult EnableSlowDatabase()
    {
        _incidentService.SetSlowDatabase(true);
        return Ok(new { message = "Slow Database incident enabled (latency ~3000-5000ms in DB save)", status = _incidentService.GetStatus() });
    }

    [HttpPost("slow-database/disable")]
    public IActionResult DisableSlowDatabase()
    {
        _incidentService.SetSlowDatabase(false);
        return Ok(new { message = "Slow Database incident disabled (normal DB execution)", status = _incidentService.GetStatus() });
    }

    // --- Payment Error ---
    [HttpPost("payment-error/enable")]
    public IActionResult EnablePaymentError()
    {
        _incidentService.SetPaymentError(true);
        return Ok(new { message = "Payment Error incident enabled (controlled payment exceptions)", status = _incidentService.GetStatus() });
    }

    [HttpPost("payment-error/disable")]
    public IActionResult DisablePaymentError()
    {
        _incidentService.SetPaymentError(false);
        return Ok(new { message = "Payment Error incident disabled", status = _incidentService.GetStatus() });
    }

    // --- Database Error ---
    [HttpPost("database-error/enable")]
    public IActionResult EnableDatabaseError()
    {
        _incidentService.SetDatabaseError(true);
        return Ok(new { message = "Database Error incident enabled (simulated lock timeout / 500 error)", status = _incidentService.GetStatus() });
    }

    [HttpPost("database-error/disable")]
    public IActionResult DisableDatabaseError()
    {
        _incidentService.SetDatabaseError(false);
        return Ok(new { message = "Database Error incident disabled", status = _incidentService.GetStatus() });
    }

    // --- External API Error ---
    [HttpPost("external-api-error/enable")]
    public IActionResult EnableExternalApiError()
    {
        _incidentService.SetExternalApiError(true);
        return Ok(new { message = "External API Error incident enabled (Stripe gateway returns 500)", status = _incidentService.GetStatus() });
    }

    [HttpPost("external-api-error/disable")]
    public IActionResult DisableExternalApiError()
    {
        _incidentService.SetExternalApiError(false);
        return Ok(new { message = "External API Error incident disabled", status = _incidentService.GetStatus() });
    }

    // --- High CPU ---
    [HttpPost("high-cpu/start")]
    public async Task<IActionResult> StartHighCpu(CancellationToken cancellationToken)
    {
        await _incidentService.StartHighCpuAsync(cancellationToken);
        return Ok(new { message = "High CPU simulation started across worker threads", status = _incidentService.GetStatus() });
    }

    [HttpPost("high-cpu/stop")]
    public IActionResult StopHighCpu()
    {
        _incidentService.StopHighCpu();
        return Ok(new { message = "High CPU simulation stopped", status = _incidentService.GetStatus() });
    }

    // --- High Memory ---
    [HttpPost("high-memory/start")]
    public async Task<IActionResult> StartHighMemory(CancellationToken cancellationToken)
    {
        await _incidentService.StartHighMemoryAsync(cancellationToken);
        return Ok(new { message = "High Memory simulation started (allocating up to ~180MB safely)", status = _incidentService.GetStatus() });
    }

    [HttpPost("high-memory/stop")]
    public IActionResult StopHighMemory()
    {
        _incidentService.StopHighMemory();
        return Ok(new { message = "High Memory simulation stopped and memory collected", status = _incidentService.GetStatus() });
    }

    // --- Traffic Spike ---
    [HttpPost("traffic-spike/start")]
    public async Task<IActionResult> StartTrafficSpike([FromBody] TrafficSpikeRequest? request, CancellationToken cancellationToken)
    {
        var rps = request?.RequestsPerSecond ?? 50;
        var durationSeconds = request?.DurationSeconds ?? 30;

        await _incidentService.StartTrafficSpikeAsync(rps, durationSeconds, async token =>
        {
            using var scope = _serviceProvider.CreateScope();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var customerService = scope.ServiceProvider.GetRequiredService<ICustomerService>();
            var productService = scope.ServiceProvider.GetRequiredService<IProductService>();

            var customers = (await customerService.GetCustomersAsync(token)).ToList();
            var products = (await productService.GetProductsAsync(token)).ToList();

            if (customers.Count > 0 && products.Count > 0)
            {
                var randomCustomer = customers[Random.Shared.Next(customers.Count)];
                var randomProduct = products[Random.Shared.Next(products.Count)];

                await orderService.CreateOrderAsync(new CreateOrderRequest
                {
                    CustomerId = randomCustomer.Id,
                    PaymentMethod = "CreditCard",
                    Items = new List<OrderItemRequest>
                    {
                        new() { ProductId = randomProduct.Id, Quantity = 1 }
                    }
                }, token);
            }
        }, cancellationToken);

        return Ok(new { message = $"Traffic spike initiated at {rps} RPS for {durationSeconds}s", status = _incidentService.GetStatus() });
    }

    [HttpPost("traffic-spike/stop")]
    public IActionResult StopTrafficSpike()
    {
        _incidentService.StopTrafficSpike();
        return Ok(new { message = "Traffic spike generator stopped", status = _incidentService.GetStatus() });
    }

    /// <summary>
    /// 1-Click Simulation: Trigger a slow order to instantly demonstrate root cause & distributed tracing
    /// </summary>
    [HttpPost("simulate-slow-order")]
    public async Task<IActionResult> SimulateSlowOrder([FromQuery] string type = "payment", CancellationToken cancellationToken = default)
    {
        var isPayment = !string.Equals(type, "database", StringComparison.OrdinalIgnoreCase);
        var bottleneckComponent = isPayment ? "Payment Gateway / Stripe" : "SQL Database / AppDbContext";
        var slowReason = isPayment
            ? "Payment gateway response delay (Stripe timeout simulation: 5000ms)"
            : "Simulated database I/O contention & lock wait (~3000-5000ms)";

        var sw = Stopwatch.StartNew();
        if (isPayment)
            _incidentService.SetSlowPayment(true);
        else
            _incidentService.SetSlowDatabase(true);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var customerService = scope.ServiceProvider.GetRequiredService<ICustomerService>();
            var productService = scope.ServiceProvider.GetRequiredService<IProductService>();

            var customer = (await customerService.GetCustomersAsync(cancellationToken)).FirstOrDefault();
            var product = (await productService.GetProductsAsync(cancellationToken)).FirstOrDefault();

            if (customer == null || product == null)
            {
                return BadRequest(new { error = "Seed data missing. Customers or Products empty." });
            }

            var order = await orderService.CreateOrderAsync(new CreateOrderRequest
            {
                CustomerId = customer.Id,
                PaymentMethod = "CreditCard",
                Items = new List<OrderItemRequest>
                {
                    new() { ProductId = product.Id, Quantity = 1 }
                }
            }, cancellationToken);

            sw.Stop();
            var traceId = Activity.Current?.TraceId.ToString() ?? string.Empty;

            return Ok(new
            {
                status = "Success",
                message = $"Slow order simulation completed. Bottleneck identified at '{bottleneckComponent}'.",
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                durationMs = sw.Elapsed.TotalMilliseconds,
                bottleneckComponent,
                slowReason,
                traceId,
                grafanaDashboardUrl = $"http://localhost:3001/d/application-observability?var-trace_id={traceId}",
                grafanaExploreUrl = $"http://localhost:3001/explore?left=%7B%22datasource%22:%22tempo%22,%22queries%22:%5B%7B%22refId%22:%22A%22,%22queryType%22:%22traceId%22,%22query%22:%22{traceId}%22%7D%5D%7D"
            });
        }
        finally
        {
            if (isPayment)
                _incidentService.SetSlowPayment(false);
            else
                _incidentService.SetSlowDatabase(false);
        }
    }

    /// <summary>
    /// 1-Click Simulation: Trigger an order that fails to instantly demonstrate error tracing & root cause
    /// </summary>
    [HttpPost("simulate-error-order")]
    public async Task<IActionResult> SimulateErrorOrder([FromQuery] string type = "payment", CancellationToken cancellationToken = default)
    {
        var isPayment = !string.Equals(type, "database", StringComparison.OrdinalIgnoreCase);
        var failedComponent = isPayment ? "Payment Gateway / Stripe" : "SQL Database / AppDbContext";
        var errorReason = isPayment
            ? "Payment declined by simulated Stripe gateway (fraud check / declined card)"
            : "SQL Server connection timed out / lock request timeout exceeded";

        var sw = Stopwatch.StartNew();
        if (isPayment)
            _incidentService.SetPaymentError(true);
        else
            _incidentService.SetDatabaseError(true);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var customerService = scope.ServiceProvider.GetRequiredService<ICustomerService>();
            var productService = scope.ServiceProvider.GetRequiredService<IProductService>();

            var customer = (await customerService.GetCustomersAsync(cancellationToken)).FirstOrDefault();
            var product = (await productService.GetProductsAsync(cancellationToken)).FirstOrDefault();

            if (customer == null || product == null)
            {
                return BadRequest(new { error = "Seed data missing." });
            }

            await orderService.CreateOrderAsync(new CreateOrderRequest
            {
                CustomerId = customer.Id,
                PaymentMethod = "CreditCard",
                Items = new List<OrderItemRequest>
                {
                    new() { ProductId = product.Id, Quantity = 1 }
                }
            }, cancellationToken);

            return Ok(new { message = "Order unexpectedly completed without error." });
        }
        catch (Exception ex)
        {
            sw.Stop();
            var traceId = Activity.Current?.TraceId.ToString() ?? string.Empty;

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                status = "Failed",
                message = $"Error order simulation triggered. Failure detected at '{failedComponent}'.",
                errorType = ex.GetType().Name,
                errorMessage = ex.Message,
                durationMs = sw.Elapsed.TotalMilliseconds,
                failedComponent,
                errorReason,
                traceId,
                grafanaDashboardUrl = $"http://localhost:3001/d/application-observability?var-trace_id={traceId}",
                grafanaExploreUrl = $"http://localhost:3001/explore?left=%7B%22datasource%22:%22tempo%22,%22queries%22:%5B%7B%22refId%22:%22A%22,%22queryType%22:%22traceId%22,%22query%22:%22{traceId}%22%7D%5D%7D"
            });
        }
        finally
        {
            if (isPayment)
                _incidentService.SetPaymentError(false);
            else
                _incidentService.SetDatabaseError(false);
        }
    }
}
