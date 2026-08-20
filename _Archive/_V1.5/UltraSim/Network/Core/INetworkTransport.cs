using System;

namespace UltraSim.Network
{
    public interface INetworkTransport : IDisposable
    {
        void Start();
        void Stop();

        int PollInbound(Span<InboundDatagram> destination);
        void EnqueueOutbound(in OutboundDatagram datagram);
        void FlushOutbound();

        // Inbound payload memory is only valid for the active consume window.
        // Callers may release that window explicitly before the next poll.
        void CompleteInboundConsume();
    }
}

