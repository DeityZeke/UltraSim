using Network.Server.Network;
using Network.Shared.Network;

namespace Network.Examples;

/// <summary>
/// Minimal console host that wires up the engine-agnostic networking stack.
/// Intended for quick smoke-testing outside of Godot/Unity.
/// </summary>
public static class ServerHost
{
    public static void Run()
    {
        var config = new NetworkConfig
        {
            TcpPort = 2593,
            UdpPort = 2594,
            MaxConnections = 64,
            ProtocolVersion = 1
        };

        using var server = new ConnectionManager(config);
        server.RegisterSystemHandlers();

        Console.WriteLine($"[Server] Listening TCP:{config.TcpPort} / UDP:{config.UdpPort} (Protocol {config.ProtocolVersion})");
        Console.WriteLine("Press ENTER to stop.");
        Console.ReadLine();
    }
}
