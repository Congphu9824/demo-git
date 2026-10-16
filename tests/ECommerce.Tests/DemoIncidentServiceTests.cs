using ECommerce.Application.Exceptions;
using ECommerce.Application.Services;
using ECommerce.Infrastructure.ExternalServices;
using ECommerce.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ECommerce.Tests;

public class DemoIncidentServiceTests
{
    private readonly DemoIncidentService _incidentService;

    public DemoIncidentServiceTests()
    {
        _incidentService = new DemoIncidentService(NullLogger<DemoIncidentService>.Instance);
    }

    [Fact]
    public void IncidentService_InitialState_AllIncidentsDisabled()
    {
        var status = _incidentService.GetStatus();

        status.SlowPaymentEnabled.Should().BeFalse();
        status.SlowDatabaseEnabled.Should().BeFalse();
        status.PaymentErrorEnabled.Should().BeFalse();
        status.DatabaseErrorEnabled.Should().BeFalse();
        status.ExternalApiErrorEnabled.Should().BeFalse();
        status.HighCpuEnabled.Should().BeFalse();
        status.HighMemoryEnabled.Should().BeFalse();
        status.TrafficSpikeEnabled.Should().BeFalse();
    }

    [Fact]
    public void IncidentService_ResetAll_ClearsAllFlags()
    {
        _incidentService.SetSlowPayment(true);
        _incidentService.SetSlowDatabase(true);
        _incidentService.SetPaymentError(true);
        _incidentService.SetDatabaseError(true);
        _incidentService.SetExternalApiError(true);

        _incidentService.ResetAll();

        var status = _incidentService.GetStatus();
        status.SlowPaymentEnabled.Should().BeFalse();
        status.SlowDatabaseEnabled.Should().BeFalse();
        status.PaymentErrorEnabled.Should().BeFalse();
        status.DatabaseErrorEnabled.Should().BeFalse();
        status.ExternalApiErrorEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task PaymentService_WhenPaymentErrorEnabled_ThrowsPaymentProcessingException()
    {
        var mockGateway = new Mock<ECommerce.Application.Interfaces.IExternalPaymentGateway>();
        _incidentService.SetPaymentError(true);

        var paymentService = new PaymentService(mockGateway.Object, _incidentService, NullLogger<PaymentService>.Instance);

        var act = () => paymentService.ProcessPaymentAsync(Guid.NewGuid(), 150m, "CreditCard");
        await act.Should().ThrowAsync<PaymentProcessingException>();
    }

    [Fact]
    public async Task ExternalPaymentGateway_WhenExternalApiErrorEnabled_ReturnsFailure()
    {
        _incidentService.SetExternalApiError(true);
        var gateway = new SimulatedPaymentGateway(_incidentService, NullLogger<SimulatedPaymentGateway>.Instance);

        var (success, txId, msg) = await gateway.ExecuteTransactionAsync(Guid.NewGuid(), 200m, "CreditCard");

        success.Should().BeFalse();
        msg.Should().Contain("500");
    }

    [Fact]
    public async Task HighCpuAndMemory_StartAndStop_ShouldBeSafeAndCancellable()
    {
        await _incidentService.StartHighCpuAsync();
        _incidentService.HighCpuEnabled.Should().BeTrue();
        _incidentService.StopHighCpu();
        _incidentService.HighCpuEnabled.Should().BeFalse();

        await _incidentService.StartHighMemoryAsync();
        _incidentService.HighMemoryEnabled.Should().BeTrue();
        _incidentService.StopHighMemory();
        _incidentService.HighMemoryEnabled.Should().BeFalse();
    }
}
