using System.Diagnostics;
using System.Runtime.InteropServices;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Spatial;
using UltraSim.Network;

internal static class BotMonotonicClock
{
    internal static long AddMilliseconds(long timestamp, long milliseconds) =>
        checked(timestamp + MillisecondsToTicks(milliseconds));

    internal static long MillisecondsToTicks(long milliseconds) =>
        MillisecondsToTicks(milliseconds, Stopwatch.Frequency);

    internal static long MillisecondsToTicks(long milliseconds, long frequency)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        Int128 result = (Int128)milliseconds * frequency / 1_000;
        return checked((long)result);
    }

    internal static long TicksToMilliseconds(long ticks) =>
        TicksToMilliseconds(ticks, Stopwatch.Frequency);

    internal static long TicksToMilliseconds(long ticks, long frequency)
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        Int128 result = (Int128)ticks * 1_000 / frequency;
        return checked((long)result);
    }

    internal static double TicksToFloatingMilliseconds(long ticks) =>
        ticks * 1_000d / Stopwatch.Frequency;

    /// <summary>
    /// Advances one fixed-rate deadline without rebasing the cadence phase on a late pump.
    /// Missed producer opportunities are coalesced rather than synthesized.
    /// </summary>
    internal static long AdvanceFixedDeadline(long deadline, long now, long intervalTicks)
    {
        if (deadline < 0) throw new ArgumentOutOfRangeException(nameof(deadline));
        if (now < 0) throw new ArgumentOutOfRangeException(nameof(now));
        if (intervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(intervalTicks));
        if (now < deadline) return deadline;
        long intervals = checked((now - deadline) / intervalTicks + 1);
        return checked(deadline + intervals * intervalTicks);
    }
}

internal readonly record struct BotCumulativeTrafficSnapshot(
    ConnectionId CurrentConnection,
    ulong CurrentApplicationSessionEpoch,
    int ApplicationLifetimes,
    UdpPayloadTrafficSnapshot UdpPayload,
    NetworkTrafficSnapshot? SecureProtocol);

internal static class BotScenarioContract
{
    internal const int MinimumMixedChaosBots = 8;
    internal const int MinimumMixedChaosDurationMilliseconds = 10_000;
    internal const int MaximumInitialAdmissionMilliseconds = 90_000;

    internal static void ValidateMixedChaos(int botCount, int durationMilliseconds)
    {
        if (botCount < MinimumMixedChaosBots)
            throw new ArgumentException(
                $"mixed-chaos requires at least {MinimumMixedChaosBots} bots so every deterministic acceptance role exists.");
        if (durationMilliseconds < MinimumMixedChaosDurationMilliseconds)
            throw new ArgumentException(
                $"mixed-chaos requires at least {MinimumMixedChaosDurationMilliseconds / 1_000} seconds for its reconnect and edge roles.");
    }

    internal static long AdmissionDeadline(
        long created,
        int botCount,
        int admissionStaggerMilliseconds)
    {
        if (botCount <= 0) throw new ArgumentOutOfRangeException(nameof(botCount));
        if (admissionStaggerMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(admissionStaggerMilliseconds));
        long lastScheduledStart = checked((long)(botCount - 1) * admissionStaggerMilliseconds);
        long perSwarmWindow = Math.Max(
            MaximumInitialAdmissionMilliseconds,
            checked((long)botCount * 1_000));
        return BotMonotonicClock.AddMilliseconds(
            created, checked(lastScheduledStart + perSwarmWindow));
    }
}

internal enum BotFinalRetirementAction : byte
{
    None = 0,
    RetireWithoutConnection = 1,
    BeginOrContinueDrain = 2,
}

