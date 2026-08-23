using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;

namespace UltraSim.Network;

public enum NetworkQueueStatus : byte
{
    Accepted = 0,
    CapacityExceeded = 1,
    MessageTooLarge = 2,
    InvalidDescriptor = 3,
    SequenceExhausted = 4,
    /// <summary>The owning transport lifetime no longer admits new application sends.</summary>
    NotAccepting = 5,
}

public readonly record struct NetworkDeliveredMessage(
    NetworkLane Lane,
    NetworkMessageId MessageId,
    ushort MessageVersion,
    LaneSequence LaneSequence,
    int PayloadLength);

public readonly record struct ProtocolLoopbackSnapshot(
    ulong Step,
    ProtocolHandshakeSnapshot Handshake,
    NetworkProtocolSnapshot Client,
    NetworkProtocolSnapshot Server,
    DeterministicDatagramSnapshot Datagram);

/// <summary>
/// Complete single-owner N0.3 protocol topology over deterministic datagrams.
/// </summary>
/// <remarks>
/// The secure channel authenticates the pinned server endpoint. Game/account identity
/// authentication remains an application Control-lane concern. This topology deliberately
/// does not claim cross-thread packet-memory ownership; real transport handoff is N0.5.
/// </remarks>
public sealed class ProtocolLoopback : IDisposable
{
    private readonly int _ownerThreadId;
    private readonly DeterministicDatagramLink _link;
    private readonly NetworkConnectionTable _connections;
    private readonly bool _ownsIdentity;
    private NetworkServerIdentity? _identity;
    private bool _disposed;
    private ulong _step;

    private ProtocolLoopback(
        DeterministicDatagramLink link,
        NetworkProtocolManifest clientManifest,
        NetworkProtocolManifest serverManifest,
        NetworkProtocolLimits limits,
        NetworkServerIdentity identity,
        bool ownsIdentity,
        ProtocolHandshakeResult handshake)
    {
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _link = link;
        _connections = handshake.Connections;
        _identity = identity;
        _ownsIdentity = ownsIdentity;
        _step = link.GetSnapshot().Step;
        Handshake = handshake.Snapshot;
        Connection = handshake.Connection;
        Client = new NetworkProtocolPeer(
            link.A, clientManifest, limits, link.Limits, handshake.Connection, handshake.SecurityEpoch,
            handshake.MasterSecret, sendsClientToServer: true);
        Server = new NetworkProtocolPeer(
            link.B, serverManifest, limits, link.Limits, handshake.Connection, handshake.SecurityEpoch,
            handshake.MasterSecret, sendsClientToServer: false);
        CryptographicOperations.ZeroMemory(handshake.MasterSecret);
    }

    public ConnectionId Connection { get; }
    public ProtocolHandshakeSnapshot Handshake { get; }
    public NetworkProtocolPeer Client { get; }
    public NetworkProtocolPeer Server { get; }

    public static bool TryCreate(
        NetworkProtocolLimits limits,
        DeterministicDatagramLimits datagramLimits,
        NetworkImpairmentOptions impairment,
        out ProtocolLoopback? loopback,
        out ProtocolHandshakeSnapshot handshake,
        NetworkProtocolManifest? manifest = null,
        NetworkServerIdentity? serverIdentity = null,
        ReadOnlyMemory<byte> trustedServerPublicKey = default,
        int maximumHandshakeSteps = 4_096,
        NetworkProtocolManifest? serverManifest = null)
    {
        limits.Validate(datagramLimits);
        manifest ??= CoreNetworkProtocol.Manifest;
        serverManifest ??= manifest;
        bool ownsIdentity = serverIdentity is null;
        serverIdentity ??= NetworkServerIdentity.CreateEphemeral();
        if (trustedServerPublicKey.IsEmpty) trustedServerPublicKey = serverIdentity.PublicKey.ToArray();
        var link = new DeterministicDatagramLink(datagramLimits, impairment);
        if (!ProtocolLoopbackHandshake.TryEstablish(
                link, manifest, serverManifest, serverIdentity, trustedServerPublicKey.Span,
                maximumHandshakeSteps, out ProtocolHandshakeResult result))
        {
            handshake = default;
            loopback = null;
            if (ownsIdentity) serverIdentity.Dispose();
            return false;
        }

        DrainHandshakeResidue(link, impairment.MaximumDelaySteps);
        loopback = new ProtocolLoopback(link, manifest, serverManifest, limits, serverIdentity, ownsIdentity, result);
        handshake = result.Snapshot;
        return true;
    }

    public void Pump()
    {
        EnsureOperational();
        Client.ProcessInbound(_step);
        Server.ProcessInbound(_step);
        Client.Emit(_step);
        Server.Emit(_step);
        _link.Advance();
        _step++;
    }

    /// <summary>Rotates both send directions only at a drained reliable boundary.</summary>
    public bool TryRotateSecurityEpoch()
    {
        EnsureOperational();
        if (!Client.CanRotateSendSecurityEpoch || !Server.CanRotateSendSecurityEpoch) return false;
        if (!_connections.TryRotateSecurityEpoch(Connection, out SecurityEpoch epoch)) return false;
        if (!Client.TryRotateSendSecurityEpoch() || !Server.TryRotateSendSecurityEpoch() ||
            Client.GetSnapshot().SendSecurityEpoch != epoch || Server.GetSnapshot().SendSecurityEpoch != epoch)
            throw new InvalidOperationException("The connection and secure peers did not rotate atomically.");
        return true;
    }

    public ProtocolLoopbackSnapshot GetSnapshot()
    {
        EnsureOperational();
        return new(_step, Handshake, Client.GetSnapshot(), Server.GetSnapshot(), _link.GetSnapshot());
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        Client.Dispose();
        Server.Dispose();
        if (_connections.TryGetState(Connection, out NetworkConnectionState state) &&
            state != NetworkConnectionState.Closed)
            _ = _connections.TryAdvance(Connection, NetworkConnectionState.Closed);
        _ = _connections.TryReleaseClosed(Connection);
        if (_ownsIdentity) _identity!.Dispose();
        _identity = null;
        _disposed = true;
    }

    private static void DrainHandshakeResidue(DeterministicDatagramLink link, ushort maximumDelay)
    {
        byte[] scratch = new byte[link.Limits.MaximumDatagramBytes];
        for (int step = 0; step <= maximumDelay + 1; step++)
        {
            while (link.A.TryReceive(scratch, out _)) { }
            while (link.B.TryReceive(scratch, out _)) { }
            link.Advance();
        }
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        if (_disposed) throw new ObjectDisposedException(nameof(ProtocolLoopback));
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("ProtocolLoopback is bound to its creating owner thread.");
    }
}

/// <summary>One bounded endpoint of the secure N0.3 protocol.</summary>
public sealed class NetworkProtocolPeer : IDisposable
{
    private readonly int _ownerThreadId;
    private INetworkDatagramEndpoint _endpoint;
    private readonly NetworkProtocolManifest _manifest;
    private readonly NetworkProtocolLimits _limits;
    private readonly int _maximumUnreliableSendFramesPerDatagram;
    private readonly int _maximumUnreliableReceiveFramesPerDatagram;
    private readonly DeterministicDatagramLimits _datagramLimits;
    private readonly ConnectionId _connection;
    private readonly NetworkDirectionalCryptography _sendCryptography;
    private readonly NetworkDirectionalCryptography _receiveCryptography;
    private readonly byte[] _wireScratch;
    private readonly byte[] _plaintextScratch;
    private readonly OutgoingMessage[] _reliable;
    private readonly byte[] _reliablePayloads;
    private readonly int[] _reliablePayloadOffsets;
    private readonly int[] _reliableCompactOrder;
    private readonly ulong[] _lastSentSteps;
    private readonly int[] _reliableFree;
    private readonly OutgoingMessage[] _independent;
    private readonly byte[] _independentPayloads;
    private readonly int _independentPayloadStride;
    private readonly int[] _independentFree;
    private readonly SentPacketRecord[] _sentPackets;
    private readonly int[] _sentPacketFree;
    private readonly int[] _sentRecordByFragment;
    private readonly ulong[] _sentPacketMapKeys;
    private readonly int[] _sentPacketMapValues;
    private readonly UnreliableStreamState[] _unreliableStreams;
    private readonly int[] _unreliableHeads;
    private readonly byte[] _unreliablePayloads;
    private readonly int[] _unreliablePayloadOffsets;
    private readonly ulong[] _nextReliableSendSequences;
    private readonly ulong[] _nextReliableReceiveSequences;
    private readonly ReassemblyState[] _reassembly;
    private readonly byte[] _reassemblyPayloads;
    private readonly int[] _reassemblyPayloadOffsets;
    private readonly NetworkDeliveredMessage[] _received;
    private readonly byte[] _receivedPayloads;
    private readonly int[] _receivedPayloadOffsets;
    private readonly int[] _receivedCompactOrder;
    private readonly int[] _receivedFree;
    private readonly int[] _receivedQueue;
    private int _reliableFreeCount;
    private int _reliableCount;
    private int _independentFreeCount;
    private int _independentCount;
    private int _sentPacketFreeCount;
    private int _nextIndependentSchedule;
    private int _receivedFreeCount;
    private int _receivedHead;
    private int _receivedCount;
    private int _reliablePayloadTail;
    private int _receivedPayloadTail;
    private ulong _nextPacketNumber;
    private ulong _sentPacketsCount;
    private ulong _receivedPacketsCount;
    private ulong _authenticationFailures;
    private ulong _replayRejections;
    private ulong _retransmissions;
    private ulong _acknowledgements;
    private ulong _sequencedDrops;
    private ulong _capacityDrops;
    private ulong _dataPacketsSent;
    private ulong _acknowledgementPacketsSent;
    private ulong _keepAlivePacketsSent;
    private ulong _encryptedPlaintextBytes;
    private ulong _decryptedPlaintextBytes;
    private ulong _reliableFragmentsSent;
    private ulong _reliableRetransmittedWireBytes;
    private ulong _unreliableFramesSent;
    private ulong _unreliableDatagramsSent;
    private ulong _unreliableFramesReceived;
    private ulong _unreliableDatagramsReceived;
    private ulong _unreliableSentDatagramsWithTwoOrMoreFrames;
    private ulong _unreliableReceivedDatagramsWithTwoOrMoreFrames;
    private ulong _unreliableSentHistogramOne;
    private ulong _unreliableSentHistogramTwo;
    private ulong _unreliableSentHistogramThree;
    private ulong _unreliableSentHistogramFour;
    private ulong _unreliableSentHistogramFive;
    private ulong _unreliableSentHistogramSix;
    private ulong _unreliableSentHistogramSeven;
    private ulong _unreliableSentHistogramEightOrMore;
    private ulong _unreliableReceivedHistogramOne;
    private ulong _unreliableReceivedHistogramTwo;
    private ulong _unreliableReceivedHistogramThree;
    private ulong _unreliableReceivedHistogramFour;
    private ulong _unreliableReceivedHistogramFive;
    private ulong _unreliableReceivedHistogramSix;
    private ulong _unreliableReceivedHistogramSeven;
    private ulong _unreliableReceivedHistogramEightOrMore;
    private ulong _unreliableSentPlaintextBytes;
    private ulong _unreliableReceivedPlaintextBytes;
    private int _maximumUnreliableSentFramesPerDatagram;
    private int _maximumUnreliableReceivedFramesPerDatagram;
    private int _maximumUnreliableSentPlaintextBytes;
    private int _maximumUnreliableReceivedPlaintextBytes;
    private int _reliableRetainedBytes;
    private int _reliableRetainedHighWaterBytes;
    private int _independentRetainedBytes;
    private int _independentRetainedHighWaterBytes;
    private int _unreliableRetainedBytes;
    private int _unreliableRetainedHighWaterBytes;
    private int _reassemblyRetainedBytes;
    private int _reassemblyRetainedHighWaterBytes;
    private int _receivedRetainedBytes;
    private int _receivedRetainedHighWaterBytes;
    private int _largestRetainedMessageBytes;
    private ulong _retainedByteCapacityFailures;
    private ulong _reliablePayloadCompactions;
    private ulong _reliablePayloadCompactionBytesMoved;
    private int _reliablePayloadMaximumBytesMovedPerCompaction;
    private ulong _receivedPayloadCompactions;
    private ulong _receivedPayloadCompactionBytesMoved;
    private int _receivedPayloadMaximumBytesMovedPerCompaction;
    private NetworkPacketWindow _authenticatedPackets;
    private NetworkPacketWindow _transportDeliveries;
    private bool _ackPending;
    private int _inFlight;
    private int _congestionWindow;
    private int _congestionAckCredit;
    private ulong _smoothedRtt;
    private ulong _rttVariation;
    private ulong _rto;
    private ulong _nextLossCheckStep = ulong.MaxValue;
    private int _nextLossDeadlineCount;
    private readonly bool _usesMonotonicPacing;
    private ulong _nextPacedDataStep;
    private bool _stageTimingEnabled;
    private ulong _emitTicks;
    private ulong _emitCalls;
    private ulong _idleEmitCalls;
    private ulong _selectionTicks;
    private ulong _frameEncodeTicks;
    private ulong _secureSendTicks;
    private ulong _endpointSendTicks;
    private ulong _inboundTicks;
    private ulong _inboundCalls;
    private ulong _emptyInboundCalls;
    private ulong _inboundDatagrams;
    private ulong _endpointReceiveTicks;
    private ulong _secureReceiveTicks;
    private ulong _frameAdmissionTicks;
    private ulong _reliableCompactionTicks;
    private ulong _receivedCompactionTicks;
    private ulong _stageOutboundUnreliableFrames;
    private ulong _stageInboundUnreliableFrames;
    private bool _acceptsNewSends = true;
    private bool _disposed;

