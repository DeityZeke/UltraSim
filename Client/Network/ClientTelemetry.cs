using System.Diagnostics;
using System.Numerics;

namespace UltraSim.Client.Network;

public readonly record struct ClientTimingDistribution(
    ulong Count,
    ulong TotalStopwatchTicks,
    long P50Microseconds,
    long P95Microseconds,
    long P99Microseconds,
    long MaximumMicroseconds,
    int RollingSamples,
    long WholeWindowP50Microseconds,
    long WholeWindowP95Microseconds,
    long WholeWindowP99Microseconds);

/// <summary>
/// Bounded unsigned-value distribution. Recent percentiles are exact across the most recent 256
/// observations; whole-window percentiles are conservative power-of-two bucket upper bounds.
/// </summary>
public readonly record struct ClientValueDistribution(
    ulong Count,
    ulong Total,
    double Average,
    ulong P50,
    ulong P95,
    ulong P99,
    ulong Maximum,
    int RollingSamples,
    ulong WholeWindowP50UpperBound,
    ulong WholeWindowP95UpperBound,
    ulong WholeWindowP99UpperBound);

internal sealed class ClientValueWindow
{
    private const int MaximumSamples = 256;
    private readonly ulong[] _samples = new ulong[MaximumSamples];
    private readonly ulong[] _wholeBuckets = new ulong[65];
    private int _next;
    private int _count;
    private ulong _observations;
    private ulong _total;
    private ulong _maximum;

    internal void Record(ulong value)
    {
        _samples[_next] = value;
        _next = (_next + 1) % _samples.Length;
        if (_count < _samples.Length) _count++;
        if (_observations != ulong.MaxValue) _observations++;
        _total = ulong.MaxValue - _total < value ? ulong.MaxValue : _total + value;
        _maximum = Math.Max(_maximum, value);
        int bucket = value == 0 ? 0 : Math.Min(64, BitOperations.Log2(value) + 1);
        if (_wholeBuckets[bucket] != ulong.MaxValue) _wholeBuckets[bucket]++;
    }

    internal ClientValueDistribution Capture()
    {
        if (_count == 0) return default;
        Span<ulong> ordered = stackalloc ulong[MaximumSamples];
        _samples.AsSpan(0, _count).CopyTo(ordered);
        ordered[.._count].Sort();
        return new(
            _observations,
            _total,
            _observations == 0 ? 0d : (double)_total / _observations,
            ordered[Index(50)], ordered[Index(95)], ordered[Index(99)], _maximum, _count,
            WholePercentile(50), WholePercentile(95), WholePercentile(99));
    }

    internal void Clear()
    {
        Array.Clear(_samples);
        Array.Clear(_wholeBuckets);
        _next = _count = 0;
        _observations = _total = _maximum = 0;
    }

    private int Index(int percentage) => Math.Clamp(
        (int)(((long)_count * percentage + 99) / 100) - 1, 0, _count - 1);

    private ulong WholePercentile(int percentage)
    {
        if (_observations == 0) return 0;
        ulong target = (ulong)(((UInt128)_observations * (uint)percentage + 99) / 100);
        ulong cumulative = 0;
        for (int index = 0; index < _wholeBuckets.Length; index++)
        {
            cumulative = ulong.MaxValue - cumulative < _wholeBuckets[index]
                ? ulong.MaxValue
                : cumulative + _wholeBuckets[index];
            if (cumulative < target) continue;
            if (index == 0) return 0;
            ulong upper = index == 64 ? ulong.MaxValue : (1UL << index) - 1;
            return Math.Min(upper, _maximum);
        }
        return _maximum;
    }
}

internal sealed class ClientTimingWindow
{
    internal const int MaximumSamples = 256;
    private readonly long[] _samples = new long[MaximumSamples];
    private readonly ulong[] _wholeBuckets = new ulong[64];
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
        int bucket = microseconds == 0 ? 0 : Math.Min(63,
            BitOperations.Log2(checked((ulong)microseconds)) + 1);
        if (_wholeBuckets[bucket] != ulong.MaxValue) _wholeBuckets[bucket]++;
    }

    internal ClientTimingDistribution Capture()
    {
        if (_count == 0) return default;
        Span<long> ordered = stackalloc long[MaximumSamples];
        _samples.AsSpan(0, _count).CopyTo(ordered);
        ordered[.._count].Sort();
        return new(_observations, _total,
            ordered[Index(0.50)], ordered[Index(0.95)], ordered[Index(0.99)], _maximum,
            _count, WholePercentile(50), WholePercentile(95), WholePercentile(99));
    }

    internal void Clear()
    {
        Array.Clear(_samples);
        Array.Clear(_wholeBuckets);
        _next = _count = 0;
        _observations = 0;
        _total = 0;
        _maximum = 0;
    }

    private int Index(double percentile) => Math.Clamp(
        (int)Math.Ceiling(_count * percentile) - 1, 0, _count - 1);

    private long WholePercentile(int percentage)
    {
        if (_observations == 0) return 0;
        ulong target = (ulong)(((UInt128)_observations * (uint)percentage + 99) / 100);
        ulong cumulative = 0;
        for (int index = 0; index < _wholeBuckets.Length; index++)
        {
            cumulative = ulong.MaxValue - cumulative < _wholeBuckets[index]
                ? ulong.MaxValue
                : cumulative + _wholeBuckets[index];
            if (cumulative < target) continue;
            if (index == 0) return 0;
            ulong upper = index == 63 ? ulong.MaxValue : (1UL << index) - 1;
            return checked((long)Math.Min(upper, checked((ulong)_maximum)));
        }
        return _maximum;
    }
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
    ClientTimingDistribution ProofDrain,
    RemoteChunkProofCensusSnapshot ProofCensus,
    RemoteChunkDeliveryCensusSnapshot DeliveryCensus);

/// <summary>Window-local PF4B outcomes for received chunk-delivery semantics.</summary>
public readonly record struct RemoteChunkDeliveryCensusSnapshot(
    ulong AppliedDeltas,
    ulong AppliedSnapshots,
    ulong AppliedAbsences,
    ulong DuplicateDeliveries,
    ulong SupersededDeliveries,
    ulong RejectedDeliveries,
    ulong CapacityFailures,
    ulong ContradictoryDeliveries,
    ulong MalformedDeliveries);

/// <summary>One bounded PF4B client-side census of semantic proof production and packing.</summary>
public readonly record struct RemoteChunkProofCensusSnapshot(
    ulong EntriesGenerated,
    ulong EntriesQueued,
    ulong EnvelopesQueued,
    ulong EnvelopeApplicationPayloadBytes,
    ulong QueueDeferrals,
    ulong Applied,
    ulong Rejected,
    ulong CapacityExceeded,
    ulong Contradictory,
    ulong Delta,
    ulong Snapshot,
    ulong Absence,
    ulong PositiveSubsumptionOpportunities,
    ulong FullEnvelopes,
    ulong SingleEntryEnvelopes,
    int QueueCurrent,
    int QueueHighWater,
    int QueueCapacity,
    int MaximumEnvelopeEntries,
    ClientValueDistribution EntriesPerEnvelope,
    ClientTimingDistribution GenerationToEnvelopeQueue);

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
