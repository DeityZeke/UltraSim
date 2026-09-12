using System.Diagnostics;
using System.Numerics;
using UltraSim.Game.Input;
using UltraSim.Game.World;

namespace UltraSim.Client.Core;

[Flags]
public enum ClientWorldCommandRequestFlags : byte
{
    None = 0,
    FaceRequested = 1 << 0,
    FacingRequiresAcceptedLocomotion = 1 << 1,
    OpenInventoryRequested = 1 << 2,
    OpenServerMonitorRequested = 1 << 3,
}

public readonly record struct ClientWorldCommandContext(
    WorldPublicEntityId Target,
    ClientWorldCommandRequestFlags Flags,
    long FaceXMillimeters,
    long FaceZMillimeters,
    ulong DiagnosticTag = 0)
{
    private const ClientWorldCommandRequestFlags KnownFlags =
        ClientWorldCommandRequestFlags.FaceRequested |
        ClientWorldCommandRequestFlags.FacingRequiresAcceptedLocomotion |
        ClientWorldCommandRequestFlags.OpenInventoryRequested |
        ClientWorldCommandRequestFlags.OpenServerMonitorRequested;

    public bool IsValid =>
        (Flags & ~KnownFlags) == 0 &&
        ((Flags & ClientWorldCommandRequestFlags.OpenInventoryRequested) == 0 ||
         (Flags & ClientWorldCommandRequestFlags.OpenServerMonitorRequested) == 0);

    public bool IsCanonical => IsValid &&
        ((Flags & ClientWorldCommandRequestFlags.FacingRequiresAcceptedLocomotion) == 0 ||
         (Flags & ClientWorldCommandRequestFlags.FaceRequested) != 0) &&
        ((Flags & ClientWorldCommandRequestFlags.FaceRequested) != 0 ||
         FaceXMillimeters == 0 && FaceZMillimeters == 0);

    internal ClientWorldCommandContext WithoutOneShots() => this with
    {
        Flags = Flags & (ClientWorldCommandRequestFlags.FaceRequested |
            ClientWorldCommandRequestFlags.FacingRequiresAcceptedLocomotion),
    };
}

public enum ClientWorldCommandObligationKind : byte
{
    Continuous = 1,
    ActionEdge = 2,
    ReleaseOrStop = 3,
    OneShot = 4,
}

public readonly record struct ClientWorldScheduledCommand(
    ClientWorldCommandIntent Intent,
    ClientWorldCommandContext Context,
    ClientWorldCommandObligationKind Kind,
    long ObservedTimestamp,
    bool IncludesReleaseOrStop = false);

public delegate bool ClientWorldSubmissionAttempt(
    in ClientWorldScheduledCommand command,
    out ulong sequence);

public readonly record struct ClientInputSubmissionPolicy(
    int MaximumInputHz,
    int RecommendedInputHz,
    int BurstCapacity,
    int BurstWindowMilliseconds,
    int MaximumPendingObligations = 32)
{
    public bool IsValid => MaximumInputHz is > 0 and <= 240 &&
        RecommendedInputHz > 0 && RecommendedInputHz <= MaximumInputHz &&
        BurstCapacity is > 0 and <= 32 &&
        BurstWindowMilliseconds is >= 10 and <= 5_000 &&
        MaximumPendingObligations is > 0 and <= 64 &&
        BurstCapacity >= Math.Max(1,
            ((long)MaximumInputHz * BurstWindowMilliseconds + 999) / 1_000);
}

public readonly record struct ClientInputTimingDistribution(
    ulong Count,
    ulong P50StopwatchTicks,
    ulong P95StopwatchTicks,
    ulong P99StopwatchTicks,
    ulong MaximumStopwatchTicks)
{
    public double P50Milliseconds => P50StopwatchTicks * 1_000d / Stopwatch.Frequency;
    public double P95Milliseconds => P95StopwatchTicks * 1_000d / Stopwatch.Frequency;
    public double P99Milliseconds => P99StopwatchTicks * 1_000d / Stopwatch.Frequency;
    public double MaximumMilliseconds => MaximumStopwatchTicks * 1_000d / Stopwatch.Frequency;
}