    internal NetworkProtocolPeer(
        INetworkDatagramEndpoint endpoint,
        NetworkProtocolManifest manifest,
        NetworkProtocolLimits limits,
        DeterministicDatagramLimits datagramLimits,
        ConnectionId connection,
        SecurityEpoch epoch,
        ReadOnlySpan<byte> masterSecret,
        bool sendsClientToServer,
        bool usesMonotonicPacing = false)
    {
        limits.Validate(datagramLimits);
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _endpoint = endpoint;
        _manifest = manifest;
        _limits = limits;
        _maximumUnreliableSendFramesPerDatagram = Math.Min(
            limits.MaximumUnreliableFramesPerDatagram,
            manifest.MaximumUnreliableDataFramesPerDatagram);
        _maximumUnreliableReceiveFramesPerDatagram =
            manifest.MaximumUnreliableDataFramesPerDatagram;
        if ((long)limits.MaximumInboundPacketsPerPump *
                _maximumUnreliableReceiveFramesPerDatagram >
            NetworkProtocolLimits.MaximumUnreliableFramesPerPumpBound)
            throw new ArgumentOutOfRangeException(nameof(manifest),
                "The manifest receive capability exceeds the bounded-work contract.");
        _datagramLimits = datagramLimits;
        _connection = connection;
        _usesMonotonicPacing = usesMonotonicPacing;
        _sendCryptography = new(masterSecret, sendsClientToServer, epoch);
        _receiveCryptography = new(masterSecret, !sendsClientToServer, epoch);
        _wireScratch = new byte[datagramLimits.MaximumDatagramBytes];
        _plaintextScratch = new byte[NetworkSecureDatagramFraming.MaximumPlaintextBytes(datagramLimits.MaximumDatagramBytes)];
        NetworkPayloadReservationLayout payloadLayout = NetworkPayloadReservationLayout.Create(
            manifest, limits, FragmentPayloadBytes);
        _reliable = new OutgoingMessage[limits.MaximumReliableMessages];
        _reliablePayloads = new byte[checked((int)Math.Min(
            (long)limits.MaximumReliableMessages * payloadLayout.ReliableStride,
            limits.ReliablePayloadByteCapacity))];
        _reliablePayloadOffsets = new int[limits.MaximumReliableMessages];
        Array.Fill(_reliablePayloadOffsets, -1);
        _reliableCompactOrder = new int[limits.MaximumReliableMessages];
        _lastSentSteps = new ulong[checked(limits.MaximumReliableMessages * limits.MaximumFragmentsPerMessage)];
        _reliableFree = new int[limits.MaximumReliableMessages];
        for (int index = 0; index < _reliableFree.Length; index++) _reliableFree[index] = _reliableFree.Length - 1 - index;
        _reliableFreeCount = _reliableFree.Length;
        _independent = new OutgoingMessage[limits.MaximumIndependentUnreliableMessages];
        _independentPayloadStride = payloadLayout.IndependentStride;
        _independentPayloads = new byte[checked(
            limits.MaximumIndependentUnreliableMessages * _independentPayloadStride)];
        _independentFree = new int[limits.MaximumIndependentUnreliableMessages];
        for (int index = 0; index < _independentFree.Length; index++)
            _independentFree[index] = _independentFree.Length - 1 - index;
        _independentFreeCount = _independentFree.Length;
        _sentPackets = new SentPacketRecord[limits.SentPacketCapacity];
        _sentPacketFree = new int[limits.SentPacketCapacity];
        for (int index = 0; index < _sentPacketFree.Length; index++)
            _sentPacketFree[index] = _sentPacketFree.Length - 1 - index;
        _sentPacketFreeCount = _sentPacketFree.Length;
        _sentRecordByFragment = new int[_lastSentSteps.Length];
        Array.Fill(_sentRecordByFragment, -1);
        int sentPacketMapCapacity = checked((int)BitOperations.RoundUpToPowerOf2(
            checked((uint)limits.SentPacketCapacity * 2U)));
        _sentPacketMapKeys = new ulong[sentPacketMapCapacity];
        _sentPacketMapValues = new int[sentPacketMapCapacity];
        Array.Fill(_sentPacketMapValues, -1);
        _unreliableStreams = new UnreliableStreamState[manifest.Count];
        _unreliableHeads = new int[NetworkLaneContracts.Count];
        Array.Fill(_unreliableHeads, -1);
        _unreliablePayloadOffsets = payloadLayout.UnreliableOffsets;
        _unreliablePayloads = new byte[_unreliablePayloadOffsets[^1]];
        _nextReliableSendSequences = new ulong[NetworkLaneContracts.Count];
        _nextReliableReceiveSequences = new ulong[NetworkLaneContracts.Count];
        _reassembly = new ReassemblyState[NetworkLaneContracts.Count];
        _reassemblyPayloadOffsets = payloadLayout.ReassemblyOffsets;
        _reassemblyPayloads = new byte[_reassemblyPayloadOffsets[^1]];
        _received = new NetworkDeliveredMessage[limits.MaximumReceivedMessages];
        _receivedPayloads = new byte[checked((int)Math.Min(
            (long)limits.MaximumReceivedMessages * payloadLayout.ReceivedStride,
            limits.ReceivedPayloadByteCapacity))];
        _receivedPayloadOffsets = new int[limits.MaximumReceivedMessages];
        Array.Fill(_receivedPayloadOffsets, -1);
        _receivedCompactOrder = new int[limits.MaximumReceivedMessages];
        _receivedFree = new int[limits.MaximumReceivedMessages];
        _receivedQueue = new int[limits.MaximumReceivedMessages];
        for (int index = 0; index < _receivedFree.Length; index++) _receivedFree[index] = _receivedFree.Length - 1 - index;
        _receivedFreeCount = _receivedFree.Length;
        _congestionWindow = limits.InitialCongestionWindowPackets;
        _rto = (ulong)limits.MinimumRetransmissionSteps;
    }

    public NetworkQueueStatus TryQueue(NetworkMessageId messageId, ReadOnlySpan<byte> payload)
    {
        EnsureOperational();
        if (!_acceptsNewSends) return NetworkQueueStatus.NotAccepting;
        if (!_manifest.TryGet(messageId, out NetworkMessageDescriptor descriptor, out int messageSlot))
            return NetworkQueueStatus.InvalidDescriptor;
        if (payload.Length > descriptor.MaximumEncodedBytes || payload.Length > _limits.MaximumMessageBytes)
            return NetworkQueueStatus.MessageTooLarge;
        NetworkLaneContract lane = NetworkLaneContracts.Get(descriptor.Lane);
        int fragmentPayload = FragmentPayloadBytes;
        int fragmentCount = Math.Max(1, (payload.Length + fragmentPayload - 1) / fragmentPayload);
        if (fragmentCount > _limits.MaximumFragmentsPerMessage || (fragmentCount > 1 && !lane.AllowsFragmentation))
            return NetworkQueueStatus.MessageTooLarge;
        int laneIndex = (int)descriptor.Lane;
        ulong sequence = lane.Delivery == NetworkDeliveryGuarantee.Reliable
            ? _nextReliableSendSequences[laneIndex]
            : _unreliableStreams[messageSlot].NextSendSequence;
        if (sequence == ulong.MaxValue) return NetworkQueueStatus.SequenceExhausted;

        if (lane.Delivery == NetworkDeliveryGuarantee.Reliable)
        {
            if (_reliableFreeCount == 0) return NetworkQueueStatus.CapacityExceeded;
            int slot = _reliableFree[_reliableFreeCount - 1];
            if (!TryAllocateReliablePayload(slot, payload.Length))
            {
                _retainedByteCapacityFailures = SaturatingIncrement(_retainedByteCapacityFailures);
                return NetworkQueueStatus.CapacityExceeded;
            }
            _reliableFreeCount--;
            payload.CopyTo(ReliablePayload(slot, payload.Length));
            _lastSentSteps.AsSpan(slot * _limits.MaximumFragmentsPerMessage,
                _limits.MaximumFragmentsPerMessage).Clear();
            _reliable[slot] = new(true, descriptor.Lane, descriptor.Id, descriptor.Version,
                new LaneSequence(sequence), payload.Length, (ushort)fragmentCount, 0);
            _reliableCount++;
            AddRetainedBytes(ref _reliableRetainedBytes, ref _reliableRetainedHighWaterBytes, payload.Length);
        }
        else if (lane.Retention == NetworkRetentionPolicy.IndependentState)
        {
            if (_independentFreeCount == 0) return NetworkQueueStatus.CapacityExceeded;
            int slot = _independentFree[--_independentFreeCount];
            payload.CopyTo(IndependentPayload(slot));
            _independent[slot] = new(true, descriptor.Lane, descriptor.Id, descriptor.Version,
                new LaneSequence(sequence), payload.Length, 1, 0);
            _independentCount++;
            AddRetainedBytes(ref _independentRetainedBytes, ref _independentRetainedHighWaterBytes, payload.Length);
            _unreliableStreams[messageSlot].NextSendSequence = sequence + 1;
        }
        else
        {
            ref UnreliableStreamState stream = ref _unreliableStreams[messageSlot];
            if (stream.Pending) _unreliableRetainedBytes -= stream.Message.Length;
            payload.CopyTo(UnreliablePayload(messageSlot));
            stream.Message = new(true, descriptor.Lane, descriptor.Id, descriptor.Version,
                new LaneSequence(sequence), payload.Length, 1, 0);
            if (!stream.Pending &&
                (_unreliableHeads[laneIndex] < 0 || messageSlot < _unreliableHeads[laneIndex]))
            {
                _unreliableHeads[laneIndex] = messageSlot;
            }
            stream.Pending = true;
            stream.NextSendSequence = sequence + 1;
            AddRetainedBytes(ref _unreliableRetainedBytes, ref _unreliableRetainedHighWaterBytes, payload.Length);
        }
        if (lane.Delivery == NetworkDeliveryGuarantee.Reliable)
            _nextReliableSendSequences[laneIndex] = sequence + 1;
        return NetworkQueueStatus.Accepted;
    }

