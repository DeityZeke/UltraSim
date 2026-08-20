namespace UltraSim.Network;

/// <summary>Fixed reservation owned by one deterministic two-peer datagram topology.</summary>
public readonly record struct DeterministicDatagramLimits(int SlotCapacity, int MaximumDatagramBytes)
{
    public static DeterministicDatagramLimits Default => new(4_096, 1_472);

    internal void Validate()
    {
        if (SlotCapacity is <= 0 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(SlotCapacity));
        if (MaximumDatagramBytes is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(MaximumDatagramBytes));
        _ = checked((long)SlotCapacity * MaximumDatagramBytes * 2L);
    }

    /// <summary>Returns known two-direction payload and metadata bytes, excluding CLR object headers.</summary>
    public long EstimateReservedPayloadBytes()
    {
        Validate();
        return checked(2L * SlotCapacity *
            (MaximumDatagramBytes + DatagramSlotMetadata.EstimatedBytes + sizeof(int)));
    }
}

internal static class DatagramSlotMetadata
{
    internal const int EstimatedBytes = sizeof(int) * 3 + sizeof(ulong) * 2 + sizeof(byte);
}

public enum DatagramSendStatus : byte
{
    /// <summary>
    /// The endpoint accepted ownership of a copied datagram. A queued/batched
    /// endpoint may not yet have submitted it to the operating-system socket;
    /// this is a transport-admission result, not proof of wire delivery.
    /// </summary>
    Accepted = 0,
    DroppedByImpairment = 1,
    CapacityExceeded = 2,
    DatagramTooLarge = 3,
}

public readonly record struct DeterministicDatagramSnapshot(
    ulong Step,
    ulong Submitted,
    ulong Dropped,
    ulong Duplicated,
    ulong Corrupted,
    int PendingAToB,
    int PendingBToA,
    long ReservedBytes);

/// <summary>Deterministic fixed-capacity in-memory datagram transport for protocol tests.</summary>
public sealed class DeterministicDatagramLink
{
    private readonly int _ownerThreadId;
    private readonly Direction _aToB;
    private readonly Direction _bToA;
    private readonly DeterministicNetworkImpairment _impairment;
    private ulong _step;
    private ulong _submissionOrdinal;
    private ulong _dropped;
    private ulong _duplicated;
    private ulong _corrupted;

    public DeterministicDatagramLink(
        int slotCapacity,
        int maximumDatagramBytes,
        NetworkImpairmentOptions impairment = default)
        : this(new DeterministicDatagramLimits(slotCapacity, maximumDatagramBytes), impairment)
    {
    }

    public DeterministicDatagramLink(
        DeterministicDatagramLimits limits,
        NetworkImpairmentOptions impairment = default)
    {
        limits.Validate();
        _ownerThreadId = Environment.CurrentManagedThreadId;
        Limits = limits;
        _aToB = new Direction(limits.SlotCapacity, limits.MaximumDatagramBytes);
        _bToA = new Direction(limits.SlotCapacity, limits.MaximumDatagramBytes);
        _impairment = new DeterministicNetworkImpairment(impairment);
        A = new DeterministicDatagramEndpoint(this, sendsAToB: true);
        B = new DeterministicDatagramEndpoint(this, sendsAToB: false);
    }

    public DeterministicDatagramEndpoint A { get; }
    public DeterministicDatagramEndpoint B { get; }
    public DeterministicDatagramLimits Limits { get; }

    public void Advance()
    {
        EnsureOwnerThread();
        if (_step == ulong.MaxValue) throw new InvalidOperationException("The deterministic datagram step space is exhausted.");
        _step++;
    }

