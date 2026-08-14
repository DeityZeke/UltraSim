using System.Security.Cryptography;

namespace UltraSim.Network;

public enum NetworkQueueStatus : byte
{
    Accepted = 0,
    CapacityExceeded = 1,
    MessageTooLarge = 2,
    InvalidDescriptor = 3,
    SequenceExhausted = 4,
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
    private static readonly NetworkLane[] s_priorityOrder =
    {
        NetworkLane.Control,
        NetworkLane.Input,
        NetworkLane.Event,
        NetworkLane.OwnerState,
        NetworkLane.WorldState,
        NetworkLane.Bulk,
        NetworkLane.Disposable,
    };

    private readonly int _ownerThreadId;
    private readonly DeterministicDatagramEndpoint _endpoint;
    private readonly NetworkProtocolManifest _manifest;
    private readonly NetworkProtocolLimits _limits;
    private readonly DeterministicDatagramLimits _datagramLimits;
    private readonly ConnectionId _connection;
    private readonly NetworkDirectionalCryptography _sendCryptography;
    private readonly NetworkDirectionalCryptography _receiveCryptography;
    private readonly byte[] _wireScratch;
    private readonly byte[] _plaintextScratch;
    private readonly OutgoingMessage[] _reliable;
    private readonly byte[] _reliablePayloads;
    private readonly ulong[] _lastSentSteps;
    private readonly int[] _reliableFree;
    private readonly SentPacketRecord[] _sentPackets;
    private readonly bool[] _unreliablePending;
    private readonly OutgoingMessage[] _unreliable;
    private readonly byte[] _unreliablePayloads;
    private readonly ulong[] _nextSendSequences;
    private readonly ulong[] _nextReceiveSequences;
    private readonly bool[] _receiveSequenceInitialized;
    private readonly ReassemblyState[] _reassembly;
    private readonly byte[] _reassemblyPayloads;
    private readonly NetworkDeliveredMessage[] _received;
    private readonly byte[] _receivedPayloads;
    private readonly int[] _receivedFree;
    private readonly int[] _receivedQueue;
    private int _reliableFreeCount;
    private int _reliableCount;
    private int _receivedFreeCount;
    private int _receivedHead;
    private int _receivedCount;
    private ulong _nextPacketNumber;
    private ulong _sentPacketsCount;
    private ulong _receivedPacketsCount;
    private ulong _authenticationFailures;
    private ulong _replayRejections;
    private ulong _retransmissions;
    private ulong _acknowledgements;
    private ulong _sequencedDrops;
    private ulong _capacityDrops;
    private NetworkReplayWindow _replay;
    private bool _ackPending;
    private int _inFlight;
    private int _congestionWindow;
    private int _congestionAckCredit;
    private ulong _smoothedRtt;
    private ulong _rttVariation;
    private ulong _rto;
    private bool _disposed;