internal static class BotLifecyclePolicy
{
    internal static BotFinalRetirementAction FinalRetirement(BotLifetimeState state) => state switch
    {
        BotLifetimeState.Pending or BotLifetimeState.ReconnectDelay =>
            BotFinalRetirementAction.RetireWithoutConnection,
        BotLifetimeState.Connecting or BotLifetimeState.Profile or BotLifetimeState.Content or
            BotLifetimeState.Active or BotLifetimeState.Draining =>
            BotFinalRetirementAction.BeginOrContinueDrain,
        BotLifetimeState.Retired or BotLifetimeState.Faulted => BotFinalRetirementAction.None,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    internal static bool IsUnexpectedTransportLoss(
        BotLifetimeState lifetime,
        ClientDirectConnectState connection)
    {
        if (lifetime == BotLifetimeState.Draining)
            return false;
        if (connection is ClientDirectConnectState.Draining or ClientDirectConnectState.Closed)
            return true;
        return (lifetime is BotLifetimeState.Profile or BotLifetimeState.Content or
                BotLifetimeState.Active) && connection != ClientDirectConnectState.Established;
    }

    internal static bool IsApplicationUnhealthy(
        BotLifetimeState lifetime,
        in RemotePlayableSessionSnapshot snapshot)
    {
        bool activeExpected = lifetime == BotLifetimeState.Active;
        if (!activeExpected && !snapshot.SemanticConsumersActive) return false;
        return !snapshot.SemanticConsumersActive ||
            snapshot.Playable.State != RemotePlayableClientState.Active ||
            snapshot.Ui.State != RemoteClientApplicationState.Active;
    }
}

internal readonly record struct BotPendingCommand(
    BotCommandKind Kind,
    ulong Sequence,
    ulong ApplicationSessionEpoch)
{
    internal bool IsPending => Kind != BotCommandKind.None;
}

internal enum BotActionAccountingModel : byte
{
    LatestStateToSubmission = 1,
    EdgeToAuthorityCompletion = 2,
}

internal readonly record struct BotActionAccountingSnapshot(
    BotCommandKind Kind,
    BotActionAccountingModel Model,
    ulong Generated,
    ulong SchedulerAdmitted,
    ulong SchedulerCapacityRejected,
    ulong NotAdmittedNoNewEdge,
    ulong NotAdmittedUnavailableOrInvalid,
    ulong Submitted,
    ulong SemanticallyCompleted,
    ulong Accepted,
    ulong Rejected,
    ulong RateLimited,
    ulong CompletionNotTrackedLatestState,
    ulong RetiredCompletionDebt);

/// <summary>
/// Measurement-window source-to-terminal accounting. Movement is latest-state and conserves at
/// scheduler submission; one-shot actions conserve through the authoritative processed sequence.
/// </summary>
internal sealed class BotActionAccounting
{
    private readonly Counter[] _counters = new Counter[6];

    internal void Reset() => Array.Clear(_counters);

    internal void Generated(BotCommandKind kind) => Increment(kind, static (ref Counter c) => c.Generated++);
    internal void Admitted(BotCommandKind kind) => Increment(kind, static (ref Counter c) => c.SchedulerAdmitted++);
    internal void CapacityRejected(BotCommandKind kind) => Increment(kind, static (ref Counter c) => c.SchedulerCapacityRejected++);
    internal void NoNewEdge(BotCommandKind kind) => Increment(kind, static (ref Counter c) => c.NotAdmittedNoNewEdge++);
    internal void UnavailableOrInvalid(BotCommandKind kind) => Increment(kind, static (ref Counter c) => c.NotAdmittedUnavailableOrInvalid++);
    internal void Submitted(BotCommandKind kind, bool completionTracked)
    {
        if (kind == BotCommandKind.None) return;
        ref Counter counter = ref Resolve(kind);
        counter.Submitted++;
        if (!completionTracked) counter.CompletionNotTrackedLatestState++;
    }

    internal void Completed(BotCommandKind kind, RemotePlayableActionStatus result)
    {
        ref Counter counter = ref Resolve(kind);
        counter.SemanticallyCompleted++;
        if (result == RemotePlayableActionStatus.Accepted) counter.Accepted++;
        else if (result == RemotePlayableActionStatus.RateLimited) counter.RateLimited++;
        else counter.Rejected++;
    }

    internal void RetiredCompletionDebt(BotCommandKind kind) =>
        Increment(kind, static (ref Counter c) => c.RetiredCompletionDebt++);

    internal BotActionAccountingSnapshot Snapshot(BotCommandKind kind)
    {
        ref Counter counter = ref Resolve(kind);
        return new BotActionAccountingSnapshot(
            kind,
            kind == BotCommandKind.Movement
                ? BotActionAccountingModel.LatestStateToSubmission
                : BotActionAccountingModel.EdgeToAuthorityCompletion,
            counter.Generated,
            counter.SchedulerAdmitted,
            counter.SchedulerCapacityRejected,
            counter.NotAdmittedNoNewEdge,
            counter.NotAdmittedUnavailableOrInvalid,
            counter.Submitted,
            counter.SemanticallyCompleted,
            counter.Accepted,
            counter.Rejected,
            counter.RateLimited,
            counter.CompletionNotTrackedLatestState,
            counter.RetiredCompletionDebt);
    }

    private delegate void CounterMutation(ref Counter counter);

    private void Increment(BotCommandKind kind, CounterMutation mutation)
    {
        if (kind == BotCommandKind.None) return;
        mutation(ref Resolve(kind));
    }

