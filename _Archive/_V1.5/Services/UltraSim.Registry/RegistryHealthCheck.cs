using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UltraSim.Registry;

internal sealed class RegistryHealthCheck : IHealthCheck
{
    private readonly InMemoryOfficialRegistryStore _store;
    private readonly RegistryRuntimeMetrics _metrics;

    public RegistryHealthCheck(InMemoryOfficialRegistryStore store, RegistryRuntimeMetrics metrics)
    {
        _store = store;
        _metrics = metrics;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        RegistryStoreRuntimeSnapshot store = _store.GetRuntimeSnapshot(DateTimeOffset.UtcNow);
        RegistryRuntimeMetricsSnapshot metrics = _metrics.GetSnapshot();

        IReadOnlyDictionary<string, object> data = new Dictionary<string, object>
        {
            ["totalRecords"] = store.TotalRecords,
            ["activeRecords"] = store.ActiveRecords,
            ["onlineRecords"] = store.OnlineRecords,
            ["activeLeases"] = store.ActiveLeases,
            ["pendingPersistence"] = store.PendingPersistence,
            ["saveStateCalls"] = metrics.SaveStateCalls,
            ["saveStateFailures"] = metrics.SaveStateFailures,
        };

        if (store.PersistenceEnabled &&
            metrics.SaveStateCalls > 0 &&
            metrics.SaveStateFailures == metrics.SaveStateCalls)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "All state persistence attempts have failed.", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Store is responsive.", data));
    }
}
