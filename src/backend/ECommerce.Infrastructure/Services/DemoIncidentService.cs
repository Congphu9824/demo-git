using System.Collections.Concurrent;
using System.Diagnostics;
using ECommerce.Application.Diagnostics;
using ECommerce.Application.DTOs;
using ECommerce.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.Services;

public class DemoIncidentService : IDemoIncidentService
{
    private readonly ILogger<DemoIncidentService> _logger;

    // In-memory state flags
    private volatile bool _slowPaymentEnabled;
    private volatile bool _slowDatabaseEnabled;
    private volatile bool _paymentErrorEnabled;
    private volatile bool _databaseErrorEnabled;
    private volatile bool _externalApiErrorEnabled;
    private volatile bool _highCpuEnabled;
    private volatile bool _highMemoryEnabled;
    private volatile bool _trafficSpikeEnabled;

    private int _activeTrafficRps;
    private readonly ConcurrentBag<byte[]> _memoryChunks = new();

    private CancellationTokenSource? _cpuCts;
    private CancellationTokenSource? _memoryCts;
    private CancellationTokenSource? _trafficCts;

    private readonly object _cpuLock = new();
    private readonly object _memoryLock = new();
    private readonly object _trafficLock = new();

    public DemoIncidentService(ILogger<DemoIncidentService> logger)
    {
        _logger = logger;
    }

    public bool SlowPaymentEnabled => _slowPaymentEnabled;
    public bool SlowDatabaseEnabled => _slowDatabaseEnabled;
    public bool PaymentErrorEnabled => _paymentErrorEnabled;
    public bool DatabaseErrorEnabled => _databaseErrorEnabled;
    public bool ExternalApiErrorEnabled => _externalApiErrorEnabled;
    public bool HighCpuEnabled => _highCpuEnabled;
    public bool HighMemoryEnabled => _highMemoryEnabled;
    public bool TrafficSpikeEnabled => _trafficSpikeEnabled;

    public DemoStatusDto GetStatus()
    {
        long allocatedBytes = 0;
        foreach (var chunk in _memoryChunks)
        {
            allocatedBytes += chunk.Length;
        }

        return new DemoStatusDto
        {
            SlowPaymentEnabled = _slowPaymentEnabled,
            SlowDatabaseEnabled = _slowDatabaseEnabled,
            PaymentErrorEnabled = _paymentErrorEnabled,
            DatabaseErrorEnabled = _databaseErrorEnabled,
            ExternalApiErrorEnabled = _externalApiErrorEnabled,
            HighCpuEnabled = _highCpuEnabled,
            HighMemoryEnabled = _highMemoryEnabled,
            TrafficSpikeEnabled = _trafficSpikeEnabled,
            ActiveTrafficRps = _activeTrafficRps,
            AllocatedMemoryBytes = allocatedBytes,
            LastUpdatedUtc = DateTime.UtcNow
        };
    }

    public void ResetAll()
    {
        _logger.LogInformation("DemoIncident: Resetting all active incidents to clean baseline");

        _slowPaymentEnabled = false;
        _slowDatabaseEnabled = false;
        _paymentErrorEnabled = false;
        _databaseErrorEnabled = false;
        _externalApiErrorEnabled = false;

        StopHighCpu();
        StopHighMemory();
        StopTrafficSpike();
    }

    public void SetSlowPayment(bool enabled)
    {
        _slowPaymentEnabled = enabled;
        _logger.LogInformation("DemoIncident: SlowPayment set to {Enabled}", enabled);
    }

    public void SetSlowDatabase(bool enabled)
    {
        _slowDatabaseEnabled = enabled;
        _logger.LogInformation("DemoIncident: SlowDatabase set to {Enabled}", enabled);
    }

    public void SetPaymentError(bool enabled)
    {
        _paymentErrorEnabled = enabled;
        _logger.LogInformation("DemoIncident: PaymentError set to {Enabled}", enabled);
    }

    public void SetDatabaseError(bool enabled)
    {
        _databaseErrorEnabled = enabled;
        _logger.LogInformation("DemoIncident: DatabaseError set to {Enabled}", enabled);
    }

    public void SetExternalApiError(bool enabled)
    {
        _externalApiErrorEnabled = enabled;
        _logger.LogInformation("DemoIncident: ExternalApiError set to {Enabled}", enabled);
    }