    private ref Counter Resolve(BotCommandKind kind)
    {
        int index = (int)kind;
        if ((uint)index >= (uint)_counters.Length || kind == BotCommandKind.None)
            throw new ArgumentOutOfRangeException(nameof(kind));
        return ref _counters[index];
    }

    private struct Counter
    {
        internal ulong Generated;
        internal ulong SchedulerAdmitted;
        internal ulong SchedulerCapacityRejected;
        internal ulong NotAdmittedNoNewEdge;
        internal ulong NotAdmittedUnavailableOrInvalid;
        internal ulong Submitted;
        internal ulong SemanticallyCompleted;
        internal ulong Accepted;
        internal ulong Rejected;
        internal ulong RateLimited;
        internal ulong CompletionNotTrackedLatestState;
        internal ulong RetiredCompletionDebt;
    }
}

internal sealed class BotPendingCommandTracker
{
    internal BotPendingCommand Pending { get; private set; }
    internal bool HasPendingExactCompletion =>
        Pending.Kind is not (BotCommandKind.None or BotCommandKind.Movement);

    internal bool TryStart(BotCommandKind kind, ulong sequence, ulong applicationSessionEpoch)
    {
        if (kind is BotCommandKind.None or BotCommandKind.Movement ||
            sequence == 0 || applicationSessionEpoch == 0 ||
            Pending.IsPending)
            return false;
        Pending = new BotPendingCommand(kind, sequence, applicationSessionEpoch);
        return true;
    }

    internal bool TryStartOrSupersedeLatestMovement(
        ulong sequence,
        ulong applicationSessionEpoch)
    {
        if (sequence == 0 || applicationSessionEpoch == 0 ||
            (Pending.IsPending && Pending.Kind != BotCommandKind.Movement) ||
            (Pending.Kind == BotCommandKind.Movement &&
                (Pending.ApplicationSessionEpoch != applicationSessionEpoch ||
                 sequence <= Pending.Sequence)))
            return false;
        Pending = new BotPendingCommand(
            BotCommandKind.Movement, sequence, applicationSessionEpoch);
        return true;
    }

    internal bool TryRetireLatestMovement(out BotPendingCommand retired)
    {
        retired = Pending;
        if (Pending.Kind != BotCommandKind.Movement) return false;
        Pending = default;
        return true;
    }

    internal bool TryComplete(
        ulong applicationSessionEpoch,
        ulong lastProcessedSequence,
        out BotPendingCommand completed)
    {
        completed = default;
        if (!Pending.IsPending || Pending.ApplicationSessionEpoch != applicationSessionEpoch ||
            lastProcessedSequence < Pending.Sequence)
            return false;
        completed = Pending;
        Pending = default;
        return true;
    }

    internal bool TryCancel(out BotPendingCommand cancelled)
    {
        cancelled = Pending;
        if (!Pending.IsPending) return false;
        Pending = default;
        return true;
    }

    internal void RequireClearForNewLifetime()
    {
        if (Pending.IsPending)
            throw new InvalidOperationException(
                "A new application lifetime cannot replace an unaccounted command outcome.");
    }
}

internal struct BotChatObservationCursor
{
    private ulong _applicationSessionEpoch;
    private ulong _nextRecipientSequence;

    internal ulong ObservationGaps { get; private set; }
    internal int RetainedCursorEntries => _applicationSessionEpoch == 0 ? 0 : 1;

