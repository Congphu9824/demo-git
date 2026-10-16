namespace ECommerce.Application.DTOs;

public class DemoStatusDto
{
    public bool SlowPaymentEnabled { get; set; }
    public bool SlowDatabaseEnabled { get; set; }
    public bool PaymentErrorEnabled { get; set; }
    public bool DatabaseErrorEnabled { get; set; }
    public bool ExternalApiErrorEnabled { get; set; }
    public bool HighCpuEnabled { get; set; }
    public bool HighMemoryEnabled { get; set; }
    public bool TrafficSpikeEnabled { get; set; }

    public int ActiveTrafficRps { get; set; }
    public long AllocatedMemoryBytes { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

public class TrafficSpikeRequest
{
    public int RequestsPerSecond { get; set; } = 50;
    public int DurationSeconds { get; set; } = 30;
}
