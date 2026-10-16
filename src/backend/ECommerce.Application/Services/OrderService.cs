using System.Diagnostics;
using ECommerce.Application.Diagnostics;
using ECommerce.Application.DTOs;
using ECommerce.Application.Exceptions;
using ECommerce.Application.Interfaces;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ECommerce.Application.Services;

public class OrderService : IOrderService
{
    private readonly IAppDbContext _dbContext;
    private readonly ICustomerService _customerService;
    private readonly IPaymentService _paymentService;
    private readonly INotificationService _notificationService;
    private readonly IDemoIncidentService _demoIncidentService;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IAppDbContext dbContext,
        ICustomerService customerService,
        IPaymentService paymentService,
        INotificationService notificationService,
        IDemoIncidentService demoIncidentService,
        ILogger<OrderService> logger)
    {
        _dbContext = dbContext;
        _customerService = customerService;
        _paymentService = paymentService;
        _notificationService = notificationService;
        _demoIncidentService = demoIncidentService;
        _logger = logger;
    }

    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var orderId = Guid.NewGuid();
        var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        using var rootActivity = ObservabilityActivitySource.StartActivity("CreateOrder");
        rootActivity?.SetTag("order.id", orderId.ToString());
        rootActivity?.SetTag("order.number", orderNumber);
        rootActivity?.SetTag("customer.id", request.CustomerId.ToString());
        rootActivity?.SetTag("order.items_count", request.Items?.Count ?? 0);

        try
        {
            if (request.Items == null || request.Items.Count == 0)
            {
                throw new ArgumentException("Order must contain at least one item.");
            }

            // Step 1: Validate Customer
            var customer = await _customerService.ValidateAndGetCustomerAsync(request.CustomerId, cancellationToken);

            // Step 2: Check Inventory & Build Items
            var (orderItems, totalAmount) = await CheckInventoryAndReserveStockAsync(orderId, request.Items, cancellationToken);
            rootActivity?.SetTag("order.total_amount", totalAmount);

            // Step 3: Process Payment
            var payment = await _paymentService.ProcessPaymentAsync(orderId, totalAmount, request.PaymentMethod, cancellationToken);

            // Step 4: Save Order to Database (with Database Chaos Hooks)
            var order = new Order
            {
                Id = orderId,
                OrderNumber = orderNumber,
                CustomerId = customer.Id,
                Customer = customer,
                TotalAmount = totalAmount,
                Status = OrderStatus.Completed,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
                Items = orderItems,
                Payment = payment
            };

            await ExecuteDatabaseSaveOrderAsync(order, cancellationToken);

            // Step 5: Send Notification
            await _notificationService.SendOrderConfirmationNotificationAsync(order, cancellationToken);

            totalStopwatch.Stop();
            var durationMs = totalStopwatch.Elapsed.TotalMilliseconds;

            ObservabilityMetrics.OrdersCreatedTotal.Add(1, new KeyValuePair<string, object?>("status", "Success"));
            ObservabilityMetrics.OrderProcessingDuration.Record(durationMs, new KeyValuePair<string, object?>("status", "Success"));

            if (durationMs > 1000)
            {
                rootActivity?.SetTag("is_slow", true);
                rootActivity?.SetTag("order.duration_ms", durationMs);

                var bottleneck = rootActivity?.GetTagItem("bottleneck.component")?.ToString() ?? "Unknown";
                var slowReason = rootActivity?.GetTagItem("slow.reason")?.ToString() ?? "Order pipeline latency exceeded 1000ms SLA";

                _logger.LogWarning("OrderSlow: Order {OrderNumber} ({OrderId}) took {DurationMs:F2}ms to complete (>1000ms SLA). Bottleneck: {BottleneckComponent}. Reason: {SlowReason}. TraceId: {TraceId}",
                    order.OrderNumber, order.Id, durationMs, bottleneck, slowReason, Activity.Current?.TraceId);
            }
            else
            {
                _logger.LogInformation("OrderCreated: Successfully created Order {OrderNumber} ({OrderId}) for Customer {CustomerName} ({CustomerId}) with Total {TotalAmount:C} in {DurationMs:F2}ms",
                    order.OrderNumber, order.Id, customer.Name, customer.Id, totalAmount, durationMs);
            }

            return MapToDto(order);
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            var durationMs = totalStopwatch.Elapsed.TotalMilliseconds;

            ObservabilityMetrics.OrdersFailedTotal.Add(1, new KeyValuePair<string, object?>("error_type", ex.GetType().Name));
            ObservabilityMetrics.OrderProcessingDuration.Record(durationMs, new KeyValuePair<string, object?>("status", "Failed"));

            rootActivity?.SetTag("order.failed", true);
            rootActivity?.SetTag("failure.error_type", ex.GetType().Name);
            rootActivity?.RecordException(ex);

            var errorComponent = rootActivity?.GetTagItem("error.component")?.ToString() ?? "OrderService";
            var errorReason = rootActivity?.GetTagItem("error.reason")?.ToString() ?? ex.Message;

            _logger.LogError(ex, "OrderFailed: Order processing failed for Customer {CustomerId} after {DurationMs:F2}ms. Failed Component: {Component}. Root Cause: {Reason}. TraceId: {TraceId}",
                request.CustomerId, durationMs, errorComponent, errorReason, Activity.Current?.TraceId);

            throw;
        }
    }

    private async Task<(List<OrderItem> items, decimal totalAmount)> CheckInventoryAndReserveStockAsync(
        Guid orderId,
        List<OrderItemRequest> itemRequests,
        CancellationToken cancellationToken)
    {
        using var activity = ObservabilityActivitySource.StartActivity("CheckInventory");
        activity?.SetTag("order.id", orderId.ToString());

        var orderItems = new List<OrderItem>();
        decimal totalAmount = 0;

        foreach (var req in itemRequests)
        {
            var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == req.ProductId, cancellationToken);
            if (product == null)
            {
                var notFoundEx = new ProductNotFoundException(req.ProductId);
                activity?.RecordException(notFoundEx);
                throw notFoundEx;
            }

            if (product.StockQuantity < req.Quantity)
            {
                var stockEx = new InsufficientStockException(product.Id, product.Name, req.Quantity, product.StockQuantity);
                activity?.RecordException(stockEx);
                throw stockEx;
            }

            // Deduct stock
            product.StockQuantity -= req.Quantity;

            var item = new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                ProductId = product.Id,
                Product = product,
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = req.Quantity
            };

            orderItems.Add(item);
            totalAmount += item.TotalPrice;
        }

        activity?.SetTag("inventory.items_count", orderItems.Count);
        activity?.SetTag("inventory.total_amount", totalAmount);

        return (orderItems, totalAmount);
    }

    private async Task ExecuteDatabaseSaveOrderAsync(Order order, CancellationToken cancellationToken)
    {
        var dbStopwatch = Stopwatch.StartNew();
        using var activity = ObservabilityActivitySource.StartActivity("DatabaseQuery");
        activity?.SetTag("database.operation", "INSERT Orders, OrderItems, Payments");
        activity?.SetTag("order.id", order.Id.ToString());

        // Incident Simulation: Slow Database
        if (_demoIncidentService.SlowDatabaseEnabled)
        {
            activity?.SetTag("demo.incident", "SlowDatabase");
            activity?.SetTag("bottleneck.component", "SQL Database / AppDbContext");
            activity?.SetTag("bottleneck.operation", "INSERT Orders, OrderItems, Payments");
            activity?.SetTag("performance.status", "Bottleneck");

            var slowDelay = Random.Shared.Next(3000, 5000);
            activity?.SetTag("bottleneck.duration_ms", slowDelay);
            activity?.SetTag("slow.reason", $"Simulated database I/O contention & lock wait ({slowDelay}ms)");

            // Propagate to parent root activity
            Activity.Current?.Parent?.SetTag("bottleneck.component", "SQL Database / AppDbContext");
            Activity.Current?.Parent?.SetTag("slow.reason", $"Database save operation delayed by {slowDelay}ms");
            Activity.Current?.Parent?.SetTag("is_slow", true);

            ObservabilityMetrics.DemoIncidentTotal.Add(1, new KeyValuePair<string, object?>("incident_type", "SlowDatabase"));

            _logger.LogWarning("DatabaseSlow: Bottleneck detected at {Component}! Database query took {DelayMs}ms on Order {OrderId}. Root Cause: {Reason}. TraceId: {TraceId}",
                "SQL Database / AppDbContext", slowDelay, order.Id, "Simulated database I/O lock contention", Activity.Current?.TraceId);

            await Task.Delay(slowDelay, cancellationToken);
        }

        // Incident Simulation: Database Error / Timeout
        if (_demoIncidentService.DatabaseErrorEnabled)
        {
            dbStopwatch.Stop();
            var durationMs = dbStopwatch.Elapsed.TotalMilliseconds;
            ObservabilityMetrics.DatabaseQueryDuration.Record(durationMs, new KeyValuePair<string, object?>("operation", "InsertOrder"), new KeyValuePair<string, object?>("status", "Failed"));
            ObservabilityMetrics.DatabaseErrorsTotal.Add(1, new KeyValuePair<string, object?>("operation", "InsertOrder"));
            ObservabilityMetrics.DemoIncidentTotal.Add(1, new KeyValuePair<string, object?>("incident_type", "DatabaseError"));

            var dbEx = new DatabaseSimulatedException($"SQL Server connection timed out while executing command: INSERT INTO Orders (Id, OrderNumber, TotalAmount) VALUES ('{order.Id}', '{order.OrderNumber}', {order.TotalAmount}). Lock request time out period exceeded.");
            activity?.SetTag("error.component", "SQL Database / AppDbContext");
            activity?.SetTag("error.reason", "SQL Server connection timed out. Lock request time out period exceeded.");
            activity?.RecordException(dbEx);

            Activity.Current?.Parent?.SetTag("error.component", "SQL Database / AppDbContext");
            Activity.Current?.Parent?.SetTag("error.reason", "Database lock wait timeout");

            _logger.LogError(dbEx, "DatabaseFailed: Failure at {Component}! Root Cause: {Reason}. Order {OrderId}. TraceId: {TraceId}",
                "SQL Database / AppDbContext", "SQL Server connection timed out", order.Id, Activity.Current?.TraceId);

            throw dbEx;
        }

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);

        dbStopwatch.Stop();
        var elapsed = dbStopwatch.Elapsed.TotalMilliseconds;
        ObservabilityMetrics.DatabaseQueryDuration.Record(elapsed, new KeyValuePair<string, object?>("operation", "InsertOrder"), new KeyValuePair<string, object?>("status", "Success"));

        _logger.LogInformation("DatabaseSaved: Order {OrderId} written to SQL Server in {ElapsedMs:F2}ms", order.Id, elapsed);
    }

    public async Task<IEnumerable<OrderDto>> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ObservabilityActivitySource.StartActivity("GetOrders");

        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.Payment)
            .OrderByDescending(o => o.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        activity?.SetTag("orders.count", orders.Count);

        return orders.Select(MapToDto);
    }

    public async Task<OrderDto?> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var activity = ObservabilityActivitySource.StartActivity("GetOrderById");
        activity?.SetTag("order.id", id.ToString());

        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order == null) return null;

        return MapToDto(order);
    }

    private static OrderDto MapToDto(Order order)
    {
        return new OrderDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerId = order.CustomerId,
            CustomerName = order.Customer?.Name ?? string.Empty,
            CustomerEmail = order.Customer?.Email ?? string.Empty,
            TotalAmount = order.TotalAmount,
            Status = order.Status.ToString(),
            FailureReason = order.FailureReason,
            CreatedAt = order.CreatedAt,
            CompletedAt = order.CompletedAt,
            Items = order.Items.Select(i => new OrderItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity,
                TotalPrice = i.TotalPrice
            }).ToList(),
            Payment = order.Payment == null ? null : new PaymentDto
            {
                Id = order.Payment.Id,
                Amount = order.Payment.Amount,
                PaymentMethod = order.Payment.PaymentMethod,
                Status = order.Payment.Status.ToString(),
                TransactionId = order.Payment.TransactionId,
                Provider = order.Payment.Provider,
                DurationMs = order.Payment.DurationMs,
                CreatedAt = order.Payment.CreatedAt
            }
        };
    }
}
