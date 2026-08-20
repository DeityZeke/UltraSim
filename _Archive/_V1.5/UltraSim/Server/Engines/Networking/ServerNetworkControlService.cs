using System;
using System.Diagnostics;
using UltraSim.Network;
using UltraSim.Network.Protocol;

namespace UltraSim.Server.Engines.Networking
{
    public sealed class ServerNetworkControlService
    {
        public readonly struct TimingSnapshot
        {
            public TimingSnapshot(
                long pongBuildCalls,
                long pongBuildElapsedTicksTotal,
                long pongBuildElapsedTicksMax,
                long stampedPingRxSamples,
                long stampedPingRxElapsedTicksTotal,
                long stampedPingRxElapsedTicksMax)
            {
                PongBuildCalls = pongBuildCalls;
                PongBuildElapsedTicksTotal = pongBuildElapsedTicksTotal;
                PongBuildElapsedTicksMax = pongBuildElapsedTicksMax;
                StampedPingRxSamples = stampedPingRxSamples;
                StampedPingRxElapsedTicksTotal = stampedPingRxElapsedTicksTotal;
                StampedPingRxElapsedTicksMax = stampedPingRxElapsedTicksMax;
            }

            public long PongBuildCalls { get; }

            public long PongBuildElapsedTicksTotal { get; }

            public long PongBuildElapsedTicksMax { get; }

            public long StampedPingRxSamples { get; }

            public long StampedPingRxElapsedTicksTotal { get; }

            public long StampedPingRxElapsedTicksMax { get; }
        }

        private readonly INetworkTransport _transport;
        private readonly NetworkSessionManager _sessions;
        private long _pongBuildCalls;
        private long _pongBuildElapsedTicksTotal;
        private long _pongBuildElapsedTicksMax;
        private long _stampedPingRxSamples;
        private long _stampedPingRxElapsedTicksTotal;
        private long _stampedPingRxElapsedTicksMax;
        public Action<long>? ReturnToCharacterSelectRequested { get; set; }

        public ServerNetworkControlService(INetworkTransport transport, NetworkSessionManager sessions)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public long PingProbeRxCount { get; private set; }
        public long PongProbeTxCount { get; private set; }

        public void ResetCounters()
        {
            PingProbeRxCount = 0;
            PongProbeTxCount = 0;
            _pongBuildCalls = 0;
            _pongBuildElapsedTicksTotal = 0;
            _pongBuildElapsedTicksMax = 0;
            _stampedPingRxSamples = 0;
            _stampedPingRxElapsedTicksTotal = 0;
            _stampedPingRxElapsedTicksMax = 0;
        }

        public TimingSnapshot GetTimingSnapshot()
        {
            return new TimingSnapshot(
                _pongBuildCalls,
                _pongBuildElapsedTicksTotal,
                _pongBuildElapsedTicksMax,
                _stampedPingRxSamples,
                _stampedPingRxElapsedTicksTotal,
                _stampedPingRxElapsedTicksMax);
        }

        public bool TryHandleInboundDatagram(long sessionId, in InboundDatagram datagram)
        {
            if (datagram.Channel != ChannelType.ReliableOrdered &&
                datagram.Channel != ChannelType.UnreliableSequenced)
            {
                return false;
            }

            if (NetworkControlMessageCodec.TryDecodeReturnToCharacterSelect(datagram.Payload.Span))
            {
                ReturnToCharacterSelectRequested?.Invoke(sessionId);
                return true;
            }

            if (!NetworkControlMessageCodec.TryDecodePingProbe(datagram.Payload.Span, out ushort sequence, out long clientTick))
                return false;

            PingProbeRxCount++;
            long startTicks = Stopwatch.GetTimestamp();
            if (NetworkControlMessageCodec.TryReadStampedPingProbeClientTimestamp(
                    datagram.Payload.Span,
                    out long clientTimestampTicks) &&
                clientTimestampTicks > 0 &&
                startTicks >= clientTimestampTicks)
            {
                long pingRxTicks = startTicks - clientTimestampTicks;
                _stampedPingRxSamples++;
                _stampedPingRxElapsedTicksTotal += pingRxTicks;
                if (pingRxTicks > _stampedPingRxElapsedTicksMax)
                    _stampedPingRxElapsedTicksMax = pingRxTicks;
            }

            if (!_sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot))
                return true;

            long serverTimestampTicks = Stopwatch.GetTimestamp();
            byte[] payload = new byte[NetworkControlMessageCodec.PongProbeStampedPayloadBytes];
            if (!NetworkControlMessageCodec.TryEncodeStampedPongProbe(payload, sequence, clientTick, serverTimestampTicks))
                return true;

            _transport.EnqueueOutbound(new OutboundDatagram(
                snapshot.ConnectionId,
                datagram.Channel,
                payload));
            PongProbeTxCount++;
            long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            _pongBuildCalls++;
            _pongBuildElapsedTicksTotal += elapsedTicks;
            if (elapsedTicks > _pongBuildElapsedTicksMax)
                _pongBuildElapsedTicksMax = elapsedTicks;
            return true;
        }
    }
}