    internal void Observe(
        ulong applicationSessionEpoch,
        ReadOnlySpan<ClientChatOccurrence> occurrences,
        string acceptedPrefix,
        out int local,
        out int global)
    {
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        ArgumentNullException.ThrowIfNull(acceptedPrefix);
        if (_applicationSessionEpoch != applicationSessionEpoch)
        {
            _applicationSessionEpoch = applicationSessionEpoch;
            _nextRecipientSequence = 1;
        }
        local = 0;
        global = 0;
        for (int index = 0; index < occurrences.Length; index++)
        {
            ref readonly ClientChatOccurrence occurrence = ref occurrences[index];
            if (occurrence.RecipientSequence < _nextRecipientSequence) continue;
            if (occurrence.RecipientSequence > _nextRecipientSequence)
                ObservationGaps = SaturatingAdd(
                    ObservationGaps, occurrence.RecipientSequence - _nextRecipientSequence);
            _nextRecipientSequence = occurrence.RecipientSequence == ulong.MaxValue
                ? ulong.MaxValue
                : occurrence.RecipientSequence + 1;
            if (!occurrence.Text.StartsWith(acceptedPrefix, StringComparison.Ordinal)) continue;
            if (occurrence.IsGlobal) global++;
            else if (occurrence.IsSpatial) local++;
        }
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

internal sealed class BotChunkObservation
{
    private readonly HashSet<SpatialChunkCoord> _distinct = [];
    private readonly int _maximumDistinct;
    private SpatialChunkCoord _last;
    private bool _hasLast;

    internal BotChunkObservation(int maximumDistinct)
    {
        if (maximumDistinct <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDistinct));
        _maximumDistinct = maximumDistinct;
    }

    internal int DistinctChunks => _distinct.Count;
    internal int Crossings { get; private set; }
    internal bool DistinctCoverageSaturated { get; private set; }

    internal void Observe(SpatialChunkCoord chunk)
    {
        if (_hasLast && chunk != _last) Crossings++;
        _last = chunk;
        _hasLast = true;
        if (_distinct.Contains(chunk)) return;
        if (_distinct.Count < _maximumDistinct) _distinct.Add(chunk);
        else DistinctCoverageSaturated = true;
    }
}

internal sealed class BotReconnectIdentityObservation
{
    internal bool OldIdentityObservedBeforeReconnect { get; private set; }
    internal bool OldIdentityRetiredAfterReconnect { get; private set; }
    internal bool ReplacementIdentityObserved { get; private set; }
    internal bool StaleOldIdentityObserved { get; private set; }
    internal bool Converged => OldIdentityObservedBeforeReconnect &&
        OldIdentityRetiredAfterReconnect && ReplacementIdentityObserved &&
        !StaleOldIdentityObserved;

    internal void ObserveBefore(bool oldPresent) =>
        OldIdentityObservedBeforeReconnect |= oldPresent;

    internal void ObserveAfter(bool oldPresent, bool replacementPresent)
    {
        StaleOldIdentityObserved |= oldPresent;
        OldIdentityRetiredAfterReconnect |= !oldPresent;
        ReplacementIdentityObserved |= replacementPresent;
    }
}

internal readonly record struct BotProcessSample(
    long WorkingSetBytes,
    double CpuMilliseconds,
    long ManagedHeapBytes = 0,
    long TotalAllocatedBytes = 0,
    int Gen0Collections = 0,
    int Gen1Collections = 0,
    int Gen2Collections = 0,
    int ThreadCount = 0);

internal interface IBotProcessSampleSource : IDisposable
{
    BotProcessSample ReadFresh();
}

internal sealed class CurrentBotProcessSampleSource : IBotProcessSampleSource
{
    private readonly Process _process = Process.GetCurrentProcess();

    public BotProcessSample ReadFresh()
    {
        _process.Refresh();
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        return new BotProcessSample(
            _process.WorkingSet64,
            _process.TotalProcessorTime.TotalMilliseconds,
            memory.HeapSizeBytes,
            GC.GetTotalAllocatedBytes(precise: false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            _process.Threads.Count);
    }

    public void Dispose() => _process.Dispose();
}

internal sealed class BotProcessTelemetrySampler : IDisposable
{
    private readonly IBotProcessSampleSource _source;
    private readonly long _intervalTicks;
    private long _nextSample;
    private bool _disposed;

    internal BotProcessTelemetrySampler(
        IBotProcessSampleSource source,
        int intervalMilliseconds = 250)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (intervalMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalMilliseconds));
        _intervalTicks = BotMonotonicClock.MillisecondsToTicks(intervalMilliseconds);
    }

    internal long WorkingSetHighWaterBytes { get; private set; }
    internal double CpuMilliseconds { get; private set; }
    internal int Samples { get; private set; }
    internal long WindowWorkingSetHighWaterBytes { get; private set; }
    internal double WindowCpuMilliseconds { get; private set; }
    internal int WindowSamples { get; private set; }
    internal long WindowManagedHeapHighWaterBytes { get; private set; }
    internal long WindowAllocatedBytes { get; private set; }
    internal int WindowGen0Collections { get; private set; }
    internal int WindowGen1Collections { get; private set; }
    internal int WindowGen2Collections { get; private set; }
    internal int WindowThreadHighWater { get; private set; }
    private double _windowCpuStart;
    private long _windowAllocatedStart;
    private int _windowGen0Start;
    private int _windowGen1Start;
    private int _windowGen2Start;
    private bool _windowActive;

