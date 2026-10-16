using ECommerce.Application.DTOs;

namespace ECommerce.Application.Interfaces;

public interface IDemoIncidentService
{
    bool SlowPaymentEnabled { get; }
    bool SlowDatabaseEnabled { get; }
    bool PaymentErrorEnabled { get; }
    bool DatabaseErrorEnabled { get; }
    bool ExternalApiErrorEnabled { get; }
    bool HighCpuEnabled { get; }
    bool HighMemoryEnabled { get; }
    bool TrafficSpikeEnabled { get; }

    DemoStatusDto GetStatus();
    void ResetAll();

    void SetSlowPayment(bool enabled);
    void SetSlowDatabase(bool enabled);
    void SetPaymentError(bool enabled);
    void SetDatabaseError(bool enabled);
    void SetExternalApiError(bool enabled);

    Task StartHighCpuAsync(CancellationToken cancellationToken = default);
    void StopHighCpu();

    Task StartHighMemoryAsync(CancellationToken cancellationToken = default);
    void StopHighMemory();

    Task StartTrafficSpikeAsync(int rps, int durationSeconds, Func<CancellationToken, Task> sendRequestAction, CancellationToken cancellationToken = default);
    void StopTrafficSpike();
}