    public DeterministicDatagramSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(
            _step, _submissionOrdinal, _dropped, _duplicated, _corrupted,
            _aToB.Count, _bToA.Count,
            checked(_aToB.ReservedBytes + _bToA.ReservedBytes));
    }

    internal DatagramSendStatus Send(bool aToB, ReadOnlySpan<byte> datagram)
    {
        EnsureOwnerThread();
        Direction direction = aToB ? _aToB : _bToA;
        if (datagram.Length > direction.MaximumDatagramBytes) return DatagramSendStatus.DatagramTooLarge;
        NetworkImpairmentDecision decision = _impairment.Decide(_submissionOrdinal);
        if (decision.Dropped)
        {
            _submissionOrdinal++;
            _dropped++;
            return DatagramSendStatus.DroppedByImpairment;
        }
        int copies = decision.Duplicated ? 2 : 1;
        if (direction.Available < copies) return DatagramSendStatus.CapacityExceeded;
        ulong ordinal = _submissionOrdinal++;
        ulong deliveryStep = checked(_step + decision.DelaySteps);
        direction.Enqueue(datagram, deliveryStep, ordinal, copy: 0, decision.Corrupted);
        if (copies == 2) direction.Enqueue(datagram, deliveryStep, ordinal, copy: 1, decision.Corrupted);
        if (decision.Duplicated) _duplicated++;
        if (decision.Corrupted) _corrupted += unchecked((ulong)copies);
        return DatagramSendStatus.Accepted;
    }

    internal bool TryReceive(bool endpointA, Span<byte> destination, out int length)
    {
        EnsureOwnerThread();
        Direction incoming = endpointA ? _bToA : _aToB;
        return incoming.TryDequeueReady(_step, destination, out length);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The deterministic datagram link is bound to its creating owner thread.");
    }

    private sealed class Direction
    {
        private readonly byte[] _payloads;
        private readonly Slot[] _slots;
        private readonly int[] _free;
        private int _freeCount;

        internal Direction(int capacity, int maximumDatagramBytes)
        {
            MaximumDatagramBytes = maximumDatagramBytes;
            _payloads = new byte[checked(capacity * maximumDatagramBytes)];
            _slots = new Slot[capacity];
            _free = new int[capacity];
            for (int index = 0; index < capacity; index++) _free[index] = capacity - 1 - index;
            _freeCount = capacity;
        }

        internal int MaximumDatagramBytes { get; }
        internal int Available => _freeCount;
        internal int Count => _slots.Length - _freeCount;
        internal long ReservedBytes => checked((long)_payloads.Length +
            (long)_slots.Length * DatagramSlotMetadata.EstimatedBytes + (long)_free.Length * sizeof(int));

        internal void Enqueue(
            ReadOnlySpan<byte> datagram,
            ulong deliveryStep,
            ulong ordinal,
            byte copy,
            bool corrupted)
        {
            int slot = _free[--_freeCount];
            Span<byte> destination = _payloads.AsSpan(slot * MaximumDatagramBytes, MaximumDatagramBytes);
            datagram.CopyTo(destination);
            if (corrupted && datagram.Length != 0) destination[datagram.Length - 1] ^= 0x80;
            _slots[slot] = new Slot(true, datagram.Length, deliveryStep, ordinal, copy);
        }

        internal bool TryDequeueReady(ulong step, Span<byte> destination, out int length)
        {
            int selected = -1;
            Slot best = default;
            for (int index = 0; index < _slots.Length; index++)
            {
                Slot candidate = _slots[index];
                if (!candidate.Active || candidate.DeliveryStep > step) continue;
                if (selected < 0 || candidate.DeliveryStep < best.DeliveryStep ||
                    (candidate.DeliveryStep == best.DeliveryStep && candidate.Ordinal < best.Ordinal) ||
                    (candidate.DeliveryStep == best.DeliveryStep && candidate.Ordinal == best.Ordinal && candidate.Copy < best.Copy))
                {
                    selected = index;
                    best = candidate;
                }
            }
            if (selected < 0) { length = 0; return false; }
            length = best.Length;
            if (destination.Length < best.Length) return false;
            _payloads.AsSpan(selected * MaximumDatagramBytes, best.Length).CopyTo(destination);
            _payloads.AsSpan(selected * MaximumDatagramBytes, MaximumDatagramBytes).Clear();
            _slots[selected] = default;
            _free[_freeCount++] = selected;
            return true;
        }

        private readonly record struct Slot(bool Active, int Length, ulong DeliveryStep, ulong Ordinal, byte Copy);
    }
}

public sealed class DeterministicDatagramEndpoint : INetworkDatagramEndpoint
{
    private readonly DeterministicDatagramLink _link;
    private readonly bool _sendsAToB;

    internal DeterministicDatagramEndpoint(DeterministicDatagramLink link, bool sendsAToB) =>
        (_link, _sendsAToB) = (link, sendsAToB);

    public DatagramSendStatus TrySend(ReadOnlySpan<byte> datagram) => _link.Send(_sendsAToB, datagram);

    public bool TryReceive(Span<byte> destination, out int length) =>
        _link.TryReceive(endpointA: _sendsAToB, destination, out length);
}
