using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.Network;

/// <summary>Generation-fenced identity of one hosted connection owner.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct HostedConnectionOwnerId : IEquatable<HostedConnectionOwnerId>
{
    public uint Slot { get; }
    public uint Generation { get; }
    public bool IsValid => Generation != 0;

    public HostedConnectionOwnerId(uint slot, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        Slot = slot;
        Generation = generation;
    }

    public bool Equals(HostedConnectionOwnerId other) =>
        Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) =>
        obj is HostedConnectionOwnerId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    public override string ToString() => IsValid ? $"{Slot}:{Generation}" : "invalid";
    public static bool operator ==(
        HostedConnectionOwnerId left, HostedConnectionOwnerId right) => left.Equals(right);
    public static bool operator !=(
        HostedConnectionOwnerId left, HostedConnectionOwnerId right) => !left.Equals(right);
}

/// <summary>Generation-fenced exclusive lease over one handoff packet slot.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct HostedPacketTransferId : IEquatable<HostedPacketTransferId>
{
    public uint Slot { get; }
    public uint Generation { get; }
    public bool IsValid => Generation != 0;

    public HostedPacketTransferId(uint slot, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        Slot = slot;
        Generation = generation;
    }

    public bool Equals(HostedPacketTransferId other) =>
        Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) =>
        obj is HostedPacketTransferId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    public static bool operator ==(
        HostedPacketTransferId left, HostedPacketTransferId right) => left.Equals(right);
    public static bool operator !=(
        HostedPacketTransferId left, HostedPacketTransferId right) => !left.Equals(right);
}

/// <summary>Meaning of one hosted cross-owner datagram.</summary>
public enum HostedPacketTransferKind : byte
{
    SecureProtocol = 1,
    PathControl = 2,
    PathRebindCommit = 3,
}

/// <summary>Bounded admission result for a hosted owner handoff.</summary>
public enum HostedPacketHandoffStatus : byte
{
    Accepted = 0,
    InvalidInput = 1,
    UnknownConnection = 2,
    StaleOwner = 3,
    DestinationClosed = 4,
    DestinationFull = 5,
    PacketCapacityExceeded = 6,
    ConnectionBusy = 7,
}

/// <summary>Immutable metadata attached to one exclusively owned packet lease.</summary>
public readonly record struct HostedPacketTransfer(
    HostedPacketTransferId Id,
    HostedConnectionOwnerId Owner,
    ConnectionId Connection,
    HostedPathGeneration PathGeneration,
    HostedPacketTransferKind Kind,
    int Length,
    bool HasPathBinding);

/// <summary>Fixed storage and queue bounds for hosted connection-owner sharding.</summary>
public readonly record struct HostedUdpOwnerShardingLimits(
    int MaximumOwners = 4,
    int MaximumConnections = 4_096,
    int MaximumPacketSlots = 8_192,
    int MaximumQueuedPacketsPerOwner = 1_024,
    int MaximumDatagramBytes = 1_472)
{
    public static HostedUdpOwnerShardingLimits Default => new();

    internal void Validate()
    {
        if (MaximumOwners is <= 0 or > 1_024)
            throw new ArgumentOutOfRangeException(nameof(MaximumOwners));
        if (MaximumConnections is <= 0 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(MaximumConnections));
        if (MaximumPacketSlots is <= 0 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(MaximumPacketSlots));
        if (MaximumQueuedPacketsPerOwner is <= 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(MaximumQueuedPacketsPerOwner));
        if (MaximumDatagramBytes is < 256 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(MaximumDatagramBytes));
        _ = checked(MaximumOwners * MaximumQueuedPacketsPerOwner);
        _ = checked(MaximumPacketSlots * MaximumDatagramBytes);
    }

    public long EstimateReservedBytes()
    {
        Validate();
        int queueSlots = checked(MaximumOwners * MaximumQueuedPacketsPerOwner);
        return checked(
            (long)MaximumPacketSlots * MaximumDatagramBytes +
            (long)MaximumPacketSlots * HostedUdpConnectionOwnerHandoff.PathBindingBytes +
            (long)MaximumPacketSlots * (
                sizeof(uint) + sizeof(byte) * 3 + sizeof(ushort) +
                Unsafe.SizeOf<HostedConnectionOwnerId>() +
                Unsafe.SizeOf<ConnectionId>() +
                Unsafe.SizeOf<HostedPathGeneration>() + sizeof(int)) +
            (long)MaximumPacketSlots * sizeof(int) +
            (long)MaximumOwners * (
                sizeof(uint) + sizeof(int) * 3 + sizeof(long) * 4) +
            (long)MaximumOwners * sizeof(int) +
            (long)MaximumConnections * (
                Unsafe.SizeOf<ConnectionId>() +
                Unsafe.SizeOf<HostedConnectionOwnerId>()) +
            (long)queueSlots * Unsafe.SizeOf<HostedPacketTransferId>() * 2);
    }
}

/// <summary>Cold ownership, pressure, and lifetime evidence for one handoff.</summary>
public readonly record struct HostedUdpOwnerHandoffSnapshot(
    int OpenOwners,
    int BoundConnections,
    int PacketCapacity,
    int RentedPackets,
    int RetiredPacketSlots,
    int QueuedIngress,
    int QueuedEgress,
    ulong IngressAccepted,
    ulong EgressAccepted,
    ulong IngressAcquired,
    ulong EgressAcquired,
    ulong Released,
    ulong DestinationFullDrops,
    ulong PacketCapacityDrops,
    ulong ClosedOrStaleDrops,
    ulong ForcedOwnerReclaims,
    long ReservedBytes);