public readonly record struct ClientWorldInputScheduleSnapshot(
    ClientInputSubmissionPolicy Policy,
    ulong SamplesObserved,
    ulong NoSourceChangeSamples,
    ulong SubmissionOpportunities,
    ulong CommandsConsidered,
    ulong CommandsSubmitted,
    ulong ContinuousCommandsSubmitted,
    ulong ContinuousCommandsCoalesced,
    ulong CadenceDeferred,
    ulong LocalHardCapDeferred,
    ulong TransportDeferred,
    ulong EdgeObligationsGenerated,
    ulong EdgeObligationsSubmitted,
    ulong EdgeObligationsCompleted,
    ulong ReleaseOrStopGenerated,
    ulong ReleaseOrStopSubmitted,
    ulong ReleaseOrStopCompleted,
    ulong CapacityRejections,
    bool ContinuousStatePending,
    int PendingObligations,
    int SubmittedCompletionDebt,
    ClientInputTimingDistribution EdgeSubmissionDelay,
    ClientInputTimingDistribution ReleaseOrStopSubmissionDelay);

/// <summary>
/// Owner-thread, engine-neutral sampling/submission separator. Continuous state is latest-wins;
/// finite action, one-shot and stop/release obligations are retained in bounded order until the
/// ordinary client transport accepts them. No authority result is manufactured here.
/// </summary>
public sealed class ClientWorldInputScheduler
{
    private readonly ClientInputSubmissionPolicy _policy;
    private readonly ClientWorldScheduledCommand[] _pending;
    private readonly SubmittedObligation[] _submitted;
    private readonly long[] _burstAdmissions;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly InputTimingHistogram _edgeDelay = new();
    private readonly InputTimingHistogram _releaseDelay = new();
    private int _pendingHead;
    private int _pendingCount;
    private int _submittedHead;
    private int _submittedCount;
    private int _burstHead;
    private int _burstCount;
    private ClientWorldScheduledCommand _latestContinuous;
    private bool _hasLatestContinuous;
    private bool _hasObserved;
    private GamePlayerActions _observedHeld;
    private GameLocomotionUpdate _observedLocomotion;
    private long _nextContinuousSubmission;
    private long _lastRefill;
    private long _tokens;
    private readonly long _maximumTokens;
    private readonly long _burstWindowTicks;
    private readonly long _continuousIntervalTicks;
    private ulong _samplesObserved;
    private ulong _noSourceChangeSamples;
    private ulong _submissionOpportunities;
    private ulong _commandsConsidered;
    private ulong _commandsSubmitted;
    private ulong _continuousSubmitted;
    private ulong _continuousCoalesced;
    private ulong _cadenceDeferred;
    private ulong _hardCapDeferred;
    private ulong _transportDeferred;
    private ulong _edgesGenerated;
    private ulong _edgesSubmitted;
    private ulong _edgesCompleted;
    private ulong _releasesGenerated;
    private ulong _releasesSubmitted;
    private ulong _releasesCompleted;
    private ulong _capacityRejections;

    public ClientWorldInputScheduler(
        ClientInputSubmissionPolicy policy,
        long initialTimestamp = 0)
    {
        if (!policy.IsValid) throw new ArgumentException("Invalid input submission policy.", nameof(policy));
        _policy = policy;
        _pending = new ClientWorldScheduledCommand[policy.MaximumPendingObligations];
        _submitted = new SubmittedObligation[policy.MaximumPendingObligations];
        _burstAdmissions = new long[policy.BurstCapacity];
        _maximumTokens = checked((long)policy.BurstCapacity * Stopwatch.Frequency);
        _tokens = _maximumTokens;
        _burstWindowTicks = Math.Max(1, checked((long)(
            (Int128)policy.BurstWindowMilliseconds * Stopwatch.Frequency / 1_000)));
        _continuousIntervalTicks = Math.Max(1, Stopwatch.Frequency / policy.RecommendedInputHz);
        long now = initialTimestamp == 0 ? Stopwatch.GetTimestamp() : initialTimestamp;
        _lastRefill = _nextContinuousSubmission = now;
    }