    internal NetworkProtocolPeer(
        DeterministicDatagramEndpoint endpoint,
        NetworkProtocolManifest manifest,
        NetworkProtocolLimits limits,
        DeterministicDatagramLimits datagramLimits,
        ConnectionId connection,
        SecurityEpoch epoch,
        ReadOnlySpan<byte> masterSecret,
        bool sendsClientToServer)
    {
        limits.Validate(datagramLimits);
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _endpoint = endpoint;
        _manifest = manifest;
        _limits = limits;
        _datagramLimits = datagramLimits;
        _connection = connection;
        _sendCryptography = new(masterSecret, sendsClientToServer, epoch);
        _receiveCryptography = new(masterSecret, !sendsClientToServer, epoch);
        _wireScratch = new byte[datagramLimits.MaximumDatagramBytes];
        _plaintextScratch = new byte[NetworkSecureDatagramFraming.MaximumPlaintextBytes(datagramLimits.MaximumDatagramBytes)];
        _reliable = new OutgoingMessage[limits.MaximumReliableMessages];
        _reliablePayloads = new byte[checked(limits.MaximumReliableMessages * limits.MaximumMessageBytes)];
        _lastSentSteps = new ulong[checked(limits.MaximumReliableMessages * limits.MaximumFragmentsPerMessage)];
        _reliableFree = new int[limits.MaximumReliableMessages];
        for (int index = 0; index < _reliableFree.Length; index++) _reliableFree[index] = _reliableFree.Length - 1 - index;
        _reliableFreeCount = _reliableFree.Length;
        _sentPackets = new SentPacketRecord[limits.SentPacketCapacity];
        _unreliablePending = new bool[NetworkLaneContracts.Count];
        _unreliable = new OutgoingMessage[NetworkLaneContracts.Count];
        _unreliablePayloads = new byte[checked(NetworkLaneContracts.Count * limits.MaximumMessageBytes)];
        _nextSendSequences = new ulong[NetworkLaneContracts.Count];
        _nextReceiveSequences = new ulong[NetworkLaneContracts.Count];
        _receiveSequenceInitialized = new bool[NetworkLaneContracts.Count];
        _reassembly = new ReassemblyState[NetworkLaneContracts.Count];
        _reassemblyPayloads = new byte[checked(NetworkLaneContracts.Count * limits.MaximumMessageBytes)];
        _received = new NetworkDeliveredMessage[limits.MaximumReceivedMessages];
        _receivedPayloads = new byte[checked(limits.MaximumReceivedMessages * limits.MaximumMessageBytes)];
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
        if (!_manifest.TryGet(messageId, out NetworkMessageDescriptor descriptor))
            return NetworkQueueStatus.InvalidDescriptor;
        if (payload.Length > descriptor.MaximumEncodedBytes || payload.Length > _limits.MaximumMessageBytes)
            return NetworkQueueStatus.MessageTooLarge;
        NetworkLaneContract lane = NetworkLaneContracts.Get(descriptor.Lane);
        int fragmentPayload = FragmentPayloadBytes;
        int fragmentCount = Math.Max(1, (payload.Length + fragmentPayload - 1) / fragmentPayload);
        if (fragmentCount > _limits.MaximumFragmentsPerMessage || (fragmentCount > 1 && !lane.AllowsFragmentation))
            return NetworkQueueStatus.MessageTooLarge;
        int laneIndex = (int)descriptor.Lane;
        ulong sequence = _nextSendSequences[laneIndex];
        if (sequence == ulong.MaxValue) return NetworkQueueStatus.SequenceExhausted;

        if (lane.Delivery == NetworkDeliveryGuarantee.Reliable)
        {
            if (_reliableFreeCount == 0) return NetworkQueueStatus.CapacityExceeded;
            int slot = _reliableFree[--_reliableFreeCount];
            payload.CopyTo(ReliablePayload(slot));
            _lastSentSteps.AsSpan(slot * _limits.MaximumFragmentsPerMessage,
                _limits.MaximumFragmentsPerMessage).Clear();
            _reliable[slot] = new(true, descriptor.Lane, descriptor.Id, descriptor.Version,
                new LaneSequence(sequence), payload.Length, (ushort)fragmentCount, 0);
            _reliableCount++;
        }
        else
        {
            payload.CopyTo(UnreliablePayload(descriptor.Lane));
            _unreliable[laneIndex] = new(true, descriptor.Lane, descriptor.Id, descriptor.Version,
                new LaneSequence(sequence), payload.Length, 1, 0);
            _unreliablePending[laneIndex] = true;
        }
        _nextSendSequences[laneIndex] = sequence + 1;
        return NetworkQueueStatus.Accepted;
    }

    public int ReliableQueued
    {
        get { EnsureOperational(); return _reliableCount; }
    }

    public int ReceivedQueued
    {
        get { EnsureOperational(); return _receivedCount; }
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
        payload = ReceivedPayload(slot)[..message.PayloadLength];
        return true;
    }

    public bool TryConsumeReceived()
    {
        EnsureOperational();
        if (_receivedCount == 0) return false;
        int slot = _receivedQueue[_receivedHead];
        _receivedHead = (_receivedHead + 1) % _receivedQueue.Length;
        _receivedCount--;
        ReceivedPayload(slot).Clear();
        _received[slot] = default;
        _receivedFree[_receivedFreeCount++] = slot;
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
        return true;
    }