/// <summary>
/// Fixed cross-owner packet storage for one listener and bounded connection owners.
/// </summary>
/// <remarks>
/// The listener owns socket/path routing and is the sole ingress producer and
/// egress consumer. Each connection owner is the sole ingress consumer and
/// egress producer for its queue. A transfer publishes a generation-fenced slot;
/// only the current owner may inspect its immutable payload, and release clears
/// the bytes before returning capacity. The clear ConnectionId selects a queue;
/// it is never authentication or path-rebind authorization.
/// </remarks>
public sealed class HostedUdpConnectionOwnerHandoff : IDisposable
{
    internal const int PathBindingBytes = 7;

    private enum PacketState : byte
    {
        Free = 0,
        IngressQueued = 1,
        IngressAcquired = 2,
        EgressQueued = 3,
        EgressAcquired = 4,
        Retired = 5,
    }

    private readonly int _listenerThreadId = Environment.CurrentManagedThreadId;
    private readonly HostedUdpOwnerShardingLimits _limits;
    private readonly object _packetGate = new();
    private readonly uint[] _ownerGenerations;
    private readonly int[] _ownerThreadIds;
    private readonly int[] _ownerStates;
    private readonly int[] _ownerFree;
    private readonly ConnectionId[] _boundConnections;
    private readonly HostedConnectionOwnerId[] _connectionOwners;
    private readonly byte[] _payloads;
    private readonly byte[] _pathBindings;
    private readonly uint[] _packetGenerations;
    private readonly byte[] _packetStates;
    private readonly HostedConnectionOwnerId[] _packetOwners;
    private readonly ConnectionId[] _packetConnections;
    private readonly HostedPathGeneration[] _packetPathGenerations;
    private readonly byte[] _packetKinds;
    private readonly ushort[] _packetLengths;
    private readonly byte[] _packetHasPath;
    private readonly int[] _packetFree;
    private readonly HostedPacketTransferId[] _ingressQueues;
    private readonly HostedPacketTransferId[] _egressQueues;
    private readonly long[] _ingressWrites;
    private readonly long[] _ingressReads;
    private readonly long[] _egressWrites;
    private readonly long[] _egressReads;
    private int _ownerFreeCount;
    private int _openOwners;
    private int _boundConnectionCount;
    private int _packetFreeCount;
    private int _rentedPackets;
    private int _retiredPacketSlots;
    private long _ingressAccepted;
    private long _egressAccepted;
    private long _ingressAcquired;
    private long _egressAcquired;
    private long _released;
    private long _destinationFullDrops;
    private long _packetCapacityDrops;
    private long _closedOrStaleDrops;
    private long _forcedOwnerReclaims;
    private bool _disposed;

    public HostedUdpConnectionOwnerHandoff(
        HostedUdpOwnerShardingLimits limits = default)
    {
        if (limits == default) limits = HostedUdpOwnerShardingLimits.Default;
        limits.Validate();
        _limits = limits;
        _ownerGenerations = new uint[limits.MaximumOwners];
        _ownerThreadIds = new int[limits.MaximumOwners];
        _ownerStates = new int[limits.MaximumOwners];
        _ownerFree = new int[limits.MaximumOwners];
        for (int index = 0; index < _ownerFree.Length; index++)
            _ownerFree[index] = _ownerFree.Length - 1 - index;
        _ownerFreeCount = _ownerFree.Length;
        _boundConnections = new ConnectionId[limits.MaximumConnections];
        _connectionOwners = new HostedConnectionOwnerId[limits.MaximumConnections];
        _payloads = new byte[checked(limits.MaximumPacketSlots * limits.MaximumDatagramBytes)];
        _pathBindings = new byte[checked(limits.MaximumPacketSlots * PathBindingBytes)];
        _packetGenerations = new uint[limits.MaximumPacketSlots];
        _packetStates = new byte[limits.MaximumPacketSlots];
        _packetOwners = new HostedConnectionOwnerId[limits.MaximumPacketSlots];
        _packetConnections = new ConnectionId[limits.MaximumPacketSlots];
        _packetPathGenerations = new HostedPathGeneration[limits.MaximumPacketSlots];
        _packetKinds = new byte[limits.MaximumPacketSlots];
        _packetLengths = new ushort[limits.MaximumPacketSlots];
        _packetHasPath = new byte[limits.MaximumPacketSlots];
        _packetFree = new int[limits.MaximumPacketSlots];
        for (int index = 0; index < _packetFree.Length; index++)
            _packetFree[index] = _packetFree.Length - 1 - index;
        _packetFreeCount = _packetFree.Length;
        int queueSlots = checked(limits.MaximumOwners * limits.MaximumQueuedPacketsPerOwner);
        _ingressQueues = new HostedPacketTransferId[queueSlots];
        _egressQueues = new HostedPacketTransferId[queueSlots];
        _ingressWrites = new long[limits.MaximumOwners];
        _ingressReads = new long[limits.MaximumOwners];
        _egressWrites = new long[limits.MaximumOwners];
        _egressReads = new long[limits.MaximumOwners];
        ReservedBytes = limits.EstimateReservedBytes();
    }

    public long ReservedBytes { get; }

    public bool TryOpenOwner(int ownerThreadId, out HostedConnectionOwnerId owner)
    {
        EnsureListenerOperational();
        if (ownerThreadId <= 0) throw new ArgumentOutOfRangeException(nameof(ownerThreadId));
        while (_ownerFreeCount != 0)
        {
            int slot = _ownerFree[--_ownerFreeCount];
            uint generation = _ownerGenerations[slot];
            if (generation == uint.MaxValue) continue;
            generation++;
            _ownerGenerations[slot] = generation;
            _ownerThreadIds[slot] = ownerThreadId;
            Volatile.Write(ref _ownerStates[slot], 1);
            Volatile.Write(ref _ingressWrites[slot], 0);
            Volatile.Write(ref _ingressReads[slot], 0);
            Volatile.Write(ref _egressWrites[slot], 0);
            Volatile.Write(ref _egressReads[slot], 0);
            Array.Clear(_ingressQueues,
                slot * _limits.MaximumQueuedPacketsPerOwner,
                _limits.MaximumQueuedPacketsPerOwner);
            Array.Clear(_egressQueues,
                slot * _limits.MaximumQueuedPacketsPerOwner,
                _limits.MaximumQueuedPacketsPerOwner);
            _openOwners++;
            owner = new HostedConnectionOwnerId(checked((uint)slot), generation);
            return true;
        }
        owner = default;
        return false;
    }

