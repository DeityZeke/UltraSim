using System.Diagnostics;
using System.Net;
using UltraSim.Network;

namespace UltraSim.Client.Godot;

public sealed class GodotClientNetworkSessionDriver : IDisposable
{
    private readonly Action<string> _statusSink;
    private readonly Action<ChatMessageEventPacket> _chatSink;
    private ClientMovementSessionHost? _host;
    private UdpSocketNetworkTransport? _transport;
    private bool _disposed;
    private uint _clientTick;
    private bool _lastReady;

    public GodotClientNetworkSessionDriver(Action<string> statusSink, Action<ChatMessageEventPacket> chatSink)
    {
        _statusSink = statusSink ?? throw new ArgumentNullException(nameof(statusSink));
        _chatSink = chatSink ?? throw new ArgumentNullException(nameof(chatSink));
    }

    public bool IsRunning => _host?.IsRunning == true;
    public bool IsReady => _host?.IsReady == true;
    public ClientMovementSessionHost? Host => _host;

    public bool TryConnect(ClientDirectConnectRequest request, out string error)
    {
        ThrowIfDisposed();
        Disconnect("Restarting client session.");

        if (!request.TryResolve(out IPEndPoint remoteEndPoint, out error))
        {
            return false;
        }

        var metadata = DefaultPacketMetadata.Create();
        var transport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Any, 0),
            metadata,
            remoteEndPoint,
            new ConnectionId(1),
            AllowDynamicConnections: false));

        var chatEvents = new ClientChatEventBridge(packet =>
        {
            _chatSink(packet);
        });
        var observerEvents = ClientObserverEventBridge.CreateSpanFastPath(
            new ConnectionId(1),
            (_, _) => { });
        observerEvents.ChatEvents = chatEvents;

        var host = new ClientMovementSessionHost(
            transport,
            new ConnectionId(1),
            new HandshakeHelloPacket(3, 4, 5, FeatureFlags.SharedPayloadFanout | FeatureFlags.Compression),
            observerEvents: observerEvents,
            chatEvents: chatEvents);

        transport.Start();
        host.Start();
        host.BeginHandshake(Stopwatch.GetTimestamp());

        _transport = transport;
        _host = host;
        _statusSink($"Connecting to {request.Host}:{request.Port}...");
        error = string.Empty;
        return true;
    }

    public void Pump()
    {
        if (_host is null)
        {
            return;
        }

        _host.Pump(Stopwatch.GetTimestamp());
        _clientTick++;

        if (_host.IsReady)
        {
            if (!_lastReady)
            {
                _statusSink("Connected.");
            }

            _lastReady = true;
        }
    }

    public bool TrySendChat(string text)
    {
        return _host?.TrySendChat(text) == true;
    }

    public void Disconnect(string status = "Disconnected.")
    {
        _host?.Stop();
        _host = null;
        _transport?.Dispose();
        _transport = null;
        _clientTick = 0;
        _lastReady = false;
        _statusSink(status);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Disconnect("Disconnected.");
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GodotClientNetworkSessionDriver));
        }
    }
}
