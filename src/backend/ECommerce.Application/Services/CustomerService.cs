using System.Diagnostics;
using ECommerce.Application.Diagnostics;
using ECommerce.Application.DTOs;
using ECommerce.Application.Exceptions;
using ECommerce.Application.Interfaces;
using ECommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ECommerce.Application.Services;

public class CustomerService : ICustomerService
{
    private readonly IAppDbContext _dbContext;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(IAppDbContext dbContext, ILogger<CustomerService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IEnumerable<CustomerDto>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ObservabilityActivitySource.StartActivity("GetCustomers");
        var customers = await _dbContext.Customers
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return customers.Select(c => new CustomerDto
        {
            Id = c.Id,
            Name = c.Name,
            Email = c.Email,
            PhoneNumber = c.PhoneNumber,
            Address = c.Address,
            Tier = c.Tier,
            IsActive = c.IsActive,
            CreatedAt = c.CreatedAt
        });
    }

    public async Task<CustomerDto?> GetCustomerByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var activity = ObservabilityActivitySource.StartActivity("GetCustomerById");
        activity?.SetTag("customer.id", id.ToString());

        var customer = await _dbContext.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (customer == null) return null;

        return new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Email = customer.Email,
            PhoneNumber = customer.PhoneNumber,
            Address = customer.Address,
            Tier = customer.Tier,
            IsActive = customer.IsActive,
            CreatedAt = customer.CreatedAt
        };
    }

    public async Task<Customer> ValidateAndGetCustomerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var activity = ObservabilityActivitySource.StartActivity("ValidateCustomer");
        activity?.SetTag("customer.id", id.ToString());

        var customer = await _dbContext.Customers
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (customer == null || !customer.IsActive)
        {
            _logger.LogWarning("Customer validation failed: Customer {CustomerId} not found or inactive", id);
            var ex = new CustomerNotFoundException(id);
            activity?.RecordException(ex);
            throw ex;
        }

        activity?.SetTag("customer.tier", customer.Tier);
        activity?.SetTag("customer.name", customer.Name);
        return customer;
    }
}
