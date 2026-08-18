using UltraSim.Game.Protocol;
using UltraSim.Game.Admission;
using UltraSim.Network;

namespace UltraSim.Game.Replication;

public readonly record struct RemoteWorldStateCompactionSchedulerSnapshot(
    int RegisteredRecipients,
    int PendingRequests,
    int MaximumStartsPerTick,
    ulong RequestsAccepted,
    ulong DuplicateRequests,
    ulong RequestsRejected,
    ulong CompactionsStarted);

/// <summary>Owner-thread sink for validated client compaction pressure.</summary>
public interface IRemoteWorldStateCompactionRequestSink
{
    void AcceptCompactionRequest(
        ParticipantId participant,
        in RemoteWorldStateCompactionRequestMessage request);
}

public enum RemoteWorldStateCompactionStartStatus : byte
{
    Retry = 0,
    Started = 1,
    Reject = 2,
}

/// <summary>Attempts one already-admitted maintenance baseline.</summary>
public delegate bool RemoteWorldStateCompactionStarter(
    ParticipantId participant,
    in RemoteWorldStateCompactionRequestMessage request);

/// <summary>Attempts one admitted maintenance baseline with stale-request handling.</summary>
public delegate RemoteWorldStateCompactionStartStatus RemoteWorldStateCompactionStarterV2(
    ParticipantId participant,
    in RemoteWorldStateCompactionRequestMessage request);

/// <summary>
/// Fixed-capacity, deterministic round-robin admission for WorldState
/// maintenance baselines. Each registered recipient owns one request bit; no
/// queue grows when many clients cross their pressure threshold together.
/// </summary>
public sealed class RemoteWorldStateCompactionScheduler :
    IRemoteWorldStateCompactionRequestSink
{
    private readonly Entry[] _entries;
    private readonly int _maximumStartsPerTick;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _registeredRecipients;
    private int _pendingRequests;
    private int _cursor;
    private ulong _requestsAccepted;
    private ulong _duplicateRequests;
    private ulong _requestsRejected;
    private ulong _compactionsStarted;

    public RemoteWorldStateCompactionScheduler(
        int maximumRecipients,
        int maximumStartsPerTick)
    {
        if (maximumRecipients <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRecipients));
        if (maximumStartsPerTick <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumStartsPerTick));
        _entries = new Entry[maximumRecipients];
        _maximumStartsPerTick = maximumStartsPerTick;
    }

    public bool TryRegister(ParticipantId participant)
    {
        EnsureOwnerThread();
        if (!participant.IsValid || Find(participant) >= 0) return false;
        for (int index = 0; index < _entries.Length; index++)
        {
            if (_entries[index].Registered) continue;
            _entries[index] = new Entry { Registered = true, Participant = participant };
            _registeredRecipients++;
            return true;
        }
        return false;
    }

    public bool Unregister(ParticipantId participant)
    {
        EnsureOwnerThread();
        int index = Find(participant);
        if (index < 0) return false;
        if (_entries[index].Pending) _pendingRequests--;
        _entries[index] = default;
        _registeredRecipients--;
        return true;
    }

    public void AcceptCompactionRequest(
        ParticipantId participant,
        in RemoteWorldStateCompactionRequestMessage request)
    {
        EnsureOwnerThread();
        int index = Find(participant);
        if (index < 0 || request.RequestSequence == 0 ||
            request.KnownIdentityCapacity == 0 ||
            request.KnownIdentityCount > request.KnownIdentityCapacity)
        {
            SaturatingIncrement(ref _requestsRejected);
            return;
        }

        ref Entry entry = ref _entries[index];
        if (request.RequestSequence <= entry.LastRequestSequence)
        {
            SaturatingIncrement(ref _duplicateRequests);
            return;
        }

        entry.LastRequestSequence = request.RequestSequence;
        entry.Request = request;
        if (!entry.Pending)
        {
            entry.Pending = true;
            _pendingRequests++;
        }
        SaturatingIncrement(ref _requestsAccepted);
    }

    /// <summary>
    /// Starts at most the configured number of pending baselines in stable
    /// rotating recipient order. A false starter result leaves the request
    /// pending for a later tick.
    /// </summary>
    public int TryStartPending(RemoteWorldStateCompactionStarter starter)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(starter);
        int started = 0;
        int inspected = 0;
        while (inspected < _entries.Length && started < _maximumStartsPerTick)
        {
            int index = _cursor++;
            if (_cursor == _entries.Length) _cursor = 0;
            inspected++;
            ref Entry entry = ref _entries[index];
            if (!entry.Registered || !entry.Pending) continue;
            if (!starter(entry.Participant, in entry.Request)) continue;
            entry.Pending = false;
            _pendingRequests--;
            started++;
            SaturatingIncrement(ref _compactionsStarted);
        }
        return started;
    }

    /// <summary>
    /// Starts pending requests while allowing a caller to discard a request that
    /// became stale after admission (for example, across a StateEpoch swap).
    /// Retry leaves the fixed request bit set for a later tick.
    /// </summary>
    public int TryStartPending(RemoteWorldStateCompactionStarterV2 starter)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(starter);
        int started = 0;
        int inspected = 0;
        while (inspected < _entries.Length && started < _maximumStartsPerTick)
        {
            int index = _cursor++;
            if (_cursor == _entries.Length) _cursor = 0;
            inspected++;
            ref Entry entry = ref _entries[index];
            if (!entry.Registered || !entry.Pending) continue;
            RemoteWorldStateCompactionStartStatus status =
                starter(entry.Participant, in entry.Request);
            if (status == RemoteWorldStateCompactionStartStatus.Retry) continue;
            entry.Pending = false;
            _pendingRequests--;
            if (status == RemoteWorldStateCompactionStartStatus.Reject)
            {
                SaturatingIncrement(ref _requestsRejected);
                continue;
            }
            started++;
            SaturatingIncrement(ref _compactionsStarted);
        }
        return started;
    }

    public RemoteWorldStateCompactionSchedulerSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(
            _registeredRecipients,
            _pendingRequests,
            _maximumStartsPerTick,
            _requestsAccepted,
            _duplicateRequests,
            _requestsRejected,
            _compactionsStarted);
    }

    private int Find(ParticipantId participant)
    {
        for (int index = 0; index < _entries.Length; index++)
            if (_entries[index].Registered && _entries[index].Participant == participant)
                return index;
        return -1;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The WorldState compaction scheduler is owner-thread-bound.");
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private struct Entry
    {
        internal bool Registered;
        internal bool Pending;
        internal ParticipantId Participant;
        internal ulong LastRequestSequence;
        internal RemoteWorldStateCompactionRequestMessage Request;
    }
}
