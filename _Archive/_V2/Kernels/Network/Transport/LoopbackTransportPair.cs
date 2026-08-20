namespace UltraSim.Network;

/// <summary>
/// Paired loopback transports that can be used as server/client test peers.
/// </summary>
public sealed class LoopbackTransportPair : IDisposable
{
    public LoopbackTransportPair()
    {
        Server = new LoopbackNetworkTransport();
        Client = new LoopbackNetworkTransport();
        Server.Peer = Client;
        Client.Peer = Server;
    }

    public LoopbackNetworkTransport Server { get; }

    public LoopbackNetworkTransport Client { get; }

    public void Dispose()
    {
        Server.Dispose();
        Client.Dispose();
    }
}