    internal bool CanRotateSendSecurityEpoch =>
        !_disposed && _reliableCount == 0 && _inFlight == 0 && _sendCryptography.Epoch.Value != uint.MaxValue;

    internal void ProcessInbound(ulong step)
    {
        EnsureOperational();
        while (_endpoint.TryReceive(_wireScratch, out int length))
            ProcessDatagram(_wireScratch.AsSpan(0, length), step);
    }

    internal void Emit(ulong step)
    {
        EnsureOperational();
        int budget = _limits.MaximumPacketsPerPump;
        if (_ackPending && budget != 0 && TryEmitAcknowledgement()) budget--;

        while (budget > 0 && _inFlight < _congestionWindow)
        {
            if (!TrySelectNext(step, out int reliableSlot, out ushort fragment, out NetworkLane unreliableLane)) break;
            bool reliable = reliableSlot >= 0;
            if (!TryEmitData(reliableSlot, fragment, unreliableLane, step, reliable)) break;
            budget--;
        }
    }

    public NetworkProtocolSnapshot GetSnapshot()
    {
        EnsureOperational();
        long reserved = _limits.EstimateReservedPayloadBytes(_datagramLimits);
        return new(true, _sendCryptography.Epoch, _receiveCryptography.Epoch,
            _sentPacketsCount, _receivedPacketsCount, _authenticationFailures, _replayRejections,
            _retransmissions, _acknowledgements, _sequencedDrops, _capacityDrops,
            _reliableCount, _receivedCount, _congestionWindow, _inFlight, _smoothedRtt, _rto, reserved);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _sendCryptography.Dispose();
        _receiveCryptography.Dispose();
        CryptographicOperations.ZeroMemory(_reliablePayloads);
        CryptographicOperations.ZeroMemory(_unreliablePayloads);
        CryptographicOperations.ZeroMemory(_reassemblyPayloads);
        CryptographicOperations.ZeroMemory(_receivedPayloads);
        CryptographicOperations.ZeroMemory(_wireScratch);
        CryptographicOperations.ZeroMemory(_plaintextScratch);
        _disposed = true;
    }

    private int FragmentPayloadBytes =>
        NetworkSecureDatagramFraming.MaximumPlaintextBytes(_datagramLimits.MaximumDatagramBytes) -
        NetworkProtocolDataHeader.EncodedBytes;

