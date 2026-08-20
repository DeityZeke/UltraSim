using System;

namespace UltraSim.Network
{
    public readonly struct OutboundDatagram
    {
        public readonly int ConnectionId;
        public readonly ChannelType Channel;
        public readonly ReadOnlyMemory<byte> Payload;

        public OutboundDatagram(int connectionId, ChannelType channel, ReadOnlyMemory<byte> payload)
        {
            ConnectionId = connectionId;
            Channel = channel;
            Payload = payload;
        }
    }
}

