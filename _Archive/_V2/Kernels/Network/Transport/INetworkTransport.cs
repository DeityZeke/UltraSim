namespace UltraSim.Network;

/// <summary>
/// Minimal engine-agnostic transport contract.
/// </summary>
public interface INetworkTransport : IDisposable
{
    bool IsRunning { get; }

    void Start();
    void Stop();

    int PollInbound(Span<InboundDatagram> destination);
    void EnqueueOutbound(in OutboundDatagram datagram);
    void FlushOutbound();
}