    private bool TrySelectNext(ulong step, out int reliableSlot, out ushort fragment, out NetworkLane unreliableLane)
    {
        foreach (NetworkLane lane in s_priorityOrder)
        {
            int selected = FindReliableHead(lane);
            if (selected >= 0 && TryFindDueFragment(selected, step, out fragment))
            {
                reliableSlot = selected;
                unreliableLane = default;
                return true;
            }
            if (_unreliablePending[(int)lane])
            {
                reliableSlot = -1;
                fragment = 0;
                unreliableLane = lane;
                return true;
            }
        }
        reliableSlot = -1;
        fragment = 0;
        unreliableLane = default;
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

    private bool TryFindDueFragment(int slot, ulong step, out ushort fragment)
    {
        OutgoingMessage message = _reliable[slot];
        Span<ulong> sent = _lastSentSteps.AsSpan(slot * _limits.MaximumFragmentsPerMessage,
            _limits.MaximumFragmentsPerMessage);
        for (ushort index = 0; index < message.FragmentCount; index++)
        {
            ulong bit = 1UL << index;
            if ((message.AcknowledgedMask & bit) != 0) continue;
            ulong marker = sent[index];
            if (marker == 0) { fragment = index; return true; }
            ulong last = marker - 1;
            if (step - last >= _rto)
            {
                RetireSentRecords(slot, index, lost: true);
                _retransmissions++;
                fragment = index;
                return true;
            }
        }
        fragment = 0;
        return false;
    }

    private bool TryEmitData(int reliableSlot, ushort fragmentIndex, NetworkLane unreliableLane,
        ulong step, bool reliable)
    {
        OutgoingMessage message = reliable ? _reliable[reliableSlot] : _unreliable[(int)unreliableLane];
        ReadOnlySpan<byte> payload = reliable
            ? ReliablePayload(reliableSlot)[..message.Length]
            : UnreliablePayload(unreliableLane)[..message.Length];
        int fragmentPayload = FragmentPayloadBytes;
        int offset = reliable ? fragmentIndex * fragmentPayload : 0;
        int length = reliable ? Math.Min(fragmentPayload, message.Length - offset) : message.Length;
        var frame = new NetworkDataFrameHeader(
            message.Lane, message.MessageId, message.MessageVersion, message.Sequence,
            fragmentIndex, message.FragmentCount, message.Length, checked((ushort)length));
        var writer = new NetworkPacketWriter(_plaintextScratch);
        if (!NetworkDataFrameCodec.TryWrite(ref writer, frame) ||
            !writer.TryWriteBytes(payload.Slice(offset, length)))
            throw new InvalidOperationException("A validated protocol frame exceeded its fixed plaintext buffer.");

        if (!TryEncryptAndSend(NetworkSecureDatagramKind.Data, writer.WrittenSpan,
                out PacketNumber packet, out DatagramSendStatus status)) return false;
        if (status is DatagramSendStatus.Accepted or DatagramSendStatus.DroppedByImpairment)
        {
            if (reliable)
            {
                int record = FindFreeSentRecord();
                if (record < 0) throw new InvalidOperationException("Sent-packet capacity was not reserved before emission.");
                _sentPackets[record] = new(true, packet.Value, reliableSlot, fragmentIndex, step);
                _lastSentSteps[reliableSlot * _limits.MaximumFragmentsPerMessage + fragmentIndex] = step + 1;
                _inFlight++;
            }
            else
            {
                _unreliablePending[(int)unreliableLane] = false;
                UnreliablePayload(unreliableLane).Clear();
                _unreliable[(int)unreliableLane] = default;
            }
            return true;
        }
        return false;
    }

    private bool TryEmitAcknowledgement()
    {
        if (!_replay.TryGetAcknowledgement(out ulong largest, out ulong mask))
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
        if (kind == NetworkSecureDatagramKind.Data && FindFreeSentRecord() < 0)
        {
            packet = default;
            status = DatagramSendStatus.CapacityExceeded;
            return false;
        }
        packet = new PacketNumber(_nextPacketNumber);
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
        status = _endpoint.TrySend(_wireScratch.AsSpan(0,
            NetworkSecureDatagramFraming.HeaderBytes + plaintext.Length + NetworkSecureDatagramFraming.TagBytes));
        if (status is DatagramSendStatus.Accepted or DatagramSendStatus.DroppedByImpairment)
        {
            _nextPacketNumber++;
            _sentPacketsCount++;
            return true;
        }
        return false;
    }

    private void ProcessDatagram(ReadOnlySpan<byte> datagram, ulong step)
    {
        if (!NetworkSecureDatagramFraming.TryReadExact(datagram, out NetworkSecureDatagramHeader header,
                out ReadOnlySpan<byte> associated, out ReadOnlySpan<byte> ciphertext, out ReadOnlySpan<byte> tag) ||
            header.ProtocolVersion != _manifest.Version || header.Connection != _connection)
        {
            _authenticationFailures++;
            return;
        }

        uint receiveEpoch = _receiveCryptography.Epoch.Value;
        bool potentialAdvance = receiveEpoch != uint.MaxValue && header.SecurityEpoch.Value == receiveEpoch + 1;
        NetworkReplayWindow candidateReplay = potentialAdvance ? default : _replay;
        if (!candidateReplay.TryPrepare(header.PacketNumber, out NetworkReplayTransition replayTransition))
        {
            _replayRejections++;
            return;
        }
        Span<byte> plaintext = _plaintextScratch.AsSpan(0, ciphertext.Length);
        if (!_receiveCryptography.TryDecryptAtEpoch(header.SecurityEpoch, header.PacketNumber,
                ciphertext, tag, plaintext, associated, out bool advanced))
        {
            _authenticationFailures++;
            return;
        }
        bool accepted = header.Kind switch
        {
            NetworkSecureDatagramKind.Data => ProcessData(plaintext),
            NetworkSecureDatagramKind.Acknowledgement => ProcessAcknowledgement(plaintext, step),
            _ => false,
        };
        if (!accepted) return;
        if (advanced)
        {
            if (!_receiveCryptography.CommitAdvance(header.SecurityEpoch))
                throw new InvalidOperationException("An authenticated next security epoch could not be committed.");
            _replay.Reset();
        }
        _replay.Commit(replayTransition);
        _receivedPacketsCount++;
        if (header.Kind == NetworkSecureDatagramKind.Data) _ackPending = true;
    }

    private bool ProcessData(ReadOnlySpan<byte> plaintext)
    {
        var reader = new NetworkPacketReader(plaintext);
        if (!NetworkDataFrameCodec.TryRead(ref reader, out NetworkDataFrameHeader header) ||
            header.MessageLength > _limits.MaximumMessageBytes ||
            header.FragmentCount > _limits.MaximumFragmentsPerMessage ||
            !reader.TryReadBytes(header.FragmentLength, out ReadOnlySpan<byte> fragment) || !reader.IsComplete ||
            !_manifest.TryGet(header.MessageId, out NetworkMessageDescriptor descriptor) ||
            descriptor.Version != header.MessageVersion || descriptor.Lane != header.Lane ||
            header.MessageLength > descriptor.MaximumEncodedBytes)
            return false;

        int fragmentPayload = FragmentPayloadBytes;
        int offset = header.FragmentIndex * fragmentPayload;
        int expectedLength = Math.Min(fragmentPayload, header.MessageLength - offset);
        if (offset < 0 || offset > header.MessageLength || expectedLength != header.FragmentLength ||
            (header.FragmentCount > 1 && !NetworkLaneContracts.Get(header.Lane).AllowsFragmentation) ||
            header.FragmentCount != Math.Max(1, (header.MessageLength + fragmentPayload - 1) / fragmentPayload))
            return false;

        NetworkLaneContract contract = NetworkLaneContracts.Get(header.Lane);
        int laneIndex = (int)header.Lane;
        if (contract.Delivery == NetworkDeliveryGuarantee.Reliable)
            return ProcessReliableFragment(header, fragment, offset);

        if (contract.Order == NetworkDeliveryOrder.Sequenced && _receiveSequenceInitialized[laneIndex] &&
            header.LaneSequence.Value <= _nextReceiveSequences[laneIndex])
        {
            _sequencedDrops++;
            return true;
        }
        if (_receivedFreeCount == 0)
        {
            _capacityDrops++;
            return true;
        }
        EnqueueReceived(header.Lane, header.MessageId, header.MessageVersion, header.LaneSequence, fragment);
        if (contract.Order == NetworkDeliveryOrder.Sequenced)
        {
            _receiveSequenceInitialized[laneIndex] = true;
            _nextReceiveSequences[laneIndex] = header.LaneSequence.Value;
        }
        return true;
    }

    private bool ProcessReliableFragment(in NetworkDataFrameHeader header, ReadOnlySpan<byte> fragment, int offset)
    {
        int laneIndex = (int)header.Lane;
        ulong expected = _nextReceiveSequences[laneIndex];
        if (header.LaneSequence.Value < expected) return true;
        if (header.LaneSequence.Value > expected) return false;
        ref ReassemblyState state = ref _reassembly[laneIndex];
        if (!state.Active)
            state = new(true, header.MessageId, header.MessageVersion, header.LaneSequence,
                header.FragmentCount, header.MessageLength, 0);
        else if (state.MessageId != header.MessageId || state.MessageVersion != header.MessageVersion ||
            state.Sequence != header.LaneSequence || state.FragmentCount != header.FragmentCount ||
            state.MessageLength != header.MessageLength)
            return false;

        ulong bit = 1UL << header.FragmentIndex;
        if ((state.ReceivedMask & bit) == 0)
        {
            fragment.CopyTo(ReassemblyPayload(header.Lane).Slice(offset, fragment.Length));
            state = state with { ReceivedMask = state.ReceivedMask | bit };
        }
        ulong completeMask = header.FragmentCount == 64 ? ulong.MaxValue : (1UL << header.FragmentCount) - 1;
        if (state.ReceivedMask != completeMask) return true;
        if (_receivedFreeCount == 0)
        {
            _capacityDrops++;
            return false;
        }
        EnqueueReceived(header.Lane, header.MessageId, header.MessageVersion, header.LaneSequence,
            ReassemblyPayload(header.Lane)[..header.MessageLength]);
        ReassemblyPayload(header.Lane).Clear();
        state = default;
        _nextReceiveSequences[laneIndex] = expected + 1;
        _receiveSequenceInitialized[laneIndex] = true;
        return true;
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
        for (int index = 0; index < _sentPackets.Length; index++)
        {
            SentPacketRecord record = _sentPackets[index];
            if (!record.Active || record.PacketNumber != packet) continue;
            _sentPackets[index] = default;
            if (_inFlight > 0) _inFlight--;
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
            return;
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

    private void RetireSentRecords(int messageSlot, ushort fragment, bool lost)
    {
        bool found = false;
        for (int index = 0; index < _sentPackets.Length; index++)
        {
            SentPacketRecord record = _sentPackets[index];
            if (!record.Active || record.MessageSlot != messageSlot || record.FragmentIndex != fragment) continue;
            _sentPackets[index] = default;
            if (_inFlight > 0) _inFlight--;
            found = true;
        }
        if (lost && found)
        {
            _congestionWindow = Math.Max(2, _congestionWindow / 2);
            _congestionAckCredit = 0;
            _rto = Math.Min((ulong)_limits.MaximumRetransmissionSteps, Math.Max(_rto + 1, _rto * 2));
        }
    }

    private int FindFreeSentRecord()
    {
        for (int index = 0; index < _sentPackets.Length; index++)
            if (!_sentPackets[index].Active) return index;
        return -1;
    }

    private void FreeReliable(int slot)
    {
        for (ushort fragment = 0; fragment < _reliable[slot].FragmentCount; fragment++)
            RetireSentRecords(slot, fragment, lost: false);
        ReliablePayload(slot).Clear();
        _lastSentSteps.AsSpan(slot * _limits.MaximumFragmentsPerMessage,
            _limits.MaximumFragmentsPerMessage).Clear();
        _reliable[slot] = default;
        _reliableFree[_reliableFreeCount++] = slot;
        _reliableCount--;
    }

    private void EnqueueReceived(NetworkLane lane, NetworkMessageId messageId, ushort messageVersion,
        LaneSequence sequence, ReadOnlySpan<byte> payload)
    {
        int slot = _receivedFree[--_receivedFreeCount];
        payload.CopyTo(ReceivedPayload(slot));
        _received[slot] = new(lane, messageId, messageVersion, sequence, payload.Length);
        int tail = (_receivedHead + _receivedCount) % _receivedQueue.Length;
        _receivedQueue[tail] = slot;
        _receivedCount++;
    }

    private Span<byte> ReliablePayload(int slot) =>
        _reliablePayloads.AsSpan(slot * _limits.MaximumMessageBytes, _limits.MaximumMessageBytes);
    private Span<byte> UnreliablePayload(NetworkLane lane) =>
        _unreliablePayloads.AsSpan((int)lane * _limits.MaximumMessageBytes, _limits.MaximumMessageBytes);
    private Span<byte> ReassemblyPayload(NetworkLane lane) =>
        _reassemblyPayloads.AsSpan((int)lane * _limits.MaximumMessageBytes, _limits.MaximumMessageBytes);
    private Span<byte> ReceivedPayload(int slot) =>
        _receivedPayloads.AsSpan(slot * _limits.MaximumMessageBytes, _limits.MaximumMessageBytes);

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

    private readonly record struct SentPacketRecord(
        bool Active,
        ulong PacketNumber,
        int MessageSlot,
        ushort FragmentIndex,
        ulong SentStep);

    private readonly record struct ReassemblyState(
        bool Active,
        NetworkMessageId MessageId,
        ushort MessageVersion,
        LaneSequence Sequence,
        ushort FragmentCount,
        int MessageLength,
        ulong ReceivedMask);
}
