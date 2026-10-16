using ECommerce.Application.DTOs;
using ECommerce.Application.Exceptions;
using ECommerce.Application.Interfaces;
using ECommerce.Application.Services;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Enums;
using ECommerce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ECommerce.Tests;

public class OrderServiceTests
{
    private readonly AppDbContext _dbContext;
    private readonly Mock<IDemoIncidentService> _demoIncidentMock;
    private readonly Mock<IExternalPaymentGateway> _paymentGatewayMock;
    private readonly OrderService _orderService;
    private readonly CustomerService _customerService;
    private readonly PaymentService _paymentService;
    private readonly NotificationService _notificationService;

    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();

    public OrderServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ECommerceTestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new AppDbContext(options);
        _demoIncidentMock = new Mock<IDemoIncidentService>();
        _paymentGatewayMock = new Mock<IExternalPaymentGateway>();

        _customerService = new CustomerService(_dbContext, NullLogger<CustomerService>.Instance);
        _paymentService = new PaymentService(_paymentGatewayMock.Object, _demoIncidentMock.Object, NullLogger<PaymentService>.Instance);
        _notificationService = new NotificationService(NullLogger<NotificationService>.Instance);

        _orderService = new OrderService(
            _dbContext,
            _customerService,
            _paymentService,
            _notificationService,
            _demoIncidentMock.Object,
            NullLogger<OrderService>.Instance);

        // Seed test customer & product
        _dbContext.Customers.Add(new Customer
        {
            Id = _customerId,
            Name = "Test Customer",
            Email = "test@example.com",
            IsActive = true
        });

        _dbContext.Products.Add(new Product
        {
            Id = _productId,
            Name = "Test Headphones",
            Sku = "TEST-HP-01",
            Price = 100m,
            StockQuantity = 10,
            IsActive = true
        });

        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task CreateOrder_WithValidData_ShouldSucceedAndDeductStock()
    {
        // Arrange
        _paymentGatewayMock
            .Setup(g => g.ExecuteTransactionAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "tx_test_123", "Approved"));

        var request = new CreateOrderRequest
        {
            CustomerId = _customerId,
            PaymentMethod = "CreditCard",
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = _productId, Quantity = 2 }
            }
        };

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TotalAmount.Should().Be(200m);
        result.Status.Should().Be(OrderStatus.Completed.ToString());
        result.Items.Should().HaveCount(1);
        result.Payment.Should().NotBeNull();
        result.Payment!.Status.Should().Be(PaymentStatus.Succeeded.ToString());

        var productInDb = await _dbContext.Products.FindAsync(_productId);
        productInDb!.StockQuantity.Should().Be(8); // 10 - 2 = 8
    }

    [Fact]
    public async Task CreateOrder_WithInvalidCustomer_ShouldThrowCustomerNotFoundException()
    {
        // Arrange
        var request = new CreateOrderRequest
        {
            CustomerId = Guid.NewGuid(), // Non-existent
            PaymentMethod = "CreditCard",
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = _productId, Quantity = 1 }
            }
        };

        // Act & Assert
        var act = () => _orderService.CreateOrderAsync(request);
        await act.Should().ThrowAsync<CustomerNotFoundException>();
    }

    [Fact]
    public async Task CreateOrder_WithInsufficientStock_ShouldThrowInsufficientStockException()
    {
        // Arrange
        var request = new CreateOrderRequest
        {
            CustomerId = _customerId,
            PaymentMethod = "CreditCard",
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = _productId, Quantity = 99 } // More than 10 in stock
            }
        };

        // Act & Assert
        var act = () => _orderService.CreateOrderAsync(request);
        await act.Should().ThrowAsync<InsufficientStockException>();
    }
}
