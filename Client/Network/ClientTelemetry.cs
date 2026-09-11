using System.Diagnostics;

namespace UltraSim.Client.Network;

public readonly record struct ClientTimingDistribution(
    ulong Count,
    ulong TotalStopwatchTicks,
    long P50Microseconds,
    long P95Microseconds,
    long P99Microseconds,
    long MaximumMicroseconds);

internal sealed class ClientTimingWindow
{
    internal const int MaximumSamples = 256;
    private readonly long[] _samples = new long[MaximumSamples];
    private int _next;
    private int _count;
    private ulong _observations;
    private ulong _total;
    private long _maximum;

    internal void RecordElapsed(long started) =>
        RecordElapsedAt(started, Stopwatch.GetTimestamp());

    internal void RecordElapsedAt(long started, long completed)
    {
        long ticks = Math.Max(0, completed - started);
        long microseconds = checked((long)((Int128)ticks * 1_000_000 / Stopwatch.Frequency));
        _samples[_next] = microseconds;
        _next = (_next + 1) % _samples.Length;
        if (_count < _samples.Length) _count++;
        if (_observations != ulong.MaxValue) _observations++;
        ulong value = checked((ulong)ticks);
        _total = ulong.MaxValue - _total < value ? ulong.MaxValue : _total + value;
        _maximum = Math.Max(_maximum, microseconds);
    }

    internal ClientTimingDistribution Capture()
    {
        if (_count == 0) return default;
        Span<long> ordered = stackalloc long[MaximumSamples];
        _samples.AsSpan(0, _count).CopyTo(ordered);
        ordered[.._count].Sort();
        return new(_observations, _total,
            ordered[Index(0.50)], ordered[Index(0.95)], ordered[Index(0.99)], _maximum);
    }

    internal void Clear()
    {
        Array.Clear(_samples);
        _next = _count = 0;
        _observations = 0;
        _total = 0;
        _maximum = 0;
    }

    private int Index(double percentile) => Math.Clamp(
        (int)Math.Ceiling(_count * percentile) - 1, 0, _count - 1);
}

public readonly record struct RemotePlayableClientTimingSnapshot(
    UltraSim.Network.NetworkTelemetryLevel Level,
    ulong CommandsSubmitted,
    ulong CommandsSemanticallyCompleted,
    ulong CommandTimingCapacityDrops,
    ulong OwnerStatesReceived,
    ulong OwnerStatesApplied,
    ulong OwnerStateSimulationTickGaps,
    ClientTimingDistribution CommandToOwnerState,
    ClientTimingDistribution OwnerStateApplyIntervals,
    ClientTimingDistribution OwnerStateApplyDuration);

public readonly record struct RemoteChunkWorldStateClientTimingSnapshot(
    UltraSim.Network.NetworkTelemetryLevel Level,
    ClientTimingDistribution DecodeAndApply,
    ClientTimingDistribution ProofDrain);

public readonly record struct RemotePlayableSessionTimingSnapshot(
    UltraSim.Network.NetworkTelemetryLevel Level,
    ClientTimingDistribution ProcessReceived,
    RemotePlayableClientTimingSnapshot Playable,
    RemoteChunkWorldStateClientTimingSnapshot World,
    RemotePublicPresentationClientTimingSnapshot PublicPresentation,
    RemoteChatClientTimingSnapshot Chat,
    RemoteUiClientTimingSnapshot Ui);

public readonly record struct RemotePublicPresentationClientTimingSnapshot(
    UltraSim.Network.NetworkTelemetryLevel Level,
    ClientTimingDistribution Apply,
    ClientTimingDistribution ProofDrain);

public readonly record struct RemoteChatClientTimingSnapshot(
    UltraSim.Network.NetworkTelemetryLevel Level,
    ClientTimingDistribution Apply,
    ClientTimingDistribution ProofDrain);

public readonly record struct RemoteUiClientTimingSnapshot(
    UltraSim.Network.NetworkTelemetryLevel Level,
    ClientTimingDistribution Apply);
