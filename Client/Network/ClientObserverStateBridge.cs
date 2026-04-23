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
    private readonly Action? _resetLatestState;
    private bool _hasActiveBaseline;
    private StateBaselineDescriptor _activeBaseline;
    private int _activeBaselineAppliedCount;
    private bool _hasCompletedBaseline;
    private StateBaselineDescriptor _completedBaseline;
    private uint _lastCompletedBaselineId;
    private long _decodeElapsedTicks;
    private long _applyElapsedTicks;

    public ClientObserverStateBridge(
        ConnectionId connectionId,
        ClientLatestStateFrameApplier? applyLatestState = null,
        Action? resetLatestState = null)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        _connectionId = connectionId;
        _applyLatestState = applyLatestState;
        _resetLatestState = resetLatestState;
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
            RealtimeStateFramePacket packet = RealtimeStateFramePacket.Deserialize(ref reader);
            _decodeElapsedTicks += Stopwatch.GetTimestamp() - decodeStart;
            long applyStart = Stopwatch.GetTimestamp();
            OnRealtimeStateFrame(packet);
            _applyElapsedTicks += Stopwatch.GetTimestamp() - applyStart;
            UpdateProfile();
            return true;
        }

        if (header.PacketId == RealtimeStateBatchPacket.PacketId)
        {
            long decodeStart = Stopwatch.GetTimestamp();
            var reader = new BitReader(payload);
            RealtimeStateBatchPacket packet = RealtimeStateBatchPacket.Deserialize(ref reader);
            _decodeElapsedTicks += Stopwatch.GetTimestamp() - decodeStart;
            for (int i = 0; i < packet.Items.Length; i++)
            {
                RealtimeStateBatchItem item = packet.Items[i];
                long applyStart = Stopwatch.GetTimestamp();
                OnRealtimeStateFrame(new RealtimeStateFramePacket(item.Sequence, item.ItemKey, item.Payload));
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
            StateBaselineFramePacket packet = StateBaselineFramePacket.Deserialize(ref reader);
            _decodeElapsedTicks += Stopwatch.GetTimestamp() - decodeStart;
            long applyStart = Stopwatch.GetTimestamp();
            OnBaselineFrame(packet);
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
            StateDeltaFramePacket packet = StateDeltaFramePacket.Deserialize(ref reader);
            _decodeElapsedTicks += Stopwatch.GetTimestamp() - decodeStart;
            long applyStart = Stopwatch.GetTimestamp();
            OnDeltaFrame(packet);
            _applyElapsedTicks += Stopwatch.GetTimestamp() - applyStart;
            UpdateProfile();
            return true;
        }

        return false;
    }

    private void OnRealtimeStateFrame(in RealtimeStateFramePacket packet)
    {
        LastReceivedFrameCount++;
        ref ulong previousSequence = ref CollectionsMarshal.GetValueRefOrAddDefault(
            _latestSequenceByItemKey,
            packet.ItemKey,
            out bool exists);

        if (exists && packet.Sequence <= previousSequence)
        {
            return;
        }

        previousSequence = packet.Sequence;
        _applyLatestState?.Invoke(packet.ItemKey, packet.Sequence, packet.Payload);
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

    private void OnBaselineFrame(in StateBaselineFramePacket packet)
    {
        if (!_hasActiveBaseline || packet.BaselineId != _activeBaseline.BaselineId)
            return;

        LastReceivedBaselineFrameCount++;
        _latestSequenceByItemKey[packet.ItemKey] = packet.Sequence;
        _applyLatestState?.Invoke(packet.ItemKey, packet.Sequence, packet.Payload);
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

    private void OnDeltaFrame(in StateDeltaFramePacket packet)
    {
        LastReceivedDeltaFrameCount++;

        if (_hasActiveBaseline)
            return;
        if (_lastCompletedBaselineId == 0 || packet.BaselineId > _lastCompletedBaselineId)
            return;

        ref ulong previousSequence = ref CollectionsMarshal.GetValueRefOrAddDefault(
            _latestSequenceByItemKey,
            packet.ItemKey,
            out bool exists);

        if (exists && packet.Sequence <= previousSequence)
            return;

        previousSequence = packet.Sequence;
        _applyLatestState?.Invoke(packet.ItemKey, packet.Sequence, packet.Payload);
        LastAppliedDeltaFrameCount++;
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
