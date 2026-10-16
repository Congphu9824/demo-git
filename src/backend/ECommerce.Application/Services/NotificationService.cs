using System.Diagnostics;
using ECommerce.Application.Diagnostics;
using ECommerce.Application.Interfaces;
using ECommerce.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ECommerce.Application.Services;

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public async Task SendOrderConfirmationNotificationAsync(Order order, CancellationToken cancellationToken = default)
    {
        using var activity = ObservabilityActivitySource.StartActivity("SendNotification");
        activity?.SetTag("order.id", order.Id.ToString());
        activity?.SetTag("customer.email", order.Customer?.Email ?? "unknown");
        activity?.SetTag("notification.type", "EmailOrderConfirmation");

        _logger.LogInformation("NotificationDispatched: Sending order confirmation for Order {OrderNumber} ({OrderId}) to {Email}",
            order.OrderNumber, order.Id, order.Customer?.Email);

        // Realistic async notification dispatch delay (20 - 50ms)
        await Task.Delay(Random.Shared.Next(20, 50), cancellationToken);
    }
}