    internal void BeginWindow(long now)
    {
        BotProcessSample sample = _source.ReadFresh();
        _windowCpuStart = sample.CpuMilliseconds;
        _windowAllocatedStart = sample.TotalAllocatedBytes;
        _windowGen0Start = sample.Gen0Collections;
        _windowGen1Start = sample.Gen1Collections;
        _windowGen2Start = sample.Gen2Collections;
        WindowCpuMilliseconds = 0;
        WindowWorkingSetHighWaterBytes = sample.WorkingSetBytes;
        WindowManagedHeapHighWaterBytes = sample.ManagedHeapBytes;
        WindowAllocatedBytes = 0;
        WindowGen0Collections = WindowGen1Collections = WindowGen2Collections = 0;
        WindowThreadHighWater = sample.ThreadCount;
        WindowSamples = 1;
        _windowActive = true;
        WorkingSetHighWaterBytes = Math.Max(WorkingSetHighWaterBytes, sample.WorkingSetBytes);
        CpuMilliseconds = sample.CpuMilliseconds;
        Samples++;
        _nextSample = checked(now + _intervalTicks);
    }

    internal void EndWindow(long now)
    {
        Sample(now, force: true);
        _windowActive = false;
    }

    internal void Sample(long now, bool force = false)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BotProcessTelemetrySampler));
        if (!force && Samples != 0 && now < _nextSample) return;
        BotProcessSample sample = _source.ReadFresh();
        WorkingSetHighWaterBytes = Math.Max(WorkingSetHighWaterBytes, sample.WorkingSetBytes);
        CpuMilliseconds = sample.CpuMilliseconds;
        Samples++;
        if (_windowActive)
        {
            WindowWorkingSetHighWaterBytes = Math.Max(
                WindowWorkingSetHighWaterBytes, sample.WorkingSetBytes);
            WindowManagedHeapHighWaterBytes = Math.Max(
                WindowManagedHeapHighWaterBytes, sample.ManagedHeapBytes);
            WindowAllocatedBytes = Math.Max(0, sample.TotalAllocatedBytes - _windowAllocatedStart);
            WindowGen0Collections = Math.Max(0, sample.Gen0Collections - _windowGen0Start);
            WindowGen1Collections = Math.Max(0, sample.Gen1Collections - _windowGen1Start);
            WindowGen2Collections = Math.Max(0, sample.Gen2Collections - _windowGen2Start);
            WindowThreadHighWater = Math.Max(WindowThreadHighWater, sample.ThreadCount);
            WindowCpuMilliseconds = Math.Max(0, sample.CpuMilliseconds - _windowCpuStart);
            WindowSamples++;
        }
        _nextSample = checked(now + _intervalTicks);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _source.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Fixed owner-local latency histogram for one measured bot-pump window. The
/// 0.05 ms buckets cover 204.8 ms and the separate maximum preserves outliers.
/// </summary>
internal sealed class BotLatencyHistogram
{
    private const int BucketCount = 4_096;
    private const double BucketMilliseconds = 0.05d;
    private readonly ulong[] _buckets = new ulong[BucketCount];

    internal ulong Count { get; private set; }
    internal double MaximumMilliseconds { get; private set; }

    internal void Clear()
    {
        Array.Clear(_buckets);
        Count = 0;
        MaximumMilliseconds = 0;
    }

    internal void RecordTicks(long ticks)
    {
        double milliseconds = BotMonotonicClock.TicksToFloatingMilliseconds(
            Math.Max(0, ticks));
        double scaled = Math.Floor(milliseconds / BucketMilliseconds);
        int bucket = scaled >= BucketCount - 1
            ? BucketCount - 1
            : scaled <= 0
                ? 0
                : (int)scaled;
        if (_buckets[bucket] != ulong.MaxValue) _buckets[bucket]++;
        if (Count != ulong.MaxValue) Count++;
        MaximumMilliseconds = Math.Max(MaximumMilliseconds, milliseconds);
    }

    internal double Percentile(double percentile)
    {
        if (percentile is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(percentile));
        if (Count == 0) return 0;
        ulong target = Math.Max(1, checked((ulong)Math.Ceiling(Count * percentile)));
        ulong cumulative = 0;
        for (int index = 0; index < _buckets.Length; index++)
        {
            cumulative = ulong.MaxValue - cumulative < _buckets[index]
                ? ulong.MaxValue
                : cumulative + _buckets[index];
            if (cumulative >= target)
                return (index + 1) * BucketMilliseconds;
        }
        return MaximumMilliseconds;
    }
}

internal static class BotRuntimeIdentity
{
    internal static string BuildModule =>
        typeof(BotRuntimeIdentity).Assembly.ManifestModule.ModuleVersionId.ToString("D");
    internal static string Runtime => RuntimeInformation.FrameworkDescription;
    internal static string OperatingSystem => RuntimeInformation.OSDescription;
    internal static string Architecture => RuntimeInformation.ProcessArchitecture.ToString();
}
