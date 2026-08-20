using System;

namespace UltraSim.Network
{
    public readonly struct InboundDatagram
    {
        public readonly int ConnectionId;
        public readonly ChannelType Channel;
        public readonly ReadOnlyMemory<byte> Payload;

        public InboundDatagram(int connectionId, ChannelType channel, ReadOnlyMemory<byte> payload)
        {
            ConnectionId = connectionId;
            Channel = channel;
            Payload = payload;
        }
    }
}

