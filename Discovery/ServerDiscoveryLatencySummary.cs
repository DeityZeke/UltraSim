namespace UltraSim.Discovery
{
    public sealed record ServerDiscoveryLatencySummary(
        int SampleCount = 0,
        double AverageLatencyMs = 0,
        int MaxLatencyMs = 0,
        int? LastLatencyMs = null,
        int FailureCount = 0);
}
