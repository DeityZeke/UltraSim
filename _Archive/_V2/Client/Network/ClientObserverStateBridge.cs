using System.Runtime.InteropServices;
using System.Diagnostics;

using UltraSim.Network;

namespace UltraSim.Client;

/// <summary>
/// Narrow client-side bridge for observer-visible latest-state frames. It keeps transport/session
/// concerns out of the client wrapper while letting callers own payload-specific decode/apply work.
/// </summary>
public sealed class ClientObserverStateBridge
{
    private readonly ConnectionId _connectionId;
    private readonly Dictionary<ulong, ulong> _latestSequenceByItemKey = new();
    private readonly ClientLatestStateFrameApplier? _applyLatestState;
    private readonly ClientLatestStateFrameSpanApplier? _applyLatestStateSpan;
    private readonly Action? _resetLatestState;
    private bool _hasActiveBaseline;
    private StateBaselineDescriptor _activeBaseline;
    private int _activeBaselineAppliedCount;
    private bool _hasCompletedBaseline;
    private StateBaselineDescriptor _completedBaseline;
    private uint _lastCompletedBaselineId;
    private long _decodeElapsedTicks;
    private long _applyElapsedTicks;

    private ClientObserverStateBridge(
        ConnectionId connectionId,
        ClientLatestStateFrameApplier? applyLatestState,
        ClientLatestStateFrameSpanApplier? applyLatestStateSpan,
        Action? resetLatestState)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        _connectionId = connectionId;
        _applyLatestState = applyLatestState;
        _applyLatestStateSpan = applyLatestStateSpan;
        _resetLatestState = resetLatestState;
    }

    public ClientObserverStateBridge(
        ConnectionId connectionId,
        ClientLatestStateFrameApplier? applyLatestState = null,
        Action? resetLatestState = null)
        : this(connectionId, applyLatestState, null, resetLatestState)
    {
    }

    public static ClientObserverStateBridge CreateSpanFastPath(
        ConnectionId connectionId,
        ClientLatestStateFrameSpanApplier applyLatestStateSpan,
        Action? resetLatestState = null)
    {
        ArgumentNullException.ThrowIfNull(applyLatestStateSpan);
        return new ClientObserverStateBridge(connectionId, null, applyLatestStateSpan, resetLatestState);
    }

    public ConnectionId ConnectionId => _connectionId;
    public int LatestItemCount => _latestSequenceByItemKey.Count;
    public int LastReceivedFrameCount { get; private set; }
    public int LastAppliedFrameCount { get; private set; }
    public int LastReceivedBaselineFrameCount { get; private set; }
    public int LastAppliedBaselineFrameCount { get; private set; }
    public int LastReceivedDeltaFrameCount { get; private set; }
    public int LastAppliedDeltaFrameCount { get; private set; }
    public ClientObserverStateProfileSnapshot LastProfile { get; private set; }

    public int ProcessInbound(ReadOnlySpan<InboundDatagram> inbound)
    {
        BeginPump();

        int processed = 0;
        for (int i = 0; i < inbound.Length; i++)
        {
            ref readonly InboundDatagram datagram = ref inbound[i];
            if (datagram.ConnectionId != _connectionId || datagram.Plane != NetworkPlane.State)
                continue;

            if (!TryProcessEnvelope(datagram.Header, datagram.EnvelopePayloadSpan))
                continue;

            processed++;
        }

        return processed;
    }

    public void BeginPump()
    {
        LastReceivedFrameCount = 0;
        LastAppliedFrameCount = 0;
        LastReceivedBaselineFrameCount = 0;
        LastAppliedBaselineFrameCount = 0;
        LastReceivedDeltaFrameCount = 0;
        LastAppliedDeltaFrameCount = 0;
        _decodeElapsedTicks = 0;
        _applyElapsedTicks = 0;
        LastProfile = default;
    }

    public bool TryProcessEnvelope(in PacketEnvelopeHeader header, ReadOnlySpan<byte> payload)
    {
        if (header.PacketId == RealtimeStateFramePacket.PacketId)
        {
            long decodeStart = Stopwatch.GetTimestamp();
            var reader = new BitReader(payload);
            ulong sequence = reader.ReadUInt64();
            ulong itemKey = reader.ReadUInt64();
            ushort payloadLength = reader.ReadUInt16();
            ReadOnlySpan<byte> statePayload = reader.ReadBytes(payloadLength);
            _decodeElapsedTicks += Stopwatch.GetTimestamp() - decodeStart;
            long applyStart = Stopwatch.GetTimestamp();
            OnRealtimeStateFrame(sequence, itemKey, statePayload);
            _applyElapsedTicks += Stopwatch.GetTimestamp() - applyStart;
            UpdateProfile();
            return true;
        }

        if (header.PacketId == RealtimeStateBatchPacket.PacketId)
        {
            long decodeStart = Stopwatch.GetTimestamp();
            var reader = new BitReader(payload);
            ushort count = reader.ReadUInt16();
            _decodeElapsedTicks += Stopwatch.GetTimestamp() - decodeStart;
            for (int i = 0; i < count; i++)
            {
                ulong sequence = reader.ReadUInt64();
                ulong itemKey = reader.ReadUInt64();
                ushort payloadLength = reader.ReadUInt16();
                ReadOnlySpan<byte> statePayload = reader.ReadBytes(payloadLength);
                long applyStart = Stopwatch.GetTimestamp();
                OnRealtimeStateFrame(sequence, itemKey, statePayload);
                _applyElapsedTicks += Stopwatch.GetTimestamp() - applyStart;
            }

            UpdateProfile();
            return true;
        }

        if (header.PacketId == StateBaselineBeginPacket.PacketId)
        {
            long decodeStart = Stopwatch.GetTimestamp();
            var reader = new BitReader(payload);
            StateBaselineBeginPacket packet = StateBaselineBeginPacket.Deserialize(ref reader);
            _decodeElapsedTicks += Stopwatch.GetTimestamp() - decodeStart;
            OnBaselineBegin(packet);
            UpdateProfile();
            return true;
        }

        if (header.PacketId == StateBaselineFramePacket.PacketId)
        {
            long decodeStart = Stopwatch.GetTimestamp();
            var reader = new BitReader(payload);
            uint baselineId = reader.ReadUInt32();
            ulong itemKey = reader.ReadUInt64();
            ulong sequence = reader.ReadUInt64();
            ushort payloadLength = reader.ReadUInt16();
            ReadOnlySpan<byte> statePayload = reader.ReadBytes(payloadLength);
            _decodeElapsedTicks += Stopwatch.GetTimestamp() - decodeStart;
            long applyStart = Stopwatch.GetTimestamp();
            OnBaselineFrame(baselineId, itemKey, sequence, statePayload);
            _applyElapsedTicks += Stopwatch.GetTimestamp() - applyStart;
            UpdateProfile();
            return true;
        }

        if (header.PacketId == StateBaselineCompletePacket.PacketId)
        {
            long decodeStart = Stopwatch.GetTimestamp();
            var reader = new BitReader(payload);
            StateBaselineCompletePacket packet = StateBaselineCompletePacket.Deserialize(ref reader);
            _decodeElapsedTicks += Stopwatch.GetTimestamp() - decodeStart;
            OnBaselineComplete(packet);
            UpdateProfile();
            return true;
        }

        if (header.PacketId == StateDeltaFramePacket.PacketId)
        {
            long decodeStart = Stopwatch.GetTimestamp();
            var reader = new BitReader(payload);
            uint baselineId = reader.ReadUInt32();
            ulong itemKey = reader.ReadUInt64();
            ulong sequence = reader.ReadUInt64();
            ushort payloadLength = reader.ReadUInt16();
            ReadOnlySpan<byte> statePayload = reader.ReadBytes(payloadLength);
            _decodeElapsedTicks += Stopwatch.GetTimestamp() - decodeStart;
            long applyStart = Stopwatch.GetTimestamp();
            OnDeltaFrame(baselineId, itemKey, sequence, statePayload);
            _applyElapsedTicks += Stopwatch.GetTimestamp() - applyStart;
            UpdateProfile();
            return true;
        }

        return false;
    }

    private void OnRealtimeStateFrame(ulong sequence, ulong itemKey, ReadOnlySpan<byte> payload)
    {
        LastReceivedFrameCount++;
        ref ulong previousSequence = ref CollectionsMarshal.GetValueRefOrAddDefault(
            _latestSequenceByItemKey,
            itemKey,
            out bool exists);

        if (exists && sequence <= previousSequence)
        {
            return;
        }

        previousSequence = sequence;
        ApplyLatestState(itemKey, sequence, payload);
        LastAppliedFrameCount++;
    }

    public bool TryDequeueCompletedBaseline(out StateBaselineDescriptor descriptor)
    {
        if (!_hasCompletedBaseline)
        {
            descriptor = default;
            return false;
        }

        descriptor = _completedBaseline;
        _completedBaseline = default;
        _hasCompletedBaseline = false;
        return true;
    }

    public bool TryDequeueBaselineAckPacket(out StateBaselineAckPacket packet)
    {
        if (!TryDequeueCompletedBaseline(out StateBaselineDescriptor descriptor))
        {
            packet = default;
            return false;
        }

        packet = new StateBaselineAckPacket(descriptor);
        return true;
    }

    private void OnBaselineBegin(in StateBaselineBeginPacket packet)
    {
        _hasActiveBaseline = true;
        _activeBaseline = packet.Descriptor;
        _activeBaselineAppliedCount = 0;
        _latestSequenceByItemKey.Clear();
        _resetLatestState?.Invoke();
    }

    private void OnBaselineFrame(uint baselineId, ulong itemKey, ulong sequence, ReadOnlySpan<byte> payload)
    {
        if (!_hasActiveBaseline || baselineId != _activeBaseline.BaselineId)
            return;

        LastReceivedBaselineFrameCount++;
        _latestSequenceByItemKey[itemKey] = sequence;
        ApplyLatestState(itemKey, sequence, payload);
        _activeBaselineAppliedCount++;
        LastAppliedBaselineFrameCount++;
    }

    private void OnBaselineComplete(in StateBaselineCompletePacket packet)
    {
        if (!_hasActiveBaseline || packet.BaselineId != _activeBaseline.BaselineId)
            return;

        _lastCompletedBaselineId = _activeBaseline.BaselineId;
        _completedBaseline = _activeBaseline;
        _hasCompletedBaseline = true;
        _hasActiveBaseline = false;
        _activeBaseline = default;
        _activeBaselineAppliedCount = 0;
    }

    private void OnDeltaFrame(uint baselineId, ulong itemKey, ulong sequence, ReadOnlySpan<byte> payload)
    {
        LastReceivedDeltaFrameCount++;

        if (_hasActiveBaseline)
            return;
        if (_lastCompletedBaselineId == 0 || baselineId > _lastCompletedBaselineId)
            return;

        ref ulong previousSequence = ref CollectionsMarshal.GetValueRefOrAddDefault(
            _latestSequenceByItemKey,
            itemKey,
            out bool exists);

        if (exists && sequence <= previousSequence)
            return;

        previousSequence = sequence;
        ApplyLatestState(itemKey, sequence, payload);
        LastAppliedDeltaFrameCount++;
    }

    private void ApplyLatestState(ulong itemKey, ulong sequence, ReadOnlySpan<byte> payload)
    {
        if (_applyLatestStateSpan is not null)
        {
            _applyLatestStateSpan(itemKey, sequence, payload);
            return;
        }

        if (_applyLatestState is not null)
            _applyLatestState(itemKey, sequence, ImmutablePayloadBuffer.CopyFrom(payload));
    }

    private void UpdateProfile()
    {
        LastProfile = new ClientObserverStateProfileSnapshot(
            _decodeElapsedTicks,
            _applyElapsedTicks,
            LastReceivedFrameCount,
            LastAppliedFrameCount,
            LastReceivedBaselineFrameCount,
            LastAppliedBaselineFrameCount,
            LastReceivedDeltaFrameCount,
            LastAppliedDeltaFrameCount);
    }
}
