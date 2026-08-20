using System;
using UltraSim.Network;

namespace UltraSim.Client.Engines.Networking
{
    public sealed class ClientInputSendBridge<TInput>
    {
        private readonly INetworkTransport _transport;
        private readonly ClientInputSequencer<TInput> _sequencer;
        private readonly ClientInputEncoder<TInput> _encoder;
        private readonly int _connectionId;
        private readonly ChannelType _channel;

        private ClientInputSample<TInput>[] _pendingScratch = new ClientInputSample<TInput>[64];
        private readonly byte[] _payloadScratch;

        public ClientInputSendBridge(
            INetworkTransport transport,
            int connectionId,
            ClientInputEncoder<TInput> encoder,
            ClientInputSequencer<TInput>? sequencer = null,
            ChannelType channel = ChannelType.ReliableOrdered,
            int maxPayloadBytes = 1024)
        {
            ArgumentNullException.ThrowIfNull(transport);
            ArgumentNullException.ThrowIfNull(encoder);
            if (maxPayloadBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

            _transport = transport;
            _connectionId = connectionId;
            _encoder = encoder;
            _sequencer = sequencer ?? new ClientInputSequencer<TInput>();
            _channel = channel;
            _payloadScratch = new byte[maxPayloadBytes];
        }

        public int ConnectionId => _connectionId;

        public ClientInputSequencer<TInput> Sequencer => _sequencer;

        public ClientInputSample<TInput> QueueInput(in TInput input, long clientTick) =>
            _sequencer.EnqueueInput(in input, clientTick);

        public int AcknowledgeUpTo(ushort acknowledgedSequence) =>
            _sequencer.AcknowledgeUpTo(acknowledgedSequence);

        public bool TrySendInput(
            in TInput input,
            long clientTick,
            out ClientInputSendResult result)
        {
            ClientInputSample<TInput> sample = _sequencer.EnqueueInput(in input, clientTick);
            return TrySendSample(in sample, out result);
        }

        public bool TrySendSample(in ClientInputSample<TInput> sample, out ClientInputSendResult result)
        {
            int payloadBytes = _encoder(in sample, _payloadScratch);
            if (payloadBytes <= 0 || payloadBytes > _payloadScratch.Length)
            {
                result = default;
                return false;
            }

            _transport.EnqueueOutbound(new OutboundDatagram(
                _connectionId,
                _channel,
                _payloadScratch.AsMemory(0, payloadBytes)));

            result = new ClientInputSendResult(sample.Sequence, payloadBytes);
            return true;
        }

        public int SendPending(int maxCount = int.MaxValue)
        {
            if (maxCount <= 0)
                return 0;

            int copied = CopyPendingToScratch();
            int sent = 0;
            int count = copied < maxCount ? copied : maxCount;
            for (int i = 0; i < count; i++)
            {
                if (TrySendSample(in _pendingScratch[i], out _))
                    sent++;
            }

            return sent;
        }

        private int CopyPendingToScratch()
        {
            while (true)
            {
                int copied = _sequencer.CopyPendingTo(_pendingScratch);
                if (copied < _pendingScratch.Length)
                    return copied;

                int newSize = _pendingScratch.Length << 1;
                if (newSize < 64)
                    newSize = 64;
                _pendingScratch = new ClientInputSample<TInput>[newSize];
            }
        }
    }
}