    public HostedPacketHandoffStatus TryBindConnection(
        ConnectionId connection,
        HostedConnectionOwnerId owner)
    {
        EnsureListenerOperational();
        if (!TryValidateConnectionSlot(connection, out int connectionSlot))
            return HostedPacketHandoffStatus.InvalidInput;
        HostedPacketHandoffStatus ownerStatus = ValidateOwner(owner, requireListener: true);
        if (ownerStatus != HostedPacketHandoffStatus.Accepted) return ownerStatus;
        lock (_packetGate)
        {
            if (_boundConnections[connectionSlot].IsValid)
                return HostedPacketHandoffStatus.ConnectionBusy;
            _boundConnections[connectionSlot] = connection;
            _connectionOwners[connectionSlot] = owner;
            _boundConnectionCount++;
        }
        return HostedPacketHandoffStatus.Accepted;
    }

    public HostedPacketHandoffStatus TryMigrateConnection(
        ConnectionId connection,
        HostedConnectionOwnerId expectedOwner,
        HostedConnectionOwnerId replacementOwner)
    {
        EnsureListenerOperational();
        HostedPacketHandoffStatus replacement = ValidateOwner(
            replacementOwner, requireListener: true);
        if (replacement != HostedPacketHandoffStatus.Accepted) return replacement;
        lock (_packetGate)
        {
            if (!TryGetBindingCore(
                    connection, out int connectionSlot,
                    out HostedConnectionOwnerId current))
                return HostedPacketHandoffStatus.UnknownConnection;
            if (current != expectedOwner)
                return HostedPacketHandoffStatus.StaleOwner;
            if (HasOutstandingConnectionPacketsCore(connection))
                return HostedPacketHandoffStatus.ConnectionBusy;
            _connectionOwners[connectionSlot] = replacementOwner;
        }
        return HostedPacketHandoffStatus.Accepted;
    }

    public HostedPacketHandoffStatus TryUnbindConnection(
        ConnectionId connection,
        HostedConnectionOwnerId expectedOwner)
    {
        EnsureListenerOperational();
        lock (_packetGate)
        {
            if (!TryGetBindingCore(connection, out int slot, out HostedConnectionOwnerId current))
                return HostedPacketHandoffStatus.UnknownConnection;
            if (current != expectedOwner)
                return HostedPacketHandoffStatus.StaleOwner;
            if (HasOutstandingConnectionPacketsCore(connection))
                return HostedPacketHandoffStatus.ConnectionBusy;
            _boundConnections[slot] = default;
            _connectionOwners[slot] = default;
            _boundConnectionCount--;
        }
        return HostedPacketHandoffStatus.Accepted;
    }

    public HostedPacketHandoffStatus TryOfferIngress(
        ConnectionId connection,
        HostedPathGeneration pathGeneration,
        HostedPacketTransferKind kind,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> pathBinding = default)
    {
        EnsureListenerOperational();
        if (!TryGetBinding(connection, out _, out HostedConnectionOwnerId owner))
            return HostedPacketHandoffStatus.UnknownConnection;
        HostedPacketHandoffStatus ownerStatus = ValidateOwner(owner, requireListener: true);
        if (ownerStatus != HostedPacketHandoffStatus.Accepted)
        {
            Interlocked.Increment(ref _closedOrStaleDrops);
            return ownerStatus;
        }
        if (!ValidatePacketInput(kind, payload, pathBinding))
            return HostedPacketHandoffStatus.InvalidInput;
        if (QueueCount(owner, ingress: true) == _limits.MaximumQueuedPacketsPerOwner)
        {
            Interlocked.Increment(ref _destinationFullDrops);
            return HostedPacketHandoffStatus.DestinationFull;
        }
        HostedPacketHandoffStatus rent = TryRentPacket(
            owner, connection, pathGeneration, kind, payload, pathBinding,
            PacketState.IngressQueued, out HostedPacketTransferId id);
        if (rent != HostedPacketHandoffStatus.Accepted)
        {
            if (rent == HostedPacketHandoffStatus.PacketCapacityExceeded)
                Interlocked.Increment(ref _packetCapacityDrops);
            return rent;
        }
        if (!TryEnqueue(owner, ingress: true, id))
        {
            ReleaseCore(id, owner, PacketState.IngressQueued);
            Interlocked.Increment(ref _destinationFullDrops);
            return HostedPacketHandoffStatus.DestinationFull;
        }
        Interlocked.Increment(ref _ingressAccepted);
        return HostedPacketHandoffStatus.Accepted;
    }

    /// <summary>
    /// Routes a clear H0.3 Request/Proof ConnectionId to its owner without
    /// authenticating or authorizing the path mutation.
    /// </summary>
    public HostedPacketHandoffStatus TryOfferPathControl(
        NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> datagram,
        ReadOnlySpan<byte> observedPathBinding)
    {
        EnsureListenerOperational();
        ArgumentNullException.ThrowIfNull(manifest);
        ConnectionId connection;
        if (!HostedUdpPathRebindCodec.TryPeekConnection(
                datagram, HostedPathDatagramKind.Request, manifest, out connection) &&
            !HostedUdpPathRebindCodec.TryPeekConnection(
                datagram, HostedPathDatagramKind.Proof, manifest, out connection))
            return HostedPacketHandoffStatus.InvalidInput;
        return TryOfferIngress(connection, default,
            HostedPacketTransferKind.PathControl, datagram, observedPathBinding);
    }

