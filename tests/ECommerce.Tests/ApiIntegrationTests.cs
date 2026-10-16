using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ECommerce.Application.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ECommerce.Tests;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_Endpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
    }

    [Fact]
    public async Task HealthLive_Endpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_Endpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Metrics_Endpoint_ReturnsPrometheusMetrics()
    {
        var response = await _client.GetAsync("/metrics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("# TYPE");
    }

    [Fact]
    public async Task GetProducts_ReturnsSeededProducts()
    {
        var response = await _client.GetAsync("/api/products");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var products = await response.Content.ReadFromJsonAsync<List<ProductDto>>(_jsonOptions);
        products.Should().NotBeNull();
        products!.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetCustomers_ReturnsSeededCustomers()
    {
        var response = await _client.GetAsync("/api/customers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var customers = await response.Content.ReadFromJsonAsync<List<CustomerDto>>(_jsonOptions);
        customers.Should().NotBeNull();
        customers!.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task DemoStatus_ReturnsInitialStatus()
    {
        var response = await _client.GetAsync("/api/demo/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await response.Content.ReadFromJsonAsync<DemoStatusDto>(_jsonOptions);
        status.Should().NotBeNull();
    }

    [Fact]
    public async Task PostOrders_CreatesNewOrder()
    {
        // 1. Get an existing customer and product
        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers", _jsonOptions);
        var products = await _client.GetFromJsonAsync<List<ProductDto>>("/api/products", _jsonOptions);

        customers.Should().NotBeNull();
        products.Should().NotBeNull();

        var customer = customers!.First();
        var product = products!.First();

        var request = new CreateOrderRequest
        {
            CustomerId = customer.Id,
            PaymentMethod = "CreditCard",
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = product.Id, Quantity = 1 }
            }
        };

        // 2. Submit order
        var postResponse = await _client.PostAsJsonAsync("/api/orders", request);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdOrder = await postResponse.Content.ReadFromJsonAsync<OrderDto>(_jsonOptions);
        createdOrder.Should().NotBeNull();
        createdOrder!.Id.Should().NotBeEmpty();
        createdOrder.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task PostOrders_WithNonExistentCustomer_ReturnsNotFoundWithErrorPayload()
    {
        var request = new CreateOrderRequest
        {
            CustomerId = Guid.NewGuid(), // Non-existent
            PaymentMethod = "CreditCard",
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = Guid.NewGuid(), Quantity = 1 }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        root.GetProperty("statusCode").GetInt32().Should().Be(404);
        root.GetProperty("title").GetString().Should().Be("Resource Not Found");
        root.GetProperty("exceptionType").GetString().Should().Be("CustomerNotFoundException");
        root.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PostOrders_WithEmptyItems_ReturnsBadRequestWithErrorPayload()
    {
        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers", _jsonOptions);
        var customer = customers!.First();

        var request = new CreateOrderRequest
        {
            CustomerId = customer.Id,
            PaymentMethod = "CreditCard",
            Items = new List<OrderItemRequest>()
        };

        var response = await _client.PostAsJsonAsync("/api/orders", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        root.GetProperty("statusCode").GetInt32().Should().Be(400);
        root.GetProperty("title").GetString().Should().Be("Bad Request / Business Rule Violation");
        root.GetProperty("exceptionType").GetString().Should().Be("ArgumentException");
        root.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
