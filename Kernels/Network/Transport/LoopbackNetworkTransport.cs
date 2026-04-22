using System.Collections.Concurrent;

namespace UltraSim.Network;

/// <summary>
/// In-memory transport used for tests and same-process loopback scenarios.
/// </summary>
public sealed class LoopbackNetworkTransport : INetworkTransport
{
    private readonly ConcurrentQueue<OutboundDatagram> _outbound = new();
    private readonly ConcurrentQueue<InboundDatagram> _inbound = new();
    private volatile bool _disposed;
    private volatile bool _running;

    internal LoopbackNetworkTransport? Peer { get; set; }

    public bool IsRunning => _running && !_disposed;

    public void Start()
    {
        ThrowIfDisposed();
        _running = true;
    }

    public void Stop()
    {
        if (_disposed)
            return;

        _running = false;
    }

    public int PollInbound(Span<InboundDatagram> destination)
    {
        if (!IsRunning || destination.Length == 0)
            return 0;

        int count = 0;
        while (count < destination.Length && _inbound.TryDequeue(out InboundDatagram datagram))
        {
            destination[count++] = datagram;
        }

        return count;
    }

    public void EnqueueOutbound(in OutboundDatagram datagram)
    {
        if (!IsRunning)
            throw new InvalidOperationException("Transport must be running before enqueue.");

        _outbound.Enqueue(datagram);
    }

    public void FlushOutbound()
    {
        if (!IsRunning)
            return;

        LoopbackNetworkTransport? peer = Peer;
        if (peer is null || !peer.IsRunning)
        {
            while (_outbound.TryDequeue(out _))
            {
            }

            return;
        }

        while (_outbound.TryDequeue(out OutboundDatagram outbound))
        {
            peer._inbound.Enqueue(new InboundDatagram(
                outbound.ConnectionId,
                outbound.Plane,
                outbound.PacketClass,
                outbound.Channel,
                outbound.Payload.Memory));
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _running = false;

        while (_outbound.TryDequeue(out _))
        {
        }

        while (_inbound.TryDequeue(out _))
        {
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LoopbackNetworkTransport));
    }
}