    public bool TryAcquireIngress(
        HostedConnectionOwnerId owner,
        out HostedPacketTransfer transfer)
    {
        EnsureOwnerOperational(owner);
        if (!TryDequeue(owner, ingress: true, out HostedPacketTransferId id) ||
            !TryAcquireCore(id, owner, PacketState.IngressQueued,
                PacketState.IngressAcquired, out transfer))
        {
            transfer = default;
            return false;
        }
        Interlocked.Increment(ref _ingressAcquired);
        return true;
    }

    public HostedPacketHandoffStatus TryOfferEgress(
        HostedConnectionOwnerId owner,
        ConnectionId connection,
        HostedPathGeneration pathGeneration,
        HostedPacketTransferKind kind,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> pathBinding = default)
    {
        EnsureOwnerOperational(owner);
        if (!ValidatePacketInput(kind, payload, pathBinding))
            return HostedPacketHandoffStatus.InvalidInput;
        if (QueueCount(owner, ingress: false) == _limits.MaximumQueuedPacketsPerOwner)
        {
            Interlocked.Increment(ref _destinationFullDrops);
            return HostedPacketHandoffStatus.DestinationFull;
        }
        HostedPacketHandoffStatus rent = TryRentPacket(
            owner, connection, pathGeneration, kind, payload, pathBinding,
            PacketState.EgressQueued, out HostedPacketTransferId id);
        if (rent != HostedPacketHandoffStatus.Accepted)
        {
            if (rent == HostedPacketHandoffStatus.PacketCapacityExceeded)
                Interlocked.Increment(ref _packetCapacityDrops);
            return rent;
        }
        if (!TryEnqueue(owner, ingress: false, id))
        {
            ReleaseCore(id, owner, PacketState.EgressQueued);
            Interlocked.Increment(ref _destinationFullDrops);
            return HostedPacketHandoffStatus.DestinationFull;
        }
        Interlocked.Increment(ref _egressAccepted);
        return HostedPacketHandoffStatus.Accepted;
    }

    public bool TryAcquireEgress(
        HostedConnectionOwnerId owner,
        out HostedPacketTransfer transfer)
    {
        EnsureListenerOperational();
        if (ValidateOwner(owner, requireListener: true) != HostedPacketHandoffStatus.Accepted ||
            !TryDequeue(owner, ingress: false, out HostedPacketTransferId id) ||
            !TryAcquireCore(id, owner, PacketState.EgressQueued,
                PacketState.EgressAcquired, out transfer))
        {
            transfer = default;
            return false;
        }
        Interlocked.Increment(ref _egressAcquired);
        return true;
    }

    /// <summary>
    /// Borrows the owner-acquired payload until the matching ingress release.
    /// The span must not be retained or read after release or failed-owner close.
    /// </summary>
    public ReadOnlySpan<byte> GetIngressPayload(
        HostedConnectionOwnerId owner,
        in HostedPacketTransfer transfer)
    {
        EnsureOwnerOperational(owner);
        return GetPayloadCore(owner, transfer, PacketState.IngressAcquired);
    }

    /// <summary>
    /// Borrows the listener-acquired payload until the matching egress release.
    /// The span must not be retained or read after release.
    /// </summary>
    public ReadOnlySpan<byte> GetEgressPayload(in HostedPacketTransfer transfer)
    {
        EnsureListenerOperational();
        return GetPayloadCore(transfer.Owner, transfer, PacketState.EgressAcquired);
    }

    public bool TryCopyIngressPathBinding(
        HostedConnectionOwnerId owner,
        in HostedPacketTransfer transfer,
        Span<byte> destination)
    {
        EnsureOwnerOperational(owner);
        return TryCopyPathCore(owner, transfer, PacketState.IngressAcquired, destination);
    }

    public bool TryCopyEgressPathBinding(
        in HostedPacketTransfer transfer,
        Span<byte> destination)
    {
        EnsureListenerOperational();
        return TryCopyPathCore(
            transfer.Owner, transfer, PacketState.EgressAcquired, destination);
    }

    public bool TryReleaseIngress(
        HostedConnectionOwnerId owner,
        HostedPacketTransferId id)
    {
        EnsureOwnerOperational(owner);
        return ReleaseCore(id, owner, PacketState.IngressAcquired);
    }

    public bool TryReleaseEgress(HostedPacketTransferId id)
    {
        EnsureListenerOperational();
        if (!TryGetPacketOwner(id, out HostedConnectionOwnerId owner)) return false;
        return ReleaseCore(id, owner, PacketState.EgressAcquired);
    }

    public HostedPacketHandoffStatus TryCloseOwner(HostedConnectionOwnerId owner)
    {
        EnsureListenerOperational();
        HostedPacketHandoffStatus status = ValidateOwner(owner, requireListener: true);
        if (status != HostedPacketHandoffStatus.Accepted) return status;
        if (OwnerHasBindings(owner) || OwnerHasPackets(owner))
            return HostedPacketHandoffStatus.ConnectionBusy;
        CloseOwnerCore(owner);
        return HostedPacketHandoffStatus.Accepted;
    }