    public int ReliableQueued
    {
        get { EnsureOperational(); return _reliableCount; }
    }

    public int IndependentUnreliableQueued
    {
        get { EnsureOperational(); return _independentCount; }
    }

    public int ReceivedQueued
    {
        get { EnsureOperational(); return _receivedCount; }
    }

    /// <summary>
    /// Maximum application-message bytes that fit in one authenticated protocol data frame.
    /// Nonfragmenting lanes must keep each queued message at or below this value.
    /// </summary>
    public int MaximumUnfragmentedMessageBytes
    {
        get { EnsureOperational(); return FragmentPayloadBytes; }
    }

    public bool TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload)
    {
        EnsureOperational();
        if (_receivedCount == 0)
        {
            message = default;
            payload = default;
            return false;
        }
        int slot = _receivedQueue[_receivedHead];
        message = _received[slot];
        payload = ReceivedPayload(slot, message.PayloadLength);
        return true;
    }

    public bool TryConsumeReceived()
    {
        EnsureOperational();
        if (_receivedCount == 0) return false;
        int slot = _receivedQueue[_receivedHead];
        _receivedHead = (_receivedHead + 1) % _receivedQueue.Length;
        _receivedCount--;
        int payloadLength = _received[slot].PayloadLength;
        ReceivedPayload(slot, payloadLength).Clear();
        _receivedPayloadOffsets[slot] = -1;
        _receivedRetainedBytes -= payloadLength;
        _received[slot] = default;
        _receivedFree[_receivedFreeCount++] = slot;
        if (_receivedCount == 0) _receivedPayloadTail = 0;
        return true;
    }

    public bool TryRotateSendSecurityEpoch()
    {
        EnsureOperational();
        if (_reliableCount != 0 || _inFlight != 0 || !_sendCryptography.Epoch.TryNext(out SecurityEpoch next))
            return false;
        if (!_sendCryptography.TryAdvanceTo(next)) return false;
        _nextPacketNumber = 0;
        Array.Clear(_sentPackets);
        Array.Clear(_sentPacketMapKeys);
        Array.Fill(_sentPacketMapValues, -1);
        Array.Fill(_sentRecordByFragment, -1);
        for (int index = 0; index < _sentPacketFree.Length; index++)
            _sentPacketFree[index] = _sentPacketFree.Length - 1 - index;
        _sentPacketFreeCount = _sentPacketFree.Length;
        return true;
    }

    internal bool CanRotateSendSecurityEpoch =>
        !_disposed && _reliableCount == 0 && _inFlight == 0 && _sendCryptography.Epoch.Value != uint.MaxValue;

    internal ulong ReceivedPacketCount => _receivedPacketsCount;

    /// <summary>
    /// Scans currently pending unreliable frames and reports how many secure Data
    /// datagrams an immediate, order-preserving greedy pack would require. It never
    /// waits for more work and does not inspect or mutate reliable flight state.
    /// </summary>
    internal NetworkUnreliableBatchOpportunity InspectUnreliableBatchOpportunity()
    {
        EnsureOperational();
        int maximumPlaintext = NetworkSecureDatagramFraming.MaximumPlaintextBytes(
            _datagramLimits.MaximumDatagramBytes);
        int ready = 0;
        int bytes = 0;
        int greedyDatagrams = 0;
        int currentDatagramBytes = 0;
        Span<int> sameLaneDatagramBytes = stackalloc int[NetworkLaneContracts.Count];
        int sameLaneGreedyDatagrams = 0;

        for (int index = 0; index < _independent.Length; index++)
            if (_independent[index].Active) Add(_independent[index], sameLaneDatagramBytes);
        for (int index = 0; index < _unreliableStreams.Length; index++)
            if (_unreliableStreams[index].Pending)
                Add(_unreliableStreams[index].Message, sameLaneDatagramBytes);

        return new(ready, bytes, greedyDatagrams, sameLaneGreedyDatagrams);

        void Add(in OutgoingMessage message, Span<int> sameLaneBytes)
        {
            int frameBytes = checked(NetworkProtocolDataHeader.EncodedBytes + message.Length);
            if (frameBytes > maximumPlaintext)
                throw new InvalidOperationException("An admitted unreliable frame exceeds one secure datagram.");
            ready++;
            bytes = checked(bytes + frameBytes);
            if (currentDatagramBytes == 0 ||
                checked(currentDatagramBytes + frameBytes) > maximumPlaintext)
            {
                greedyDatagrams++;
                currentDatagramBytes = frameBytes;
            }
            else
            {
                currentDatagramBytes += frameBytes;
            }

            int laneIndex = (int)message.Lane;
            if (sameLaneBytes[laneIndex] == 0 ||
                checked(sameLaneBytes[laneIndex] + frameBytes) > maximumPlaintext)
            {
                sameLaneGreedyDatagrams++;
                sameLaneBytes[laneIndex] = frameBytes;
            }
            else
            {
                sameLaneBytes[laneIndex] += frameBytes;
            }
        }
    }

    internal void ResetStageTiming(bool enabled)
    {
        EnsureOperational();
        _emitTicks = 0;
        _emitCalls = 0;
        _idleEmitCalls = 0;
        _selectionTicks = 0;
        _frameEncodeTicks = 0;
        _secureSendTicks = 0;
        _endpointSendTicks = 0;
        _inboundTicks = 0;
        _inboundCalls = 0;
        _emptyInboundCalls = 0;
        _inboundDatagrams = 0;
        _endpointReceiveTicks = 0;
        _secureReceiveTicks = 0;
        _frameAdmissionTicks = 0;
        _reliableCompactionTicks = 0;
        _receivedCompactionTicks = 0;
        _stageOutboundUnreliableFrames = 0;
        _stageInboundUnreliableFrames = 0;
        _stageTimingEnabled = enabled;
    }

    internal NetworkProtocolStageTiming GetStageTiming()
    {
        EnsureOperational();
        return new(
            _emitTicks,
            _emitCalls,
            _idleEmitCalls,
            _selectionTicks,
            _frameEncodeTicks,
            _secureSendTicks,
            _endpointSendTicks,
            _inboundTicks,
            _inboundCalls,
            _emptyInboundCalls,
            _inboundDatagrams,
            _endpointReceiveTicks,
            _secureReceiveTicks,
            _frameAdmissionTicks,
            _reliableCompactionTicks,
            _receivedCompactionTicks,
            _stageOutboundUnreliableFrames,
            _stageInboundUnreliableFrames);
    }

    /// <summary>
    /// Closes new application admission while retaining ACK/retransmission work
    /// already owned by this protocol peer.
    /// </summary>
    internal void StopAcceptingNewSends()
    {
        EnsureOperational();
        _acceptsNewSends = false;
    }

    internal bool TryEmitKeepAlive()
    {
        EnsureOperational();
        return TryEncryptAndSend(
            NetworkSecureDatagramKind.KeepAlive,
            ReadOnlySpan<byte>.Empty,
            out _,
            out DatagramSendStatus status) &&
            status is DatagramSendStatus.Accepted or DatagramSendStatus.DroppedByImpairment;
    }

    internal int ProcessInbound(ulong step)
    {
        EnsureOperational();
        long inboundStarted = StartStageTiming();
        int budget = _limits.MaximumInboundPacketsPerPump;
        int processed = 0;
        while (budget-- > 0)
        {
            long receiveStarted = StartStageTiming();
            bool received = _endpoint.TryReceive(_wireScratch, out int length);
            RecordStageTiming(ref _endpointReceiveTicks, receiveStarted);
            if (!received) break;
            ProcessDatagram(_wireScratch.AsSpan(0, length), step);
            processed++;
        }
        RecordStageTiming(ref _inboundTicks, inboundStarted);
        if (_stageTimingEnabled)
        {
            _inboundCalls = SaturatingIncrement(_inboundCalls);
            if (processed == 0) _emptyInboundCalls = SaturatingIncrement(_emptyInboundCalls);
            _inboundDatagrams = SaturatingAdd(_inboundDatagrams, checked((ulong)processed));
        }
        return processed;
    }

    internal void Emit(ulong step)
    {
        EnsureOperational();
        long emitStarted = StartStageTiming();
        ulong sentBefore = _stageTimingEnabled ? _sentPacketsCount : 0;
        if (_inFlight != 0) ProcessExpiredFlights(step);
        int budget = _limits.MaximumPacketsPerPump;
        if (_ackPending && budget != 0 && TryEmitAcknowledgement()) budget--;

        if (_usesMonotonicPacing && step < _nextPacedDataStep)
        {
            RecordStageTiming(ref _emitTicks, emitStarted);
            RecordEmitCall(sentBefore);
            return;
        }

        if (_maximumUnreliableSendFramesPerDatagram == 1)
            EmitScalarData(step, ref budget);
        else
            EmitAggregatedData(step, ref budget);
        RecordStageTiming(ref _emitTicks, emitStarted);
        RecordEmitCall(sentBefore);
    }

    private void EmitScalarData(ulong step, ref int budget)
    {
        while (budget > 0 && _inFlight < _congestionWindow)
        {
            long selectionStarted = StartStageTiming();
            bool selected = TrySelectNext(out int reliableSlot, out ushort fragment,
                out int independentSlot, out int unreliableSlot);
            RecordStageTiming(ref _selectionTicks, selectionStarted);
            if (!selected) break;
            bool reliable = reliableSlot >= 0;
            if (!TryEmitData(reliableSlot, fragment, independentSlot,
                    unreliableSlot, step, reliable)) break;
            budget--;
            if (ShouldStopAfterData(step)) break;
        }
    }

    private void EmitAggregatedData(ulong step, ref int budget)
    {
        Span<UnreliableSelection> aggregate =
            stackalloc UnreliableSelection[_maximumUnreliableSendFramesPerDatagram];
        while (budget > 0 && _inFlight < _congestionWindow)
        {
            long selectionStarted = StartStageTiming();
            bool selected = TrySelectNext(out int reliableSlot, out ushort fragment,
                out int independentSlot, out int unreliableSlot);
            RecordStageTiming(ref _selectionTicks, selectionStarted);
            if (!selected) break;
            bool reliable = reliableSlot >= 0;
            if (reliable)
            {
                if (!TryEmitData(reliableSlot, fragment, independentSlot,
                        unreliableSlot, step, reliable)) break;
            }
            else
            {
                int aggregateCount = 1;
                aggregate[0] = new(independentSlot, unreliableSlot);
                int aggregatePlaintextBytes = GetUnreliableFrameBytes(independentSlot, unreliableSlot);
                int maximumPlaintextBytes = NetworkSecureDatagramFraming.MaximumPlaintextBytes(
                    _datagramLimits.MaximumDatagramBytes);
                while (aggregateCount < aggregate.Length)
                {
                    long nextSelectionStarted = StartStageTiming();
                    bool nextSelected = TrySelectNext(aggregate[..aggregateCount],
                        out int nextReliableSlot, out _,
                        out int nextIndependentSlot, out int nextUnreliableSlot);
                    RecordStageTiming(ref _selectionTicks, nextSelectionStarted);
                    if (!nextSelected || nextReliableSlot >= 0) break;
                    int nextFrameBytes = GetUnreliableFrameBytes(
                        nextIndependentSlot, nextUnreliableSlot);
                    if (checked(aggregatePlaintextBytes + nextFrameBytes) > maximumPlaintextBytes)
                        break;
                    aggregate[aggregateCount++] = new(nextIndependentSlot, nextUnreliableSlot);
                    aggregatePlaintextBytes += nextFrameBytes;
                }
                if (!TryEmitUnreliableFrames(aggregate[..aggregateCount])) break;
            }
            budget--;
            if (ShouldStopAfterData(step)) break;
        }
    }

    private bool ShouldStopAfterData(ulong step)
    {
        if (!_usesMonotonicPacing || _smoothedRtt == 0) return false;
        ulong interval = _smoothedRtt /
            checked((ulong)Math.Max(1, _congestionWindow));
        if (interval == 0) return false;
        _nextPacedDataStep = checked(step + interval);
        return true;
    }

    private void RecordEmitCall(ulong sentBefore)
    {
        if (!_stageTimingEnabled) return;
        _emitCalls = SaturatingIncrement(_emitCalls);
        if (_sentPacketsCount == sentBefore)
            _idleEmitCalls = SaturatingIncrement(_idleEmitCalls);
    }

    public NetworkProtocolSnapshot GetSnapshot()
    {
        EnsureOperational();
        long reserved = CalculateReservedBytes();
        return new(true, _sendCryptography.Epoch, _receiveCryptography.Epoch,
            _sentPacketsCount, _receivedPacketsCount, _authenticationFailures, _replayRejections,
            _retransmissions, _acknowledgements, _sequencedDrops, _capacityDrops,
            _dataPacketsSent, _acknowledgementPacketsSent, _keepAlivePacketsSent,
            _encryptedPlaintextBytes, _decryptedPlaintextBytes, _reliableFragmentsSent,
            _reliableRetransmittedWireBytes,
            _reliableCount, _independentCount, _receivedCount, _congestionWindow,
            _inFlight, _smoothedRtt, _rto, reserved,
            new NetworkRetainedPayloadSnapshot(
                _reliableRetainedBytes, _reliableRetainedHighWaterBytes, _reliablePayloads.Length,
                _independentRetainedBytes, _independentRetainedHighWaterBytes, _independentPayloads.Length,
                _unreliableRetainedBytes, _unreliableRetainedHighWaterBytes, _unreliablePayloads.Length,
                _reassemblyRetainedBytes, _reassemblyRetainedHighWaterBytes, _reassemblyPayloads.Length,
                _receivedRetainedBytes, _receivedRetainedHighWaterBytes, _receivedPayloads.Length,
                _largestRetainedMessageBytes, _retainedByteCapacityFailures,
                _reliablePayloadCompactions, _reliablePayloadCompactionBytesMoved,
                _reliablePayloadMaximumBytesMovedPerCompaction,
                _receivedPayloadCompactions, _receivedPayloadCompactionBytesMoved,
                _receivedPayloadMaximumBytesMovedPerCompaction),
            new NetworkUnreliableAggregationSnapshot(
                _maximumUnreliableSendFramesPerDatagram,
                _unreliableFramesSent, _unreliableDatagramsSent,
                _unreliableFramesReceived, _unreliableDatagramsReceived,
                _unreliableSentDatagramsWithTwoOrMoreFrames,
                _unreliableReceivedDatagramsWithTwoOrMoreFrames,
                _unreliableSentHistogramOne, _unreliableSentHistogramTwo,
                _unreliableSentHistogramThree, _unreliableSentHistogramFour,
                _unreliableSentHistogramFive, _unreliableSentHistogramSix,
                _unreliableSentHistogramSeven, _unreliableSentHistogramEightOrMore,
                _unreliableReceivedHistogramOne, _unreliableReceivedHistogramTwo,
                _unreliableReceivedHistogramThree, _unreliableReceivedHistogramFour,
                _unreliableReceivedHistogramFive, _unreliableReceivedHistogramSix,
                _unreliableReceivedHistogramSeven, _unreliableReceivedHistogramEightOrMore,
                _unreliableSentPlaintextBytes, _unreliableReceivedPlaintextBytes,
                _maximumUnreliableSentFramesPerDatagram,
                _maximumUnreliableReceivedFramesPerDatagram,
                _maximumUnreliableSentPlaintextBytes,
                _maximumUnreliableReceivedPlaintextBytes));
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _sendCryptography.Dispose();
        _receiveCryptography.Dispose();
        CryptographicOperations.ZeroMemory(_reliablePayloads);
        CryptographicOperations.ZeroMemory(_independentPayloads);
        CryptographicOperations.ZeroMemory(_unreliablePayloads);
        CryptographicOperations.ZeroMemory(_reassemblyPayloads);
        CryptographicOperations.ZeroMemory(_receivedPayloads);
        CryptographicOperations.ZeroMemory(_wireScratch);
        CryptographicOperations.ZeroMemory(_plaintextScratch);
        _disposed = true;
    }

    internal void ReplaceEndpoint(
        INetworkDatagramEndpoint expected,
        INetworkDatagramEndpoint replacement)
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!ReferenceEquals(_endpoint, expected))
            throw new InvalidOperationException(
                "The secure peer endpoint replacement has stale ownership.");
        _endpoint = replacement;
    }

    private int FragmentPayloadBytes =>
        NetworkSecureDatagramFraming.MaximumPlaintextBytes(_datagramLimits.MaximumDatagramBytes) -
        NetworkProtocolDataHeader.EncodedBytes;

    private readonly struct UnreliableSelection
    {
        internal UnreliableSelection(int independentSlot, int unreliableSlot)
        {
            IndependentSlot = independentSlot;
            UnreliableSlot = unreliableSlot;
        }

        internal int IndependentSlot { get; }
        internal int UnreliableSlot { get; }
    }

    private readonly struct ValidatedDataFrame
    {
        internal ValidatedDataFrame(
            NetworkDataFrameHeader header,
            int messageSlot,
            int fragmentOffset,
            int messageFragmentOffset)
        {
            Header = header;
            MessageSlot = messageSlot;
            FragmentOffset = fragmentOffset;
            MessageFragmentOffset = messageFragmentOffset;
        }

        internal NetworkDataFrameHeader Header { get; }
        internal int MessageSlot { get; }
        internal int FragmentOffset { get; }
        internal int MessageFragmentOffset { get; }
    }

    private bool TrySelectNext(
        out int reliableSlot,
        out ushort fragment,
        out int independentSlot,
        out int unreliableSlot)
    {
        return TrySelectNext(ReadOnlySpan<UnreliableSelection>.Empty,
            out reliableSlot, out fragment, out independentSlot, out unreliableSlot);
    }

    private bool TrySelectNext(
        ReadOnlySpan<UnreliableSelection> excluded,
        out int reliableSlot,
        out ushort fragment,
        out int independentSlot,
        out int unreliableSlot)
    {
        foreach (NetworkLane lane in NetworkLaneContracts.PriorityOrder)
        {
            int selected = FindReliableHead(lane);
            if (selected >= 0 && TryFindDueFragment(selected, out fragment))
            {
                reliableSlot = selected;
                independentSlot = -1;
                unreliableSlot = -1;
                return true;
            }
            int independent = FindIndependentPending(lane, excluded);
            if (independent >= 0)
            {
                reliableSlot = -1;
                fragment = 0;
                independentSlot = independent;
                unreliableSlot = -1;
                return true;
            }
            int pending = FindUnreliablePending(lane, excluded);
            if (pending >= 0)
            {
                reliableSlot = -1;
                fragment = 0;
                independentSlot = -1;
                unreliableSlot = pending;
                return true;
            }
        }
        reliableSlot = -1;
        fragment = 0;
        independentSlot = -1;
        unreliableSlot = -1;
        return false;
    }

    private int FindIndependentPending(NetworkLane lane, ReadOnlySpan<UnreliableSelection> excluded)
    {
        for (int offset = 0; offset < _independent.Length; offset++)
        {
            int index = (_nextIndependentSchedule + offset) % _independent.Length;
            if (_independent[index].Active && _independent[index].Lane == lane &&
                !IsExcluded(excluded, index, -1)) return index;
        }
        return -1;
    }

    private int FindUnreliablePending(NetworkLane lane, ReadOnlySpan<UnreliableSelection> excluded)
    {
        int head = _unreliableHeads[(int)lane];
        if (head < 0 || !IsExcluded(excluded, -1, head)) return head;
        return FindNextUnreliablePending(lane, excluded);
    }

    private int FindNextUnreliablePending(NetworkLane lane)
    {
        return FindNextUnreliablePending(lane, ReadOnlySpan<UnreliableSelection>.Empty);
    }

    private int FindNextUnreliablePending(
        NetworkLane lane,
        ReadOnlySpan<UnreliableSelection> excluded)
    {
        for (int index = 0; index < _unreliableStreams.Length; index++)
            if (_unreliableStreams[index].Pending && _unreliableStreams[index].Message.Lane == lane &&
                !IsExcluded(excluded, -1, index)) return index;
        return -1;
    }

    private static bool IsExcluded(
        ReadOnlySpan<UnreliableSelection> excluded,
        int independentSlot,
        int unreliableSlot)
    {
        for (int index = 0; index < excluded.Length; index++)
        {
            UnreliableSelection selection = excluded[index];
            if (selection.IndependentSlot == independentSlot &&
                selection.UnreliableSlot == unreliableSlot) return true;
        }
        return false;
    }

    private int FindReliableHead(NetworkLane lane)
    {
        int selected = -1;
        ulong sequence = ulong.MaxValue;
        for (int index = 0; index < _reliable.Length; index++)
        {
            OutgoingMessage candidate = _reliable[index];
            if (candidate.Active && candidate.Lane == lane && candidate.Sequence.Value < sequence)
            {
                selected = index;
                sequence = candidate.Sequence.Value;
            }
        }
        return selected;
    }

    private bool TryFindDueFragment(int slot, out ushort fragment)
    {
        OutgoingMessage message = _reliable[slot];
        Span<ulong> sent = _lastSentSteps.AsSpan(slot * _limits.MaximumFragmentsPerMessage,
            _limits.MaximumFragmentsPerMessage);
        for (ushort index = 0; index < message.FragmentCount; index++)
        {
            ulong bit = 1UL << index;
            if ((message.AcknowledgedMask & bit) != 0) continue;
            ulong marker = sent[index];
            if (marker is 0 or ulong.MaxValue) { fragment = index; return true; }
        }
        fragment = 0;
        return false;
    }

    private int GetUnreliableFrameBytes(int independentSlot, int unreliableSlot)
    {
        OutgoingMessage message = independentSlot >= 0
            ? _independent[independentSlot]
            : _unreliableStreams[unreliableSlot].Message;
        return checked(NetworkProtocolDataHeader.EncodedBytes + message.Length);
    }

    private bool TryEmitUnreliableFrames(
        ReadOnlySpan<UnreliableSelection> selections)
    {
        if (selections.IsEmpty) return false;
        long frameStarted = StartStageTiming();
        var writer = new NetworkPacketWriter(_plaintextScratch);
        for (int index = 0; index < selections.Length; index++)
        {
            UnreliableSelection selection = selections[index];
            bool independent = selection.IndependentSlot >= 0;
            OutgoingMessage message = independent
                ? _independent[selection.IndependentSlot]
                : _unreliableStreams[selection.UnreliableSlot].Message;
            ReadOnlySpan<byte> payload = independent
                ? IndependentPayload(selection.IndependentSlot)[..message.Length]
                : UnreliablePayload(selection.UnreliableSlot)[..message.Length];
            var frame = new NetworkDataFrameHeader(
                message.Lane, message.MessageId, message.MessageVersion, message.Sequence,
                0, 1, message.Length, checked((ushort)message.Length));
            if (!NetworkDataFrameCodec.TryWrite(ref writer, frame) ||
                !writer.TryWriteBytes(payload))
                throw new InvalidOperationException(
                    "A validated unreliable aggregate exceeded its fixed plaintext buffer.");
        }
        RecordStageTiming(ref _frameEncodeTicks, frameStarted);

        if (!TryEncryptAndSend(NetworkSecureDatagramKind.Data, writer.WrittenSpan,
                out _, out DatagramSendStatus status)) return false;
        if (status is not (DatagramSendStatus.Accepted or DatagramSendStatus.DroppedByImpairment))
            return false;

        for (int index = 0; index < selections.Length; index++)
            ConsumeUnreliableSelection(selections[index]);
        _unreliableFramesSent = SaturatingAdd(
            _unreliableFramesSent, checked((ulong)selections.Length));
        _unreliableDatagramsSent = SaturatingIncrement(_unreliableDatagramsSent);
        _unreliableSentPlaintextBytes = SaturatingAdd(
            _unreliableSentPlaintextBytes, checked((ulong)writer.WrittenCount));
        _maximumUnreliableSentFramesPerDatagram = Math.Max(
            _maximumUnreliableSentFramesPerDatagram, selections.Length);
        _maximumUnreliableSentPlaintextBytes = Math.Max(
            _maximumUnreliableSentPlaintextBytes, writer.WrittenCount);
        RecordUnreliableHistogram(sent: true, selections.Length);
        if (_stageTimingEnabled)
            _stageOutboundUnreliableFrames = SaturatingAdd(
                _stageOutboundUnreliableFrames, checked((ulong)selections.Length));
        return true;
    }

    private void ConsumeUnreliableSelection(UnreliableSelection selection)
    {
        if (selection.IndependentSlot >= 0)
        {
            FreeIndependent(selection.IndependentSlot);
            _nextIndependentSchedule = (selection.IndependentSlot + 1) % _independent.Length;
            return;
        }

        int unreliableSlot = selection.UnreliableSlot;
        ref UnreliableStreamState stream = ref _unreliableStreams[unreliableSlot];
        NetworkLane lane = stream.Message.Lane;
        _unreliableRetainedBytes -= stream.Message.Length;
        stream.Pending = false;
        stream.Message = default;
        UnreliablePayload(unreliableSlot).Clear();
        _unreliableHeads[(int)lane] = FindNextUnreliablePending(lane);
    }

    private void RecordUnreliableHistogram(bool sent, int frameCount)
    {
        if (frameCount >= 2)
        {
            if (sent)
                _unreliableSentDatagramsWithTwoOrMoreFrames =
                    SaturatingIncrement(_unreliableSentDatagramsWithTwoOrMoreFrames);
            else
                _unreliableReceivedDatagramsWithTwoOrMoreFrames =
                    SaturatingIncrement(_unreliableReceivedDatagramsWithTwoOrMoreFrames);
        }

        if (sent)
        {
            switch (frameCount)
            {
                case 1: _unreliableSentHistogramOne = SaturatingIncrement(_unreliableSentHistogramOne); break;
                case 2: _unreliableSentHistogramTwo = SaturatingIncrement(_unreliableSentHistogramTwo); break;
                case 3: _unreliableSentHistogramThree = SaturatingIncrement(_unreliableSentHistogramThree); break;
                case 4: _unreliableSentHistogramFour = SaturatingIncrement(_unreliableSentHistogramFour); break;
                case 5: _unreliableSentHistogramFive = SaturatingIncrement(_unreliableSentHistogramFive); break;
                case 6: _unreliableSentHistogramSix = SaturatingIncrement(_unreliableSentHistogramSix); break;
                case 7: _unreliableSentHistogramSeven = SaturatingIncrement(_unreliableSentHistogramSeven); break;
                default: _unreliableSentHistogramEightOrMore = SaturatingIncrement(_unreliableSentHistogramEightOrMore); break;
            }
        }
        else
        {
            switch (frameCount)
            {
                case 1: _unreliableReceivedHistogramOne = SaturatingIncrement(_unreliableReceivedHistogramOne); break;
                case 2: _unreliableReceivedHistogramTwo = SaturatingIncrement(_unreliableReceivedHistogramTwo); break;
                case 3: _unreliableReceivedHistogramThree = SaturatingIncrement(_unreliableReceivedHistogramThree); break;
                case 4: _unreliableReceivedHistogramFour = SaturatingIncrement(_unreliableReceivedHistogramFour); break;
                case 5: _unreliableReceivedHistogramFive = SaturatingIncrement(_unreliableReceivedHistogramFive); break;
                case 6: _unreliableReceivedHistogramSix = SaturatingIncrement(_unreliableReceivedHistogramSix); break;
                case 7: _unreliableReceivedHistogramSeven = SaturatingIncrement(_unreliableReceivedHistogramSeven); break;
                default: _unreliableReceivedHistogramEightOrMore = SaturatingIncrement(_unreliableReceivedHistogramEightOrMore); break;
            }
        }
    }

    private bool TryEmitData(
        int reliableSlot,
        ushort fragmentIndex,
        int independentSlot,
        int unreliableSlot,
        ulong step,
        bool reliable)
    {
        bool independent = independentSlot >= 0;
        OutgoingMessage message = reliable
            ? _reliable[reliableSlot]
            : independent
                ? _independent[independentSlot]
                : _unreliableStreams[unreliableSlot].Message;
        ReadOnlySpan<byte> payload = reliable
            ? ReliablePayload(reliableSlot, message.Length)
            : independent
                ? IndependentPayload(independentSlot)[..message.Length]
                : UnreliablePayload(unreliableSlot)[..message.Length];
        int fragmentPayload = FragmentPayloadBytes;
        int offset = reliable ? fragmentIndex * fragmentPayload : 0;
        int length = reliable ? Math.Min(fragmentPayload, message.Length - offset) : message.Length;
        long frameStarted = StartStageTiming();
        var frame = new NetworkDataFrameHeader(
            message.Lane, message.MessageId, message.MessageVersion, message.Sequence,
            fragmentIndex, message.FragmentCount, message.Length, checked((ushort)length));
        var writer = new NetworkPacketWriter(_plaintextScratch);
        if (!NetworkDataFrameCodec.TryWrite(ref writer, frame) ||
            !writer.TryWriteBytes(payload.Slice(offset, length)))
            throw new InvalidOperationException("A validated protocol frame exceeded its fixed plaintext buffer.");
        RecordStageTiming(ref _frameEncodeTicks, frameStarted);

        int reservedSentRecord = reliable && _sentPacketFreeCount != 0
            ? _sentPacketFree[_sentPacketFreeCount - 1]
            : -1;
        if (reliable && reservedSentRecord < 0) return false;
        int sentMarkerIndex = reliable
            ? reliableSlot * _limits.MaximumFragmentsPerMessage + fragmentIndex
            : -1;
        bool retransmission = reliable && _lastSentSteps[sentMarkerIndex] == ulong.MaxValue;

        if (!TryEncryptAndSend(NetworkSecureDatagramKind.Data, writer.WrittenSpan,
                out PacketNumber packet, out DatagramSendStatus status)) return false;
        if (status is DatagramSendStatus.Accepted or DatagramSendStatus.DroppedByImpairment)
        {
            if (reliable)
            {
                _reliableFragmentsSent = SaturatingIncrement(_reliableFragmentsSent);
                if (retransmission)
                    _reliableRetransmittedWireBytes = SaturatingAdd(
                        _reliableRetransmittedWireBytes,
                        checked((ulong)(NetworkSecureDatagramFraming.HeaderBytes +
                            writer.WrittenCount + NetworkSecureDatagramFraming.TagBytes)));
                _sentPackets[reservedSentRecord] = new(
                    packet.Value, step, reliableSlot, checked((uint)_rto), fragmentIndex, true);
                _sentPacketFreeCount--;
                AddSentPacketIndex(packet.Value, reservedSentRecord);
                _sentRecordByFragment[sentMarkerIndex] = reservedSentRecord;
                ulong deadline = _sentPackets[reservedSentRecord].DeadlineStep;
                if (_nextLossCheckStep != 0)
                {
                    if (deadline < _nextLossCheckStep)
                    {
                        _nextLossCheckStep = deadline;
                        _nextLossDeadlineCount = 1;
                    }
                    else if (deadline == _nextLossCheckStep)
                    {
                        _nextLossDeadlineCount++;
                    }
                }
                _lastSentSteps[reliableSlot * _limits.MaximumFragmentsPerMessage + fragmentIndex] = step + 1;
                _inFlight++;
            }
            else
            {
                ConsumeUnreliableSelection(new(independentSlot, unreliableSlot));
            }
            return true;
        }
        return false;
    }

    private bool TryEmitAcknowledgement()
    {
        if (!_transportDeliveries.TryGetRange(out ulong largest, out ulong mask))
        {
            _ackPending = false;
            return false;
        }
        var writer = new NetworkPacketWriter(_plaintextScratch);
        if (!writer.TryWriteUInt64(largest) || !writer.TryWriteUInt64(mask))
            throw new InvalidOperationException("The acknowledgement buffer is invalid.");
        if (!TryEncryptAndSend(NetworkSecureDatagramKind.Acknowledgement, writer.WrittenSpan,
                out _, out DatagramSendStatus status)) return false;
        if (status is DatagramSendStatus.Accepted or DatagramSendStatus.DroppedByImpairment)
        {
            _ackPending = false;
            return true;
        }
        return false;
    }

    private bool TryEncryptAndSend(NetworkSecureDatagramKind kind, ReadOnlySpan<byte> plaintext,
        out PacketNumber packet, out DatagramSendStatus status)
    {
        if (_nextPacketNumber == ulong.MaxValue)
        {
            packet = default;
            status = DatagramSendStatus.CapacityExceeded;
            return false;
        }
        packet = new PacketNumber(_nextPacketNumber);
        long secureStarted = StartStageTiming();
        var header = new NetworkSecureDatagramHeader(
            _manifest.Version, _connection, _sendCryptography.Epoch, packet, kind,
            checked((ushort)plaintext.Length));
        var headerWriter = new NetworkPacketWriter(_wireScratch.AsSpan(0, NetworkSecureDatagramFraming.HeaderBytes));
        if (!NetworkSecureDatagramFraming.TryWriteHeader(ref headerWriter, header))
            throw new InvalidOperationException("The secure datagram header is invalid.");
        Span<byte> ciphertext = _wireScratch.AsSpan(NetworkSecureDatagramFraming.HeaderBytes, plaintext.Length);
        Span<byte> tag = _wireScratch.AsSpan(NetworkSecureDatagramFraming.HeaderBytes + plaintext.Length,
            NetworkSecureDatagramFraming.TagBytes);
        _sendCryptography.Encrypt(packet, plaintext, ciphertext, tag,
            _wireScratch.AsSpan(0, NetworkSecureDatagramFraming.HeaderBytes));
        RecordStageTiming(ref _secureSendTicks, secureStarted);
        long endpointStarted = StartStageTiming();
        status = _endpoint.TrySend(_wireScratch.AsSpan(0,
            NetworkSecureDatagramFraming.HeaderBytes + plaintext.Length + NetworkSecureDatagramFraming.TagBytes));
        RecordStageTiming(ref _endpointSendTicks, endpointStarted);
        if (status is DatagramSendStatus.Accepted or DatagramSendStatus.DroppedByImpairment)
        {
            _nextPacketNumber++;
            _sentPacketsCount = SaturatingIncrement(_sentPacketsCount);
            _encryptedPlaintextBytes = SaturatingAdd(
                _encryptedPlaintextBytes, checked((ulong)plaintext.Length));
            switch (kind)
            {
                case NetworkSecureDatagramKind.Data:
                    _dataPacketsSent = SaturatingIncrement(_dataPacketsSent);
                    break;
                case NetworkSecureDatagramKind.Acknowledgement:
                    _acknowledgementPacketsSent = SaturatingIncrement(_acknowledgementPacketsSent);
                    break;
                case NetworkSecureDatagramKind.KeepAlive:
                    _keepAlivePacketsSent = SaturatingIncrement(_keepAlivePacketsSent);
                    break;
            }
            return true;
        }
        return false;
    }

    private void ProcessDatagram(ReadOnlySpan<byte> datagram, ulong step)
    {
        long secureStarted = StartStageTiming();
        if (!NetworkSecureDatagramFraming.TryReadExact(datagram, out NetworkSecureDatagramHeader header,
                out ReadOnlySpan<byte> associated, out ReadOnlySpan<byte> ciphertext, out ReadOnlySpan<byte> tag) ||
            header.ProtocolVersion != _manifest.Version || header.Connection != _connection)
        {
            RecordStageTiming(ref _secureReceiveTicks, secureStarted);
            _authenticationFailures++;
            return;
        }

        uint receiveEpoch = _receiveCryptography.Epoch.Value;
        bool potentialAdvance = receiveEpoch != uint.MaxValue && header.SecurityEpoch.Value == receiveEpoch + 1;
        NetworkPacketWindow candidateAuthentication = potentialAdvance ? default : _authenticatedPackets;
        if (!candidateAuthentication.TryPrepare(header.PacketNumber,
                out NetworkPacketWindowTransition authenticationTransition))
        {
            RecordStageTiming(ref _secureReceiveTicks, secureStarted);
            _replayRejections++;
            return;
        }
        Span<byte> plaintext = _plaintextScratch.AsSpan(0, ciphertext.Length);
        if (!_receiveCryptography.TryDecryptAtEpoch(header.SecurityEpoch, header.PacketNumber,
                ciphertext, tag, plaintext, associated, out bool advanced))
        {
            RecordStageTiming(ref _secureReceiveTicks, secureStarted);
            _authenticationFailures++;
            return;
        }
        _decryptedPlaintextBytes = SaturatingAdd(
            _decryptedPlaintextBytes, checked((ulong)plaintext.Length));

        // Authentication and delivery are distinct facts. An authentic packet number is
        // consumed even when bounded semantic admission must defer it; reliable retry uses
        // a fresh packet number and remains unacknowledged until that retry is admitted.
        if (advanced)
        {
            if (!_receiveCryptography.CommitAdvance(header.SecurityEpoch))
                throw new InvalidOperationException("An authenticated next security epoch could not be committed.");
            _authenticatedPackets.Reset();
            _transportDeliveries.Reset();
            _ackPending = false;
        }
        _authenticatedPackets.Commit(authenticationTransition);
        RecordStageTiming(ref _secureReceiveTicks, secureStarted);

        if (header.Kind == NetworkSecureDatagramKind.Acknowledgement)
        {
            if (ProcessAcknowledgement(plaintext, step)) _receivedPacketsCount++;
            return;
        }

        if (header.Kind == NetworkSecureDatagramKind.KeepAlive)
        {
            if (!plaintext.IsEmpty) return;
            _receivedPacketsCount++;
            return;
        }

        long admissionStarted = StartStageTiming();
        NetworkDeliveryAdmission admission = ProcessData(plaintext, out bool acknowledgementEliciting);
        RecordStageTiming(ref _frameAdmissionTicks, admissionStarted);
        if (admission != NetworkDeliveryAdmission.Admitted) return;
        if (!_transportDeliveries.TryPrepare(header.PacketNumber,
                out NetworkPacketWindowTransition deliveryTransition))
            throw new InvalidOperationException("An authenticated admitted packet could not enter the delivery window.");
        _transportDeliveries.Commit(deliveryTransition);
        _receivedPacketsCount++;
        _ackPending |= acknowledgementEliciting;
    }

    private NetworkDeliveryAdmission ProcessData(
        ReadOnlySpan<byte> plaintext,
        out bool acknowledgementEliciting)
    {
        if (_maximumUnreliableReceiveFramesPerDatagram == 1)
            return ProcessSingleData(plaintext, out acknowledgementEliciting);

        acknowledgementEliciting = false;
        var reader = new NetworkPacketReader(plaintext);
        Span<ValidatedDataFrame> frames =
            stackalloc ValidatedDataFrame[_maximumUnreliableReceiveFramesPerDatagram];
        int frameCount = 0;
        bool hasReliable = false;
        int fragmentPayload = FragmentPayloadBytes;

        while (reader.Remaining > 0)
        {
            if (frameCount == frames.Length ||
                !NetworkDataFrameCodec.TryRead(ref reader, out NetworkDataFrameHeader header) ||
                header.MessageLength > _limits.MaximumMessageBytes ||
                header.FragmentCount > _limits.MaximumFragmentsPerMessage)
                return NetworkDeliveryAdmission.Invalid;
            if (!_manifest.TryGet(header.MessageId,
                    out NetworkMessageDescriptor descriptor, out int messageSlot) ||
                descriptor.Version != header.MessageVersion || descriptor.Lane != header.Lane ||
                header.MessageLength > descriptor.MaximumEncodedBytes)
                return NetworkDeliveryAdmission.Invalid;

            int fragmentOffset = reader.ConsumedCount;
            if (!reader.TryReadBytes(header.FragmentLength, out _) ||
                !ValidateDataFrame(header, fragmentPayload, out int messageFragmentOffset))
                return NetworkDeliveryAdmission.Invalid;
            NetworkLaneContract contract = NetworkLaneContracts.Get(header.Lane);
            bool reliable = contract.Delivery == NetworkDeliveryGuarantee.Reliable;
            hasReliable |= reliable;
            frames[frameCount++] = new(
                header, messageSlot, fragmentOffset, messageFragmentOffset);
        }

        if (frameCount == 0 || (hasReliable && frameCount != 1))
            return NetworkDeliveryAdmission.Invalid;

        if (hasReliable)
        {
            ValidatedDataFrame frame = frames[0];
            NetworkDeliveryAdmission reliable = ProcessReliableFragment(
                frame.Header,
                plaintext.Slice(frame.FragmentOffset, frame.Header.FragmentLength),
                frame.MessageFragmentOffset);
            acknowledgementEliciting = reliable == NetworkDeliveryAdmission.Admitted;
            return reliable;
        }

        RecordReceivedUnreliableDatagram(frameCount, plaintext.Length);

        for (int index = 0; index < frameCount; index++)
        {
            ValidatedDataFrame frame = frames[index];
            ProcessUnreliableFrame(
                frame.Header,
                frame.MessageSlot,
                plaintext.Slice(frame.FragmentOffset, frame.Header.FragmentLength));
        }
        return NetworkDeliveryAdmission.Admitted;
    }

    private NetworkDeliveryAdmission ProcessSingleData(
        ReadOnlySpan<byte> plaintext,
        out bool acknowledgementEliciting)
    {
        acknowledgementEliciting = false;
        var reader = new NetworkPacketReader(plaintext);
        if (!NetworkDataFrameCodec.TryRead(ref reader, out NetworkDataFrameHeader header) ||
            header.MessageLength > _limits.MaximumMessageBytes ||
            header.FragmentCount > _limits.MaximumFragmentsPerMessage)
            return NetworkDeliveryAdmission.Invalid;
        if (!_manifest.TryGet(header.MessageId,
                out NetworkMessageDescriptor descriptor, out int messageSlot) ||
            descriptor.Version != header.MessageVersion || descriptor.Lane != header.Lane ||
            header.MessageLength > descriptor.MaximumEncodedBytes)
            return NetworkDeliveryAdmission.Invalid;

        int fragmentOffset = reader.ConsumedCount;
        if (!reader.TryReadBytes(header.FragmentLength, out ReadOnlySpan<byte> fragment) ||
            !reader.IsComplete ||
            !ValidateDataFrame(header, FragmentPayloadBytes,
                out int messageFragmentOffset))
            return NetworkDeliveryAdmission.Invalid;

        NetworkLaneContract contract = NetworkLaneContracts.Get(header.Lane);
        if (contract.Delivery == NetworkDeliveryGuarantee.Reliable)
        {
            NetworkDeliveryAdmission reliable = ProcessReliableFragment(
                header, fragment, messageFragmentOffset);
            acknowledgementEliciting = reliable == NetworkDeliveryAdmission.Admitted;
            return reliable;
        }

        return ProcessUnreliableFrame(header, messageSlot, fragment);
    }

    private void RecordReceivedUnreliableDatagram(int frameCount, int plaintextBytes)
    {
        _unreliableFramesReceived = SaturatingAdd(
            _unreliableFramesReceived, checked((ulong)frameCount));
        _unreliableDatagramsReceived = SaturatingIncrement(_unreliableDatagramsReceived);
        _unreliableReceivedPlaintextBytes = SaturatingAdd(
            _unreliableReceivedPlaintextBytes, checked((ulong)plaintextBytes));
        _maximumUnreliableReceivedFramesPerDatagram = Math.Max(
            _maximumUnreliableReceivedFramesPerDatagram, frameCount);
        _maximumUnreliableReceivedPlaintextBytes = Math.Max(
            _maximumUnreliableReceivedPlaintextBytes, plaintextBytes);
        RecordUnreliableHistogram(sent: false, frameCount);
        if (_stageTimingEnabled)
            _stageInboundUnreliableFrames = SaturatingAdd(
                _stageInboundUnreliableFrames, checked((ulong)frameCount));
    }

    private bool ValidateDataFrame(
        in NetworkDataFrameHeader header,
        int fragmentPayload,
        out int messageFragmentOffset)
    {
        messageFragmentOffset = header.FragmentIndex * fragmentPayload;
        int expectedLength = Math.Min(
            fragmentPayload, header.MessageLength - messageFragmentOffset);
        NetworkLaneContract contract = NetworkLaneContracts.Get(header.Lane);
        return messageFragmentOffset >= 0 && messageFragmentOffset <= header.MessageLength &&
            expectedLength == header.FragmentLength &&
            (header.FragmentCount == 1 || contract.AllowsFragmentation) &&
            header.FragmentCount == Math.Max(1,
                (header.MessageLength + fragmentPayload - 1) / fragmentPayload);
    }

    private NetworkDeliveryAdmission ProcessUnreliableFrame(
        in NetworkDataFrameHeader header,
        int messageSlot,
        ReadOnlySpan<byte> fragment)
    {
        NetworkLaneContract contract = NetworkLaneContracts.Get(header.Lane);
        ref UnreliableStreamState unreliableStream = ref _unreliableStreams[messageSlot];
        if (contract.Order == NetworkDeliveryOrder.Sequenced &&
            unreliableStream.ReceiveSequenceInitialized &&
            header.LaneSequence.Value <= unreliableStream.LastReceiveSequence)
        {
            _sequencedDrops++;
            return NetworkDeliveryAdmission.Admitted;
        }
        if (contract.Order == NetworkDeliveryOrder.Sequenced)
        {
            unreliableStream.ReceiveSequenceInitialized = true;
            unreliableStream.LastReceiveSequence = header.LaneSequence.Value;
        }
        if (_receivedFreeCount == 0)
        {
            _capacityDrops++;
            return NetworkDeliveryAdmission.Admitted;
        }
        if (!TryEnqueueReceived(header.Lane, header.MessageId, header.MessageVersion,
                header.LaneSequence, fragment))
        {
            _capacityDrops++;
            return NetworkDeliveryAdmission.Admitted;
        }
        return NetworkDeliveryAdmission.Admitted;
    }

    private NetworkDeliveryAdmission ProcessReliableFragment(
        in NetworkDataFrameHeader header,
        ReadOnlySpan<byte> fragment,
        int offset)
    {
        int laneIndex = (int)header.Lane;
        ulong expected = _nextReliableReceiveSequences[laneIndex];
        if (header.LaneSequence.Value < expected) return NetworkDeliveryAdmission.Admitted;
        if (header.LaneSequence.Value > expected) return NetworkDeliveryAdmission.Deferred;
        ref ReassemblyState state = ref _reassembly[laneIndex];
        if (!state.Active)
            state = new(true, header.MessageId, header.MessageVersion, header.LaneSequence,
                header.FragmentCount, header.MessageLength, 0);
        else if (state.MessageId != header.MessageId || state.MessageVersion != header.MessageVersion ||
            state.Sequence != header.LaneSequence || state.FragmentCount != header.FragmentCount ||
            state.MessageLength != header.MessageLength)
            return NetworkDeliveryAdmission.Invalid;

        ulong bit = 1UL << header.FragmentIndex;
        if ((state.ReceivedMask & bit) == 0)
        {
            fragment.CopyTo(ReassemblyPayload(header.Lane).Slice(offset, fragment.Length));
            state = state with { ReceivedMask = state.ReceivedMask | bit };
            AddRetainedBytes(ref _reassemblyRetainedBytes, ref _reassemblyRetainedHighWaterBytes,
                fragment.Length);
        }
        ulong completeMask = header.FragmentCount == 64 ? ulong.MaxValue : (1UL << header.FragmentCount) - 1;
        if (state.ReceivedMask != completeMask) return NetworkDeliveryAdmission.Admitted;
        if (_receivedFreeCount == 0)
        {
            _capacityDrops++;
            return NetworkDeliveryAdmission.Deferred;
        }
        if (!TryEnqueueReceived(header.Lane, header.MessageId, header.MessageVersion,
                header.LaneSequence, ReassemblyPayload(header.Lane)[..header.MessageLength]))
        {
            _capacityDrops++;
            return NetworkDeliveryAdmission.Deferred;
        }
        ReassemblyPayload(header.Lane).Clear();
        _reassemblyRetainedBytes -= header.MessageLength;
        state = default;
        _nextReliableReceiveSequences[laneIndex] = expected + 1;
        return NetworkDeliveryAdmission.Admitted;
    }

    private bool ProcessAcknowledgement(ReadOnlySpan<byte> plaintext, ulong step)
    {
        var reader = new NetworkPacketReader(plaintext);
        if (!reader.TryReadUInt64(out ulong largest) || !reader.TryReadUInt64(out ulong mask) ||
            !reader.IsComplete || (mask & 1UL) == 0) return false;
        for (int bit = 0; bit < 64; bit++)
        {
            if ((mask & (1UL << bit)) == 0 || largest < (ulong)bit) continue;
            AcknowledgePacket(largest - (ulong)bit, step);
        }
        return true;
    }

    private void AcknowledgePacket(ulong packet, ulong step)
    {
        if (!TryGetSentPacketIndex(packet, out int index)) return;
        SentPacketRecord record = _sentPackets[index];
        if (!record.Active || record.PacketNumber != packet)
            throw new InvalidOperationException("The sent-packet index disagrees with retained flight state.");
        ReleaseSentRecord(index, in record);
        ulong sample = Math.Max(1, step - record.SentStep);
        UpdateRtt(sample);
        if ((uint)record.MessageSlot < (uint)_reliable.Length && _reliable[record.MessageSlot].Active)
        {
            OutgoingMessage message = _reliable[record.MessageSlot];
            ulong bit = 1UL << record.FragmentIndex;
            if ((message.AcknowledgedMask & bit) == 0)
            {
                message = message with { AcknowledgedMask = message.AcknowledgedMask | bit };
                _reliable[record.MessageSlot] = message;
                _acknowledgements++;
                OnCongestionAcknowledgement();
                ulong complete = message.FragmentCount == 64 ? ulong.MaxValue :
                    (1UL << message.FragmentCount) - 1;
                if (message.AcknowledgedMask == complete) FreeReliable(record.MessageSlot);
            }
        }
    }

    private void UpdateRtt(ulong sample)
    {
        if (_smoothedRtt == 0)
        {
            _smoothedRtt = sample;
            _rttVariation = Math.Max(1, sample / 2);
        }
        else
        {
            ulong difference = _smoothedRtt > sample ? _smoothedRtt - sample : sample - _smoothedRtt;
            _rttVariation = (3 * _rttVariation + difference) / 4;
            _smoothedRtt = (7 * _smoothedRtt + sample) / 8;
        }
        ulong candidate = checked(_smoothedRtt + 4 * _rttVariation);
        _rto = Math.Clamp(candidate, (ulong)_limits.MinimumRetransmissionSteps,
            (ulong)_limits.MaximumRetransmissionSteps);
    }

    private void OnCongestionAcknowledgement()
    {
        _congestionAckCredit++;
        if (_congestionAckCredit >= _congestionWindow && _congestionWindow < _limits.MaximumCongestionWindowPackets)
        {
            _congestionWindow++;
            _congestionAckCredit = 0;
        }
    }

    private void ProcessExpiredFlights(ulong step)
    {
        if (_inFlight == 0)
        {
            _nextLossCheckStep = ulong.MaxValue;
            _nextLossDeadlineCount = 0;
            return;
        }
        if (_nextLossCheckStep != 0 && step < _nextLossCheckStep) return;

        bool detectedLoss = false;
        ulong nextCheck = ulong.MaxValue;
        int nextCheckCount = 0;
        for (int index = 0; index < _sentPackets.Length; index++)
        {
            SentPacketRecord record = _sentPackets[index];
            if (!record.Active) continue;
            if (step < record.DeadlineStep)
            {
                if (record.DeadlineStep < nextCheck)
                {
                    nextCheck = record.DeadlineStep;
                    nextCheckCount = 1;
                }
                else if (record.DeadlineStep == nextCheck)
                {
                    nextCheckCount++;
                }
                continue;
            }
            RetireSentRecords(record.MessageSlot, record.FragmentIndex);
            _lastSentSteps[record.MessageSlot * _limits.MaximumFragmentsPerMessage + record.FragmentIndex] =
                ulong.MaxValue;
            _retransmissions = SaturatingIncrement(_retransmissions);
            detectedLoss = true;
        }
        _nextLossCheckStep = _inFlight == 0 ? ulong.MaxValue : nextCheck;
        _nextLossDeadlineCount = _inFlight == 0 ? 0 : nextCheckCount;
        if (!detectedLoss) return;
        _congestionWindow = Math.Clamp(_congestionWindow / 2, 1, _limits.MaximumCongestionWindowPackets);
        _congestionAckCredit = 0;
        _rto = Math.Min((ulong)_limits.MaximumRetransmissionSteps, Math.Max(_rto + 1, _rto * 2));
    }

    private void RetireSentRecords(int messageSlot, ushort fragment)
    {
        int fragmentMarker = messageSlot * _limits.MaximumFragmentsPerMessage + fragment;
        int index = _sentRecordByFragment[fragmentMarker];
        if (index < 0) return;
        SentPacketRecord record = _sentPackets[index];
        if (!record.Active || record.MessageSlot != messageSlot || record.FragmentIndex != fragment)
            throw new InvalidOperationException("The reliable-fragment flight index is inconsistent.");
        ReleaseSentRecord(index, in record);
    }

    private void ReleaseSentRecord(int index, in SentPacketRecord record)
    {
        _sentPackets[index] = default;
        if (!RemoveSentPacketIndex(record.PacketNumber))
            throw new InvalidOperationException("A retained flight was missing from the packet index.");
        int fragmentMarker = record.MessageSlot * _limits.MaximumFragmentsPerMessage +
            record.FragmentIndex;
        if (_sentRecordByFragment[fragmentMarker] == index)
            _sentRecordByFragment[fragmentMarker] = -1;
        _sentPacketFree[_sentPacketFreeCount++] = index;
        if (_inFlight > 0) _inFlight--;
        TrackRemovedDeadline(record.DeadlineStep);
    }

    private void AddSentPacketIndex(ulong packet, int sentRecord)
    {
        int mask = _sentPacketMapValues.Length - 1;
        int index = SentPacketHash(packet) & mask;
        for (int examined = 0; examined < _sentPacketMapValues.Length; examined++)
        {
            if (_sentPacketMapValues[index] < 0)
            {
                _sentPacketMapKeys[index] = packet;
                _sentPacketMapValues[index] = sentRecord;
                return;
            }
            if (_sentPacketMapKeys[index] == packet)
                throw new InvalidOperationException("A packet number was inserted twice into flight state.");
            index = (index + 1) & mask;
        }
        throw new InvalidOperationException("The bounded sent-packet index exhausted unexpectedly.");
    }

    private bool TryGetSentPacketIndex(ulong packet, out int sentRecord)
    {
        int mask = _sentPacketMapValues.Length - 1;
        int index = SentPacketHash(packet) & mask;
        for (int examined = 0; examined < _sentPacketMapValues.Length; examined++)
        {
            sentRecord = _sentPacketMapValues[index];
            if (sentRecord < 0) return false;
            if (_sentPacketMapKeys[index] == packet) return true;
            index = (index + 1) & mask;
        }
        sentRecord = -1;
        return false;
    }

    private bool RemoveSentPacketIndex(ulong packet)
    {
        int mask = _sentPacketMapValues.Length - 1;
        int index = SentPacketHash(packet) & mask;
        for (int examined = 0; examined < _sentPacketMapValues.Length; examined++)
        {
            if (_sentPacketMapValues[index] < 0) return false;
            if (_sentPacketMapKeys[index] == packet)
            {
                _sentPacketMapKeys[index] = 0;
                _sentPacketMapValues[index] = -1;
                int displaced = (index + 1) & mask;
                while (_sentPacketMapValues[displaced] >= 0)
                {
                    ulong displacedPacket = _sentPacketMapKeys[displaced];
                    int displacedRecord = _sentPacketMapValues[displaced];
                    _sentPacketMapKeys[displaced] = 0;
                    _sentPacketMapValues[displaced] = -1;
                    AddSentPacketIndex(displacedPacket, displacedRecord);
                    displaced = (displaced + 1) & mask;
                }
                return true;
            }
            index = (index + 1) & mask;
        }
        return false;
    }

    private static int SentPacketHash(ulong value)
    {
        unchecked
        {
            value ^= value >> 33;
            value *= 0xff51afd7ed558ccdUL;
            value ^= value >> 33;
            return (int)value;
        }
    }

    private void TrackRemovedDeadline(ulong deadline)
    {
        if (deadline != _nextLossCheckStep) return;
        if (_nextLossDeadlineCount > 0) _nextLossDeadlineCount--;
        if (_nextLossDeadlineCount == 0 && _inFlight != 0) _nextLossCheckStep = 0;
        if (_inFlight == 0)
        {
            _nextLossCheckStep = ulong.MaxValue;
            _nextLossDeadlineCount = 0;
        }
    }

    private void FreeReliable(int slot)
    {
        int payloadLength = _reliable[slot].Length;
        for (ushort fragment = 0; fragment < _reliable[slot].FragmentCount; fragment++)
            RetireSentRecords(slot, fragment);
        ReliablePayload(slot, payloadLength).Clear();
        _reliablePayloadOffsets[slot] = -1;
        _lastSentSteps.AsSpan(slot * _limits.MaximumFragmentsPerMessage,
            _limits.MaximumFragmentsPerMessage).Clear();
        _reliable[slot] = default;
        _reliableRetainedBytes -= payloadLength;
        _reliableFree[_reliableFreeCount++] = slot;
        _reliableCount--;
        if (_reliableCount == 0) _reliablePayloadTail = 0;
    }

    private bool TryEnqueueReceived(NetworkLane lane, NetworkMessageId messageId, ushort messageVersion,
        LaneSequence sequence, ReadOnlySpan<byte> payload)
    {
        if (_receivedFreeCount == 0) return false;
        int slot = _receivedFree[_receivedFreeCount - 1];
        if (!TryAllocateReceivedPayload(slot, payload.Length))
        {
            _retainedByteCapacityFailures = SaturatingIncrement(_retainedByteCapacityFailures);
            return false;
        }
        _receivedFreeCount--;
        payload.CopyTo(ReceivedPayload(slot, payload.Length));
        _received[slot] = new(lane, messageId, messageVersion, sequence, payload.Length);
        int tail = (_receivedHead + _receivedCount) % _receivedQueue.Length;
        _receivedQueue[tail] = slot;
        _receivedCount++;
        AddRetainedBytes(ref _receivedRetainedBytes, ref _receivedRetainedHighWaterBytes, payload.Length);
        return true;
    }

    private Span<byte> ReliablePayload(int slot, int length) =>
        _reliablePayloads.AsSpan(_reliablePayloadOffsets[slot], length);
    private Span<byte> IndependentPayload(int slot) =>
        _independentPayloads.AsSpan(slot * _independentPayloadStride, _independentPayloadStride);
    private Span<byte> UnreliablePayload(int messageSlot) =>
        _unreliablePayloads.AsSpan(_unreliablePayloadOffsets[messageSlot],
            _unreliablePayloadOffsets[messageSlot + 1] - _unreliablePayloadOffsets[messageSlot]);
    private Span<byte> ReassemblyPayload(NetworkLane lane) =>
        _reassemblyPayloads.AsSpan(_reassemblyPayloadOffsets[(int)lane],
            _reassemblyPayloadOffsets[(int)lane + 1] - _reassemblyPayloadOffsets[(int)lane]);
    private Span<byte> ReceivedPayload(int slot, int length) =>
        _receivedPayloads.AsSpan(_receivedPayloadOffsets[slot], length);

    private bool TryAllocateReliablePayload(int slot, int length)
    {
        if (length > _reliablePayloads.Length - _reliableRetainedBytes) return false;
        if (length > _reliablePayloads.Length - _reliablePayloadTail) CompactReliablePayloads();
        _reliablePayloadOffsets[slot] = _reliablePayloadTail;
        _reliablePayloadTail = checked(_reliablePayloadTail + length);
        return true;
    }

    private bool TryAllocateReceivedPayload(int slot, int length)
    {
        if (length > _receivedPayloads.Length - _receivedRetainedBytes) return false;
        if (length > _receivedPayloads.Length - _receivedPayloadTail) CompactReceivedPayloads();
        _receivedPayloadOffsets[slot] = _receivedPayloadTail;
        _receivedPayloadTail = checked(_receivedPayloadTail + length);
        return true;
    }

    private void CompactReliablePayloads()
    {
        long timingStarted = StartStageTiming();
        _reliablePayloadCompactions = SaturatingIncrement(_reliablePayloadCompactions);
        int bytesMoved = 0;
        int count = 0;
        for (int slot = 0; slot < _reliable.Length; slot++)
            if (_reliable[slot].Active) _reliableCompactOrder[count++] = slot;
        SortByPayloadOffset(_reliableCompactOrder, count, _reliablePayloadOffsets);
        int cursor = 0;
        for (int index = 0; index < count; index++)
        {
            int slot = _reliableCompactOrder[index];
            int length = _reliable[slot].Length;
            int source = _reliablePayloadOffsets[slot];
            if (source != cursor)
            {
                _reliablePayloads.AsSpan(source, length).CopyTo(_reliablePayloads.AsSpan(cursor, length));
                bytesMoved = checked(bytesMoved + length);
            }
            _reliablePayloadOffsets[slot] = cursor;
            cursor = checked(cursor + length);
        }
        _reliablePayloads.AsSpan(cursor, _reliablePayloadTail - cursor).Clear();
        _reliablePayloadTail = cursor;
        _reliablePayloadCompactionBytesMoved = SaturatingAdd(
            _reliablePayloadCompactionBytesMoved, checked((ulong)bytesMoved));
        _reliablePayloadMaximumBytesMovedPerCompaction = Math.Max(
            _reliablePayloadMaximumBytesMovedPerCompaction, bytesMoved);
        RecordStageTiming(ref _reliableCompactionTicks, timingStarted);
    }

    private void CompactReceivedPayloads()
    {
        long timingStarted = StartStageTiming();
        _receivedPayloadCompactions = SaturatingIncrement(_receivedPayloadCompactions);
        int bytesMoved = 0;
        int count = 0;
        for (int slot = 0; slot < _received.Length; slot++)
            if (_receivedPayloadOffsets[slot] >= 0) _receivedCompactOrder[count++] = slot;
        SortByPayloadOffset(_receivedCompactOrder, count, _receivedPayloadOffsets);
        int cursor = 0;
        for (int index = 0; index < count; index++)
        {
            int slot = _receivedCompactOrder[index];
            int length = _received[slot].PayloadLength;
            int source = _receivedPayloadOffsets[slot];
            if (source != cursor)
            {
                _receivedPayloads.AsSpan(source, length).CopyTo(_receivedPayloads.AsSpan(cursor, length));
                bytesMoved = checked(bytesMoved + length);
            }
            _receivedPayloadOffsets[slot] = cursor;
            cursor = checked(cursor + length);
        }
        _receivedPayloads.AsSpan(cursor, _receivedPayloadTail - cursor).Clear();
        _receivedPayloadTail = cursor;
        _receivedPayloadCompactionBytesMoved = SaturatingAdd(
            _receivedPayloadCompactionBytesMoved, checked((ulong)bytesMoved));
        _receivedPayloadMaximumBytesMovedPerCompaction = Math.Max(
            _receivedPayloadMaximumBytesMovedPerCompaction, bytesMoved);
        RecordStageTiming(ref _receivedCompactionTicks, timingStarted);
    }

    private static void SortByPayloadOffset(int[] order, int count, int[] offsets)
    {
        for (int index = 1; index < count; index++)
        {
            int slot = order[index];
            int offset = offsets[slot];
            int insertion = index;
            while (insertion > 0 && offsets[order[insertion - 1]] > offset)
            {
                order[insertion] = order[insertion - 1];
                insertion--;
            }
            order[insertion] = slot;
        }
    }

    private long CalculateReservedBytes() => checked(
        (long)_wireScratch.Length +
        _plaintextScratch.Length +
        (long)_reliable.Length * Unsafe.SizeOf<OutgoingMessage>() +
        _reliablePayloads.Length +
        (long)(_reliablePayloadOffsets.Length + _reliableCompactOrder.Length) * sizeof(int) +
        (long)_lastSentSteps.Length * sizeof(ulong) +
        (long)_reliableFree.Length * sizeof(int) +
        (long)_independent.Length * Unsafe.SizeOf<OutgoingMessage>() +
        _independentPayloads.Length +
        (long)_independentFree.Length * sizeof(int) +
        (long)_sentPackets.Length * Unsafe.SizeOf<SentPacketRecord>() +
        (long)(_sentPacketFree.Length + _sentRecordByFragment.Length) * sizeof(int) +
        (long)_sentPacketMapKeys.Length * sizeof(ulong) +
        (long)_sentPacketMapValues.Length * sizeof(int) +
        (long)_unreliableStreams.Length * Unsafe.SizeOf<UnreliableStreamState>() +
        (long)_unreliableHeads.Length * sizeof(int) +
        (long)_unreliablePayloadOffsets.Length * sizeof(int) +
        _unreliablePayloads.Length +
        (long)_nextReliableSendSequences.Length * sizeof(ulong) +
        (long)_nextReliableReceiveSequences.Length * sizeof(ulong) +
        (long)_reassembly.Length * Unsafe.SizeOf<ReassemblyState>() +
        (long)_reassemblyPayloadOffsets.Length * sizeof(int) +
        _reassemblyPayloads.Length +
        (long)_received.Length * Unsafe.SizeOf<NetworkDeliveredMessage>() +
        _receivedPayloads.Length +
        (long)(_receivedPayloadOffsets.Length + _receivedCompactOrder.Length) * sizeof(int) +
        (long)_receivedFree.Length * sizeof(int) +
        (long)_receivedQueue.Length * sizeof(int) +
        64L);

    private void FreeIndependent(int slot)
    {
        int payloadLength = _independent[slot].Length;
        IndependentPayload(slot).Clear();
        _independent[slot] = default;
        _independentRetainedBytes -= payloadLength;
        _independentFree[_independentFreeCount++] = slot;
        _independentCount--;
    }

    private void AddRetainedBytes(ref int current, ref int highWater, int bytes)
    {
        current = checked(current + bytes);
        if (current > highWater) highWater = current;
        if (bytes > _largestRetainedMessageBytes) _largestRetainedMessageBytes = bytes;
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private static ulong SaturatingIncrement(ulong value) =>
        value == ulong.MaxValue ? value : value + 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long StartStageTiming() => _stageTimingEnabled ? Stopwatch.GetTimestamp() : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordStageTiming(ref ulong accumulator, long started)
    {
        if (!_stageTimingEnabled) return;
        long elapsed = Stopwatch.GetTimestamp() - started;
        if (elapsed > 0)
            accumulator = SaturatingAdd(accumulator, checked((ulong)elapsed));
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        if (_disposed) throw new ObjectDisposedException(nameof(NetworkProtocolPeer));
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The protocol peer is bound to its creating owner thread.");
    }

    private readonly record struct OutgoingMessage(
        bool Active,
        NetworkLane Lane,
        NetworkMessageId MessageId,
        ushort MessageVersion,
        LaneSequence Sequence,
        int Length,
        ushort FragmentCount,
        ulong AcknowledgedMask);

    private struct UnreliableStreamState
    {
        internal OutgoingMessage Message;
        internal ulong NextSendSequence;
        internal ulong LastReceiveSequence;
        internal bool Pending;
        internal bool ReceiveSequenceInitialized;
    }

    private readonly record struct SentPacketRecord(
        ulong PacketNumber,
        ulong SentStep,
        int MessageSlot,
        uint TimeoutSteps,
        ushort FragmentIndex,
        bool Active)
    {
        internal ulong DeadlineStep => SentStep > ulong.MaxValue - TimeoutSteps
            ? ulong.MaxValue
            : SentStep + TimeoutSteps;
    }


    private readonly record struct ReassemblyState(
        bool Active,
        NetworkMessageId MessageId,
        ushort MessageVersion,
        LaneSequence Sequence,
        ushort FragmentCount,
        int MessageLength,
        ulong ReceivedMask);

    private enum NetworkDeliveryAdmission : byte
    {
        Invalid = 0,
        Deferred = 1,
        Admitted = 2,
    }
}