    public bool TryObserve(
        in ClientWorldCommandIntent intent,
        in ClientWorldCommandContext context,
        long observedTimestamp)
    {
        EnsureOwner();
        if (!intent.IsValid || !context.IsCanonical || observedTimestamp < 0) return false;
        SaturatingIncrement(ref _samplesObserved);
        GamePlayerActions held = intent.HasFocus ? intent.HeldActions : GamePlayerActions.None;
        GameLocomotionUpdate locomotion = intent.HasFocus
            ? intent.Locomotion
            : GameLocomotionUpdate.Stop;
        GamePlayerActions rising = held & ~_observedHeld;
        GamePlayerActions falling = _observedHeld & ~held;
        bool stopped = _hasObserved && IsMoving(_observedLocomotion) &&
            locomotion.Kind == GameLocomotionUpdateKind.Stop;
        ClientWorldCommandRequestFlags oneShots = context.Flags &
            (ClientWorldCommandRequestFlags.OpenInventoryRequested |
             ClientWorldCommandRequestFlags.OpenServerMonitorRequested);
        bool special = rising != GamePlayerActions.None || falling != GamePlayerActions.None ||
            stopped || oneShots != ClientWorldCommandRequestFlags.None;
        bool sourceChanged = !_hasObserved || held != _observedHeld ||
            locomotion != _observedLocomotion || context != _latestContinuous.Context;
        if (!sourceChanged) SaturatingIncrement(ref _noSourceChangeSamples);

        if (special)
        {
            ClientWorldCommandObligationKind kind = rising != GamePlayerActions.None
                ? ClientWorldCommandObligationKind.ActionEdge
                : oneShots != ClientWorldCommandRequestFlags.None
                    ? ClientWorldCommandObligationKind.OneShot
                    : ClientWorldCommandObligationKind.ReleaseOrStop;
            if (_pendingCount == _pending.Length)
            {
                SaturatingIncrement(ref _capacityRejections);
                return false;
            }
            Enqueue(new ClientWorldScheduledCommand(
                new ClientWorldCommandIntent(locomotion, held, intent.HasFocus),
                context,
                kind,
                observedTimestamp,
                falling != GamePlayerActions.None || stopped));
            if (kind is ClientWorldCommandObligationKind.ActionEdge or
                    ClientWorldCommandObligationKind.OneShot)
                SaturatingIncrement(ref _edgesGenerated);
            if (falling != GamePlayerActions.None || stopped)
                SaturatingIncrement(ref _releasesGenerated);
        }

        if (locomotion.Kind != GameLocomotionUpdateKind.Unchanged)
        {
            if (_hasLatestContinuous)
                SaturatingIncrement(ref _continuousCoalesced);
            _latestContinuous = new ClientWorldScheduledCommand(
                new ClientWorldCommandIntent(locomotion, held, intent.HasFocus),
                context.WithoutOneShots(),
                ClientWorldCommandObligationKind.Continuous,
                observedTimestamp,
                locomotion.Kind == GameLocomotionUpdateKind.Stop);
            _hasLatestContinuous = true;
        }
        _observedHeld = held;
        _observedLocomotion = locomotion;
        _hasObserved = true;
        return true;
    }