    /// <summary>
    /// Reclaims a failed owner after its thread has stopped. The caller must
    /// establish that quiescence before invoking this listener-owned operation.
    /// </summary>
    public HostedPacketHandoffStatus ForceCloseFailedOwner(
        HostedConnectionOwnerId owner)
    {
        EnsureListenerOperational();
        HostedPacketHandoffStatus status = ValidateOwner(owner, requireListener: true);
        if (status != HostedPacketHandoffStatus.Accepted) return status;
        Volatile.Write(ref _ownerStates[owner.Slot], 0);
        int reclaimed = 0;
        lock (_packetGate)
        {
            for (int slot = 0; slot < _boundConnections.Length; slot++)
            {
                if (_connectionOwners[slot] != owner) continue;
                _boundConnections[slot] = default;
                _connectionOwners[slot] = default;
                _boundConnectionCount--;
            }
            for (int slot = 0; slot < _packetStates.Length; slot++)
            {
                if (_packetOwners[slot] != owner ||
                    (PacketState)_packetStates[slot] is PacketState.Free or PacketState.Retired)
                    continue;
                ClearPacket(slot);
                _packetStates[slot] = (byte)PacketState.Free;
                _packetFree[_packetFreeCount++] = slot;
                _rentedPackets--;
                reclaimed++;
            }
        }
        Interlocked.Add(ref _forcedOwnerReclaims, reclaimed);
        CloseOwnerCore(owner, stateAlreadyClosed: true);
        return HostedPacketHandoffStatus.Accepted;
    }

    public HostedUdpOwnerHandoffSnapshot GetSnapshot()
    {
        EnsureListenerOperational();
        int ingress = 0;
        int egress = 0;
        for (int slot = 0; slot < _ownerStates.Length; slot++)
        {
            ingress = checked(ingress + QueueCount(slot, ingress: true));
            egress = checked(egress + QueueCount(slot, ingress: false));
        }
        int rented;
        lock (_packetGate) rented = _rentedPackets;
        return new(
            _openOwners,
            _boundConnectionCount,
            _packetStates.Length,
            rented,
            _retiredPacketSlots,
            ingress,
            egress,
            checked((ulong)Interlocked.Read(ref _ingressAccepted)),
            checked((ulong)Interlocked.Read(ref _egressAccepted)),
            checked((ulong)Interlocked.Read(ref _ingressAcquired)),
            checked((ulong)Interlocked.Read(ref _egressAcquired)),
            checked((ulong)Interlocked.Read(ref _released)),
            checked((ulong)Interlocked.Read(ref _destinationFullDrops)),
            checked((ulong)Interlocked.Read(ref _packetCapacityDrops)),
            checked((ulong)Interlocked.Read(ref _closedOrStaleDrops)),
            checked((ulong)Interlocked.Read(ref _forcedOwnerReclaims)),
            ReservedBytes);
    }

    public void Dispose()
    {
        EnsureListenerThread();
        if (_disposed) return;
        if (_openOwners != 0 || _boundConnectionCount != 0 || _rentedPackets != 0)
            throw new InvalidOperationException(
                "Every hosted connection owner, binding, and packet lease must close before disposal.");
        _disposed = true;
        Array.Clear(_payloads);
        Array.Clear(_pathBindings);
        Array.Clear(_packetStates);
        Array.Clear(_packetOwners);
        Array.Clear(_packetConnections);
        Array.Clear(_packetPathGenerations);
        Array.Clear(_packetKinds);
        Array.Clear(_packetLengths);
        Array.Clear(_packetHasPath);
        Array.Clear(_ingressQueues);
        Array.Clear(_egressQueues);
    }

    private HostedPacketHandoffStatus TryRentPacket(
        HostedConnectionOwnerId owner,
        ConnectionId connection,
        HostedPathGeneration pathGeneration,
        HostedPacketTransferKind kind,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> pathBinding,
        PacketState state,
        out HostedPacketTransferId id)
    {
        lock (_packetGate)
        {
            if (!TryGetBindingCore(connection, out _, out HostedConnectionOwnerId current))
            {
                id = default;
                return HostedPacketHandoffStatus.UnknownConnection;
            }
            if (current != owner)
            {
                id = default;
                return HostedPacketHandoffStatus.StaleOwner;
            }
            while (_packetFreeCount != 0)
            {
                int slot = _packetFree[--_packetFreeCount];
                uint generation = _packetGenerations[slot];
                if (generation == uint.MaxValue)
                {
                    _packetStates[slot] = (byte)PacketState.Retired;
                    _retiredPacketSlots++;
                    continue;
                }
                generation++;
                _packetGenerations[slot] = generation;
                _packetOwners[slot] = owner;
                _packetConnections[slot] = connection;
                _packetPathGenerations[slot] = pathGeneration;
                _packetKinds[slot] = (byte)kind;
                _packetLengths[slot] = checked((ushort)payload.Length);
                _packetHasPath[slot] = pathBinding.IsEmpty ? (byte)0 : (byte)1;
                payload.CopyTo(PacketPayload(slot));
                if (!pathBinding.IsEmpty) pathBinding.CopyTo(PacketPath(slot));
                _packetStates[slot] = (byte)state;
                _rentedPackets++;
                id = new HostedPacketTransferId(checked((uint)slot), generation);
                return HostedPacketHandoffStatus.Accepted;
            }
        }
        id = default;
        return HostedPacketHandoffStatus.PacketCapacityExceeded;
    }

    private bool TryAcquireCore(
        HostedPacketTransferId id,
        HostedConnectionOwnerId owner,
        PacketState expected,
        PacketState acquired,
        out HostedPacketTransfer transfer)
    {
        lock (_packetGate)
        {
            if (!TryValidatePacket(id, owner, expected, out int slot))
            {
                transfer = default;
                return false;
            }
            _packetStates[slot] = (byte)acquired;
            transfer = new(
                id,
                owner,
                _packetConnections[slot],
                _packetPathGenerations[slot],
                (HostedPacketTransferKind)_packetKinds[slot],
                _packetLengths[slot],
                _packetHasPath[slot] != 0);
            return true;
        }
    }

    private ReadOnlySpan<byte> GetPayloadCore(
        HostedConnectionOwnerId owner,
        in HostedPacketTransfer transfer,
        PacketState expected)
    {
        lock (_packetGate)
        {
            if (!TryValidatePacket(transfer.Id, owner, expected, out int slot) ||
                transfer.Connection != _packetConnections[slot] ||
                transfer.Length != _packetLengths[slot])
                throw new InvalidOperationException(
                    "The hosted packet lease is stale, foreign, or no longer owned.");
            return PacketPayload(slot)[..transfer.Length];
        }
    }

