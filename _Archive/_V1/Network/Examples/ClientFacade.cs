using System.Threading;
using System.Threading.Tasks;
using Network.Client.Network;

namespace Network.Examples;

/// <summary>
/// Thin wrapper that wires the engine-agnostic NetworkClient with game-friendly defaults.
/// </summary>
public sealed class ClientFacade : IAsyncDisposable
{
    public NetworkClient Client { get; } = new();

    public Task ConnectAsync(string host = "localhost", int tcpPort = 2593, int udpPort = 2594, CancellationToken ct = default) =>
        Client.ConnectAsync(host, tcpPort, udpPort, ct);

    public void Disconnect(string reason = "Client requested") => Client.Disconnect(reason);

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        return ValueTask.CompletedTask;
    }
}
