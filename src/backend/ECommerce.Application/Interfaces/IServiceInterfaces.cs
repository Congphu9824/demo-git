using ECommerce.Application.DTOs;
using ECommerce.Domain.Entities;

namespace ECommerce.Application.Interfaces;

public interface IOrderService
{
    Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);
    Task<IEnumerable<OrderDto>> GetOrdersAsync(CancellationToken cancellationToken = default);
    Task<OrderDto?> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IProductService
{
    Task<IEnumerable<ProductDto>> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<ProductDto?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface ICustomerService
{
    Task<IEnumerable<CustomerDto>> GetCustomersAsync(CancellationToken cancellationToken = default);
    Task<CustomerDto?> GetCustomerByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Customer> ValidateAndGetCustomerAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IPaymentService
{
    Task<Payment> ProcessPaymentAsync(Guid orderId, decimal amount, string paymentMethod, CancellationToken cancellationToken = default);
}

public interface IExternalPaymentGateway
{
    Task<(bool Success, string TransactionId, string Message)> ExecuteTransactionAsync(Guid orderId, decimal amount, string paymentMethod, CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task SendOrderConfirmationNotificationAsync(Order order, CancellationToken cancellationToken = default);
}