    private bool TryCopyPathCore(
        HostedConnectionOwnerId owner,
        in HostedPacketTransfer transfer,
        PacketState expected,
        Span<byte> destination)
    {
        lock (_packetGate)
        {
            if (!TryValidatePacket(transfer.Id, owner, expected, out int slot) ||
                _packetHasPath[slot] == 0 || destination.Length < PathBindingBytes)
                return false;
            PacketPath(slot).CopyTo(destination);
            return true;
        }
    }

    private bool ReleaseCore(
        HostedPacketTransferId id,
        HostedConnectionOwnerId owner,
        PacketState expected)
    {
        lock (_packetGate)
        {
            if (!TryValidatePacket(id, owner, expected, out int slot)) return false;
            ClearPacket(slot);
            _packetStates[slot] = (byte)PacketState.Free;
            _packetFree[_packetFreeCount++] = slot;
            _rentedPackets--;
        }
        Interlocked.Increment(ref _released);
        return true;
    }

    private bool TryGetPacketOwner(
        HostedPacketTransferId id,
        out HostedConnectionOwnerId owner)
    {
        lock (_packetGate)
        {
            if (!id.IsValid || id.Slot >= (uint)_packetStates.Length ||
                _packetGenerations[id.Slot] != id.Generation ||
                (PacketState)_packetStates[id.Slot] is PacketState.Free or PacketState.Retired)
            {
                owner = default;
                return false;
            }
            owner = _packetOwners[id.Slot];
            return true;
        }
    }

    private bool TryValidatePacket(
        HostedPacketTransferId id,
        HostedConnectionOwnerId owner,
        PacketState expected,
        out int slot)
    {
        if (!id.IsValid || id.Slot >= (uint)_packetStates.Length)
        {
            slot = -1;
            return false;
        }
        slot = checked((int)id.Slot);
        return _packetGenerations[slot] == id.Generation &&
            _packetOwners[slot] == owner &&
            (PacketState)_packetStates[slot] == expected;
    }

    private bool TryEnqueue(
        HostedConnectionOwnerId owner,
        bool ingress,
        HostedPacketTransferId id)
    {
        int slot = checked((int)owner.Slot);
        HostedPacketTransferId[] queue = ingress ? _ingressQueues : _egressQueues;
        long[] writes = ingress ? _ingressWrites : _egressWrites;
        long[] reads = ingress ? _ingressReads : _egressReads;
        long write = Volatile.Read(ref writes[slot]);
        long read = Volatile.Read(ref reads[slot]);
        if (write - read == _limits.MaximumQueuedPacketsPerOwner || write == long.MaxValue)
            return false;
        int index = QueueIndex(slot, write);
        queue[index] = id;
        Volatile.Write(ref writes[slot], write + 1);
        return true;
    }

    private bool TryDequeue(
        HostedConnectionOwnerId owner,
        bool ingress,
        out HostedPacketTransferId id)
    {
        int slot = checked((int)owner.Slot);
        HostedPacketTransferId[] queue = ingress ? _ingressQueues : _egressQueues;
        long[] writes = ingress ? _ingressWrites : _egressWrites;
        long[] reads = ingress ? _ingressReads : _egressReads;
        long read = Volatile.Read(ref reads[slot]);
        long write = Volatile.Read(ref writes[slot]);
        if (read == write)
        {
            id = default;
            return false;
        }
        int index = QueueIndex(slot, read);
        id = queue[index];
        queue[index] = default;
        Volatile.Write(ref reads[slot], read + 1);
        return true;
    }

    private int QueueCount(HostedConnectionOwnerId owner, bool ingress) =>
        QueueCount(checked((int)owner.Slot), ingress);

    private int QueueCount(int ownerSlot, bool ingress)
    {
        long write = Volatile.Read(ref (ingress ? _ingressWrites : _egressWrites)[ownerSlot]);
        long read = Volatile.Read(ref (ingress ? _ingressReads : _egressReads)[ownerSlot]);
        return checked((int)(write - read));
    }

    private int QueueIndex(int ownerSlot, long sequence) => checked(
        ownerSlot * _limits.MaximumQueuedPacketsPerOwner +
        (int)(sequence % _limits.MaximumQueuedPacketsPerOwner));

    private HostedPacketHandoffStatus ValidateOwner(
        HostedConnectionOwnerId owner,
        bool requireListener)
    {
        if (requireListener) EnsureListenerOperational();
        if (!owner.IsValid || owner.Slot >= (uint)_ownerStates.Length)
            return HostedPacketHandoffStatus.StaleOwner;
        int slot = checked((int)owner.Slot);
        if (Volatile.Read(ref _ownerGenerations[slot]) != owner.Generation)
            return HostedPacketHandoffStatus.StaleOwner;
        return Volatile.Read(ref _ownerStates[slot]) == 1
            ? HostedPacketHandoffStatus.Accepted
            : HostedPacketHandoffStatus.DestinationClosed;
    }