    public bool TryPump(long now, ClientWorldSubmissionAttempt attempt)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(attempt);
        SaturatingIncrement(ref _submissionOpportunities);
        bool pending = _pendingCount != 0;
        if (!pending && !_hasLatestContinuous) return true;
        if (!pending && now < _nextContinuousSubmission)
        {
            SaturatingIncrement(ref _cadenceDeferred);
            return true;
        }
        if (now < _lastRefill) now = _lastRefill;
        Refill(now);
        ExpireBurstAdmissions(now);
        if (_tokens < Stopwatch.Frequency || _burstCount == _burstAdmissions.Length)
        {
            SaturatingIncrement(ref _hardCapDeferred);
            return true;
        }
        ClientWorldScheduledCommand command = pending
            ? _pending[_pendingHead]
            : _latestContinuous;
        SaturatingIncrement(ref _commandsConsidered);
        if (!attempt(in command, out ulong sequence) || sequence == 0)
        {
            SaturatingIncrement(ref _transportDeferred);
            return false;
        }
        _tokens -= Stopwatch.Frequency;
        int burstTail = (_burstHead + _burstCount) % _burstAdmissions.Length;
        _burstAdmissions[burstTail] = now;
        _burstCount++;
        SaturatingIncrement(ref _commandsSubmitted);
        if (pending)
        {
            _pending[_pendingHead] = default;
            _pendingHead = (_pendingHead + 1) % _pending.Length;
            _pendingCount--;
            RecordSubmittedObligation(in command, sequence, now);
        }
        else
        {
            _hasLatestContinuous = false;
            SaturatingIncrement(ref _continuousSubmitted);
        }
        if (pending && command.Intent.Locomotion.Kind != GameLocomotionUpdateKind.Unchanged &&
            _hasLatestContinuous &&
            _latestContinuous.Intent == command.Intent &&
            _latestContinuous.Context == command.Context.WithoutOneShots())
        {
            // The retained edge carried the latest locomotion state with it. Do not emit the
            // same continuous state again merely because it was also the most recent sample.
            _hasLatestContinuous = false;
            SaturatingIncrement(ref _continuousCoalesced);
        }
        AdvanceContinuousDeadline(now);
        return true;
    }

    public void ObserveCompleted(ulong processedSequence)
    {
        EnsureOwner();
        while (_submittedCount != 0)
        {
            SubmittedObligation submitted = _submitted[_submittedHead];
            if (submitted.Sequence > processedSequence) break;
            if (submitted.Edge) SaturatingIncrement(ref _edgesCompleted);
            if (submitted.ReleaseOrStop) SaturatingIncrement(ref _releasesCompleted);
            _submitted[_submittedHead] = default;
            _submittedHead = (_submittedHead + 1) % _submitted.Length;
            _submittedCount--;
        }
    }

    public ClientWorldInputScheduleSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new(
            _policy, _samplesObserved, _noSourceChangeSamples, _submissionOpportunities,
            _commandsConsidered, _commandsSubmitted, _continuousSubmitted,
            _continuousCoalesced, _cadenceDeferred, _hardCapDeferred, _transportDeferred,
            _edgesGenerated, _edgesSubmitted, _edgesCompleted,
            _releasesGenerated, _releasesSubmitted, _releasesCompleted,
            _capacityRejections, _hasLatestContinuous, _pendingCount, _submittedCount,
            _edgeDelay.Snapshot(), _releaseDelay.Snapshot());
    }

    /// <summary>
    /// Starts a cold diagnostic measurement window without changing sampled input, cadence,
    /// limiter state, or any retained semantic obligation. Callers must first quiesce all debt
    /// so window-local generated/submitted/completed counts reconcile exactly.
    /// </summary>
    public void ResetTelemetryWindow()
    {
        EnsureOwner();
        if (_hasLatestContinuous || _pendingCount != 0 || _submittedCount != 0)
            throw new InvalidOperationException(
                "Input scheduling telemetry cannot reset while semantic work is outstanding.");
        _samplesObserved = _noSourceChangeSamples = _submissionOpportunities = 0;
        _commandsConsidered = _commandsSubmitted = _continuousSubmitted = 0;
        _continuousCoalesced = _cadenceDeferred = _hardCapDeferred = _transportDeferred = 0;
        _edgesGenerated = _edgesSubmitted = _edgesCompleted = 0;
        _releasesGenerated = _releasesSubmitted = _releasesCompleted = 0;
        _capacityRejections = 0;
        _edgeDelay.Clear();
        _releaseDelay.Clear();
    }

    private void RecordSubmittedObligation(
        in ClientWorldScheduledCommand command,
        ulong sequence,
        long now)
    {
        bool edge = command.Kind is ClientWorldCommandObligationKind.ActionEdge or
            ClientWorldCommandObligationKind.OneShot;
        bool release = command.IncludesReleaseOrStop;
        if (edge)
        {
            SaturatingIncrement(ref _edgesSubmitted);
            _edgeDelay.Record(Math.Max(0, now - command.ObservedTimestamp));
        }
        if (release)
        {
            SaturatingIncrement(ref _releasesSubmitted);
            _releaseDelay.Record(Math.Max(0, now - command.ObservedTimestamp));
        }
        if (!edge && !release) return;
        if (_submittedCount == _submitted.Length)
            throw new InvalidOperationException(
                "Input completion debt exceeded its explicit bounded capacity.");
        int tail = (_submittedHead + _submittedCount) % _submitted.Length;
        _submitted[tail] = new SubmittedObligation(sequence, edge, release);
        _submittedCount++;
    }

    private void Enqueue(in ClientWorldScheduledCommand command)
    {
        int tail = (_pendingHead + _pendingCount) % _pending.Length;
        _pending[tail] = command;
        _pendingCount++;
    }

    private void Refill(long now)
    {
        if (now < _lastRefill) now = _lastRefill;
        long elapsed = now - _lastRefill;
        if (elapsed != 0)
        {
            Int128 proportional = (Int128)elapsed * _policy.MaximumInputHz;
            long added = proportional >= _maximumTokens
                ? _maximumTokens
                : checked((long)proportional);
            _tokens = Math.Min(_maximumTokens, checked(_tokens + added));
            _lastRefill = now;
        }
    }

    private void ExpireBurstAdmissions(long now)
    {
        while (_burstCount != 0 &&
               now - _burstAdmissions[_burstHead] >= _burstWindowTicks)
        {
            _burstAdmissions[_burstHead] = 0;
            _burstHead = (_burstHead + 1) % _burstAdmissions.Length;
            _burstCount--;
        }
    }

    private void AdvanceContinuousDeadline(long now)
    {
        // Retain an absolute cadence phase. Rebasing the deadline on the actual sampled frame
        // quantizes a 30 Hz policy down to 20 Hz on a nominal 60 Hz renderer whenever small
        // scheduling jitter puts every second frame just before the newly rebased deadline.
        // We still submit at most one current state per pump and never synthesize missed samples.
        if (now < _nextContinuousSubmission) return;
        long elapsed = now - _nextContinuousSubmission;
        long intervals = checked(elapsed / _continuousIntervalTicks + 1);
        _nextContinuousSubmission = checked(
            _nextContinuousSubmission + intervals * _continuousIntervalTicks);
    }

    private static bool IsMoving(in GameLocomotionUpdate value) =>
        value.Kind is GameLocomotionUpdateKind.Direct or GameLocomotionUpdateKind.Destination;

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Client input scheduling is owner-thread bound.");
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private readonly record struct SubmittedObligation(
        ulong Sequence, bool Edge, bool ReleaseOrStop);

    private sealed class InputTimingHistogram
    {
        private readonly ulong[] _buckets = new ulong[64];
        private ulong _count;
        private ulong _maximum;

        internal void Record(long ticks)
        {
            ulong value = checked((ulong)Math.Max(0, ticks));
            int bucket = value == 0 ? 0 : Math.Min(63, BitOperations.Log2(value) + 1);
            if (_buckets[bucket] != ulong.MaxValue) _buckets[bucket]++;
            if (_count != ulong.MaxValue) _count++;
            _maximum = Math.Max(_maximum, value);
        }

        internal ClientInputTimingDistribution Snapshot() => new(
            _count, Percentile(50), Percentile(95), Percentile(99), _maximum);

        internal void Clear()
        {
            Array.Clear(_buckets);
            _count = 0;
            _maximum = 0;
        }

        private ulong Percentile(int percentage)
        {
            if (_count == 0) return 0;
            ulong target = (ulong)(((UInt128)_count * (uint)percentage + 99) / 100);
            ulong cumulative = 0;
            for (int index = 0; index < _buckets.Length; index++)
            {
                cumulative = ulong.MaxValue - cumulative < _buckets[index]
                    ? ulong.MaxValue
                    : cumulative + _buckets[index];
                if (cumulative < target) continue;
                if (index == 0) return 0;
                ulong upper = index == 63 ? ulong.MaxValue : (1UL << index) - 1;
                return Math.Min(upper, _maximum);
            }
            return _maximum;
        }
    }
}