    public Task StartHighCpuAsync(CancellationToken cancellationToken = default)
    {
        lock (_cpuLock)
        {
            if (_highCpuEnabled) return Task.CompletedTask;

            _highCpuEnabled = true;
            _cpuCts = new CancellationTokenSource();
            var token = _cpuCts.Token;

            _logger.LogWarning("DemoIncident: Starting High CPU simulation across worker threads");
            ObservabilityMetrics.DemoIncidentTotal.Add(1, new KeyValuePair<string, object?>("incident_type", "HighCpuStarted"));

            var threadCount = Math.Max(1, Math.Min(Environment.ProcessorCount - 1, 4));
            for (int i = 0; i < threadCount; i++)
            {
                Task.Run(() =>
                {
                    var sw = new Stopwatch();
                    while (!token.IsCancellationRequested)
                    {
                        sw.Restart();
                        // Spin bounded calculations for ~90ms then sleep 10ms so app stays responsive
                        while (sw.ElapsedMilliseconds < 90 && !token.IsCancellationRequested)
                        {
                            _ = Math.Sqrt(Random.Shared.NextDouble() * 1000000);
                        }
                        Thread.Sleep(10);
                    }
                }, token);
            }
        }

        return Task.CompletedTask;
    }

    public void StopHighCpu()
    {
        lock (_cpuLock)
        {
            if (!_highCpuEnabled) return;

            _highCpuEnabled = false;
            _cpuCts?.Cancel();
            _cpuCts?.Dispose();
            _cpuCts = null;

            _logger.LogInformation("DemoIncident: Stopped High CPU simulation");
        }
    }

    public Task StartHighMemoryAsync(CancellationToken cancellationToken = default)
    {
        lock (_memoryLock)
        {
            if (_highMemoryEnabled) return Task.CompletedTask;

            _highMemoryEnabled = true;
            _memoryCts = new CancellationTokenSource();
            var token = _memoryCts.Token;

            _logger.LogWarning("DemoIncident: Starting Bounded High Memory Allocation simulation");
            ObservabilityMetrics.DemoIncidentTotal.Add(1, new KeyValuePair<string, object?>("incident_type", "HighMemoryStarted"));

            Task.Run(async () =>
            {
                try
                {
                    // Incrementally allocate up to ~180MB in chunks to safely show memory ascent
                    for (int i = 0; i < 18 && !token.IsCancellationRequested; i++)
                    {
                        var buffer = new byte[10 * 1024 * 1024]; // 10 MB chunk
                        Random.Shared.NextBytes(buffer); // dirty memory pages
                        _memoryChunks.Add(buffer);
                        await Task.Delay(500, token);
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        return Task.CompletedTask;
    }

    public void StopHighMemory()
    {
        lock (_memoryLock)
        {
            if (!_highMemoryEnabled) return;

            _highMemoryEnabled = false;
            _memoryCts?.Cancel();
            _memoryCts?.Dispose();
            _memoryCts = null;

            while (_memoryChunks.TryTake(out _)) { }

            // Trigger garbage collection to immediately free the allocated memory in metrics
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();

            _logger.LogInformation("DemoIncident: Stopped High Memory simulation and released all allocated buffers");
        }
    }

    public Task StartTrafficSpikeAsync(int rps, int durationSeconds, Func<CancellationToken, Task> sendRequestAction, CancellationToken cancellationToken = default)
    {
        lock (_trafficLock)
        {
            StopTrafficSpike();

            _trafficSpikeEnabled = true;
            _activeTrafficRps = Math.Clamp(rps, 1, 300);
            var safeDuration = Math.Clamp(durationSeconds, 1, 120);

            _trafficCts = new CancellationTokenSource();
            var token = _trafficCts.Token;

            _logger.LogWarning("DemoIncident: Starting Traffic Spike at {Rps} RPS for {DurationSeconds}s", _activeTrafficRps, safeDuration);
            ObservabilityMetrics.DemoIncidentTotal.Add(1, new KeyValuePair<string, object?>("incident_type", "TrafficSpikeStarted"));

            Task.Run(async () =>
            {
                var stopTime = DateTime.UtcNow.AddSeconds(safeDuration);
                var delayBetweenRequestsMs = Math.Max(1, 1000 / _activeTrafficRps);

                while (DateTime.UtcNow < stopTime && !token.IsCancellationRequested)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await sendRequestAction(token);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("TrafficSpike request failed: {Message}", ex.Message);
                        }
                    }, token);

                    await Task.Delay(delayBetweenRequestsMs, token).ConfigureAwait(false);
                }

                StopTrafficSpike();
            }, token);
        }

        return Task.CompletedTask;
    }

    public void StopTrafficSpike()
    {
        lock (_trafficLock)
        {
            if (!_trafficSpikeEnabled) return;

            _trafficSpikeEnabled = false;
            _activeTrafficRps = 0;
            _trafficCts?.Cancel();
            _trafficCts?.Dispose();
            _trafficCts = null;

            _logger.LogInformation("DemoIncident: Stopped Traffic Spike generator");
        }
    }
}