    private void EnsureOwnerOperational(HostedConnectionOwnerId owner)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        HostedPacketHandoffStatus status = ValidateOwner(owner, requireListener: false);
        if (status != HostedPacketHandoffStatus.Accepted)
            throw new InvalidOperationException("The hosted connection owner is stale or closed.");
        if (Environment.CurrentManagedThreadId !=
            Volatile.Read(ref _ownerThreadIds[owner.Slot]))
            throw new InvalidOperationException(
                "The hosted connection queue belongs to another owner thread.");
    }

    internal bool TryGetBinding(
        ConnectionId connection,
        out int connectionSlot,
        out HostedConnectionOwnerId owner)
    {
        lock (_packetGate)
            return TryGetBindingCore(connection, out connectionSlot, out owner);
    }

    private bool TryGetBindingCore(
        ConnectionId connection,
        out int connectionSlot,
        out HostedConnectionOwnerId owner)
    {
        if (!TryValidateConnectionSlot(connection, out connectionSlot) ||
            _boundConnections[connectionSlot] != connection)
        {
            owner = default;
            return false;
        }
        owner = _connectionOwners[connectionSlot];
        return true;
    }

    private bool TryValidateConnectionSlot(ConnectionId connection, out int slot)
    {
        if (!connection.IsValid || connection.Slot >= (uint)_boundConnections.Length)
        {
            slot = -1;
            return false;
        }
        slot = checked((int)connection.Slot);
        return true;
    }

    private bool HasOutstandingConnectionPacketsCore(ConnectionId connection)
    {
        for (int slot = 0; slot < _packetStates.Length; slot++)
            if (_packetConnections[slot] == connection &&
                (PacketState)_packetStates[slot] is not PacketState.Free and not PacketState.Retired)
                return true;
        return false;
    }

    private bool OwnerHasPackets(HostedConnectionOwnerId owner)
    {
        lock (_packetGate)
        {
            for (int slot = 0; slot < _packetStates.Length; slot++)
                if (_packetOwners[slot] == owner &&
                    (PacketState)_packetStates[slot] is not PacketState.Free and not PacketState.Retired)
                    return true;
        }
        return false;
    }

    private bool OwnerHasBindings(HostedConnectionOwnerId owner)
    {
        for (int slot = 0; slot < _connectionOwners.Length; slot++)
            if (_connectionOwners[slot] == owner) return true;
        return false;
    }

    private void CloseOwnerCore(
        HostedConnectionOwnerId owner,
        bool stateAlreadyClosed = false)
    {
        int slot = checked((int)owner.Slot);
        if (!stateAlreadyClosed) Volatile.Write(ref _ownerStates[slot], 0);
        _ownerThreadIds[slot] = 0;
        Volatile.Write(ref _ingressWrites[slot], 0);
        Volatile.Write(ref _ingressReads[slot], 0);
        Volatile.Write(ref _egressWrites[slot], 0);
        Volatile.Write(ref _egressReads[slot], 0);
        Array.Clear(_ingressQueues,
            slot * _limits.MaximumQueuedPacketsPerOwner,
            _limits.MaximumQueuedPacketsPerOwner);
        Array.Clear(_egressQueues,
            slot * _limits.MaximumQueuedPacketsPerOwner,
            _limits.MaximumQueuedPacketsPerOwner);
        _ownerFree[_ownerFreeCount++] = slot;
        _openOwners--;
    }

    private bool ValidatePacketInput(
        HostedPacketTransferKind kind,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> pathBinding) =>
        (kind is HostedPacketTransferKind.SecureProtocol or
            HostedPacketTransferKind.PathControl or
            HostedPacketTransferKind.PathRebindCommit) &&
        payload.Length is > 0 &&
        payload.Length <= _limits.MaximumDatagramBytes &&
        (pathBinding.IsEmpty || pathBinding.Length == PathBindingBytes);

    private void ClearPacket(int slot)
    {
        PacketPayload(slot).Clear();
        PacketPath(slot).Clear();
        _packetOwners[slot] = default;
        _packetConnections[slot] = default;
        _packetPathGenerations[slot] = default;
        _packetKinds[slot] = 0;
        _packetLengths[slot] = 0;
        _packetHasPath[slot] = 0;
    }

    private Span<byte> PacketPayload(int slot) =>
        _payloads.AsSpan(slot * _limits.MaximumDatagramBytes, _limits.MaximumDatagramBytes);
    private Span<byte> PacketPath(int slot) =>
        _pathBindings.AsSpan(slot * PathBindingBytes, PathBindingBytes);

    private void EnsureListenerOperational()
    {
        EnsureListenerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureListenerThread()
    {
        if (Environment.CurrentManagedThreadId != _listenerThreadId)
            throw new InvalidOperationException(
                "The hosted packet handoff operation belongs to its listener thread.");
    }
}

/// <summary>
/// Connection-owner endpoint over the fixed bidirectional handoff. It owns no
/// socket and never exposes a mutable packet buffer to the listener owner.
/// </summary>
internal sealed class HostedUdpConnectionOwnerEndpoint : INetworkDatagramEndpoint, IDisposable
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly HostedUdpConnectionOwnerHandoff _handoff;
    private readonly HostedConnectionOwnerId _owner;
    private readonly ConnectionId _connection;
    private readonly int _maximumDatagramBytes;
    private readonly IHostedUdpOwnerPathControlHandler? _pathControl;
    private readonly bool _externallyDemultiplexed;
    private HostedPacketTransfer _stagedIngress;
    private HostedPathGeneration _pathGeneration;
    private ulong _sent;
    private ulong _received;
    private ulong _sentBytes;
    private ulong _receivedBytes;
    private ulong _capacityDrops;
    private ulong _rejected;
    private int _maximumSentDatagramBytes;
    private int _maximumReceivedDatagramBytes;
    private bool _disposed;

    internal HostedUdpConnectionOwnerEndpoint(
        HostedUdpConnectionOwnerHandoff handoff,
        HostedConnectionOwnerId owner,
        ConnectionId connection,
        HostedPathGeneration pathGeneration,
        int maximumDatagramBytes,
        IHostedUdpOwnerPathControlHandler? pathControl = null,
        bool externallyDemultiplexed = false)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        if (!owner.IsValid) throw new ArgumentOutOfRangeException(nameof(owner));
        if (!connection.IsValid) throw new ArgumentOutOfRangeException(nameof(connection));
        if (maximumDatagramBytes is < 256 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maximumDatagramBytes));
        _handoff = handoff;
        _owner = owner;
        _connection = connection;
        _pathGeneration = pathGeneration;
        _maximumDatagramBytes = maximumDatagramBytes;
        _pathControl = pathControl;
        _externallyDemultiplexed = externallyDemultiplexed;
    }

    internal void SetPathGeneration(HostedPathGeneration pathGeneration)
    {
        EnsureOperational();
        if (!pathGeneration.IsValid ||
            (_pathGeneration.IsValid && pathGeneration.Value <= _pathGeneration.Value))
            throw new InvalidOperationException(
                "The connection-owner path generation must advance monotonically.");
        _pathGeneration = pathGeneration;
    }

    public DatagramSendStatus TrySend(ReadOnlySpan<byte> datagram)
    {
        EnsureOwnerThread();
        if (_disposed) return DatagramSendStatus.CapacityExceeded;
        if (datagram.Length > _maximumDatagramBytes)
        {
            _rejected = SaturatingIncrement(_rejected);
            return DatagramSendStatus.DatagramTooLarge;
        }
        HostedPacketHandoffStatus status = _handoff.TryOfferEgress(
            _owner, _connection, _pathGeneration,
            HostedPacketTransferKind.SecureProtocol, datagram);
        if (status == HostedPacketHandoffStatus.Accepted)
        {
            _sent = SaturatingIncrement(_sent);
            _sentBytes = SaturatingAdd(_sentBytes, checked((ulong)datagram.Length));
            _maximumSentDatagramBytes = Math.Max(_maximumSentDatagramBytes, datagram.Length);
            return DatagramSendStatus.Accepted;
        }
        _capacityDrops = SaturatingIncrement(_capacityDrops);
        return DatagramSendStatus.CapacityExceeded;
    }

    public bool TryReceive(Span<byte> destination, out int length)
    {
        EnsureOperational();
        length = 0;
        if (destination.Length < _maximumDatagramBytes)
            throw new ArgumentException(
                "The owner receive destination is below the admitted datagram size.",
                nameof(destination));
        if (_externallyDemultiplexed)
        {
            if (!_stagedIngress.Id.IsValid) return false;
            HostedPacketTransfer staged = _stagedIngress;
            _stagedIngress = default;
            return TryReceiveTransfer(staged, destination, out length);
        }
        while (_handoff.TryAcquireIngress(_owner, out HostedPacketTransfer transfer))
        {
            if (TryReceiveTransfer(transfer, destination, out length)) return true;
        }
        return false;
    }

    internal void StageIngress(HostedPacketTransfer transfer)
    {
        EnsureOperational();
        if (!_externallyDemultiplexed || _stagedIngress.Id.IsValid ||
            transfer.Connection != _connection)
            throw new InvalidOperationException(
                "The owner endpoint cannot stage this ingress transfer.");
        _stagedIngress = transfer;
    }

    internal void ReleaseStagedIngress()
    {
        EnsureOperational();
        if (!_stagedIngress.Id.IsValid) return;
        HostedPacketTransferId id = _stagedIngress.Id;
        _stagedIngress = default;
        if (!_handoff.TryReleaseIngress(_owner, id))
            throw new InvalidOperationException(
                "The owner endpoint could not release its staged packet lease.");
    }

    internal UdpDatagramEndpointSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(
            _sent, _received, _sentBytes, _receivedBytes,
            _capacityDrops, _rejected, 0, 0, 0, 0,
            _maximumSentDatagramBytes, _maximumReceivedDatagramBytes,
            !_disposed, System.Net.Sockets.SocketError.Success);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        ReleaseStagedIngress();
        _disposed = true;
    }

    private bool TryReceiveTransfer(
        HostedPacketTransfer transfer,
        Span<byte> destination,
        out int length)
    {
        length = 0;
        try
        {
            if (transfer.Connection != _connection)
            {
                _rejected = SaturatingIncrement(_rejected);
                return false;
            }
            if (transfer.Kind != HostedPacketTransferKind.SecureProtocol)
            {
                if (_pathControl is null)
                {
                    _rejected = SaturatingIncrement(_rejected);
                    return false;
                }
                Span<byte> path =
                    stackalloc byte[HostedUdpConnectionOwnerHandoff.PathBindingBytes];
                ReadOnlySpan<byte> binding = transfer.HasPathBinding &&
                    _handoff.TryCopyIngressPathBinding(_owner, transfer, path)
                    ? path
                    : default;
                _pathControl.HandlePathControl(
                    transfer.Kind,
                    transfer.PathGeneration,
                    _handoff.GetIngressPayload(_owner, transfer),
                    binding);
                return false;
            }
            if (transfer.PathGeneration != _pathGeneration)
            {
                _rejected = SaturatingIncrement(_rejected);
                return false;
            }
            ReadOnlySpan<byte> payload = _handoff.GetIngressPayload(_owner, transfer);
            payload.CopyTo(destination);
            length = payload.Length;
            _received = SaturatingIncrement(_received);
            _receivedBytes = SaturatingAdd(_receivedBytes, checked((ulong)length));
            _maximumReceivedDatagramBytes = Math.Max(_maximumReceivedDatagramBytes, length);
            return true;
        }
        finally
        {
            if (!_handoff.TryReleaseIngress(_owner, transfer.Id))
                throw new InvalidOperationException(
                    "The owner endpoint could not release its packet lease.");
        }
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                "The hosted connection endpoint belongs to another owner thread.");
    }

    private static ulong SaturatingIncrement(ulong value) =>
        value == ulong.MaxValue ? value : value + 1;

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

/// <summary>
/// Owner-thread sink for authenticated hosted path-control work carried beside,
/// but never interpreted as, secure application traffic.
/// </summary>
internal interface IHostedUdpOwnerPathControlHandler
{
    void HandlePathControl(
        HostedPacketTransferKind kind,
        HostedPathGeneration pathGeneration,
        ReadOnlySpan<byte> datagram,
        ReadOnlySpan<byte> observedPathBinding);
}
