using System.Net;
using System.Net.Sockets;

/// <summary>
/// Test-only external UDP path. Both directions traverse the same bounded delay queue;
/// packet contents, authentication, and application dispatch are left to production Network.
/// </summary>
internal sealed class F18aUdpMotionProxy : IDisposable
{
    private const int MaximumPending = 512;
    private readonly Socket _socket;
    private readonly IPEndPoint _server;
    private readonly Thread _worker;
    private readonly byte[] _receive = new byte[1_472];
    private readonly PriorityQueue<Datagram, long> _pending = new();
    private readonly int _delayMilliseconds;
    private readonly int _jitterMilliseconds;
    private readonly int _dropPeriod;
    private IPEndPoint? _client;
    private long _forwarded;
    private long _dropped;
    private long _inboundSequence;
    private long _outboundSequence;
    private Exception? _fault;
    private volatile bool _stopping;

    internal F18aUdpMotionProxy(IPEndPoint server, int delayMilliseconds,
        int jitterMilliseconds = 0, int dropPeriod = 0)
    {
        if (delayMilliseconds is < 0 or > 250 || jitterMilliseconds is < 0 or > 100 ||
            jitterMilliseconds > delayMilliseconds || dropPeriod is < 0 or 1)
            throw new ArgumentOutOfRangeException(nameof(delayMilliseconds));
        _server = server;
        _delayMilliseconds = delayMilliseconds;
        _jitterMilliseconds = jitterMilliseconds;
        _dropPeriod = dropPeriod;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveTimeout = 2,
        };
        _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        EndPoint = (IPEndPoint)_socket.LocalEndPoint!;
        _worker = new Thread(Run) { IsBackground = true, Name = "F1.8A UDP motion proxy" };
        _worker.Start();
    }

    internal IPEndPoint EndPoint { get; }
    internal long Forwarded => Interlocked.Read(ref _forwarded);
    internal long Dropped => Interlocked.Read(ref _dropped);

    private void Run()
    {
        try
        {
            while (!_stopping)
            {
                FlushDue();
                EndPoint source = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    int length = _socket.ReceiveFrom(_receive, 0, _receive.Length,
                        SocketFlags.None, ref source);
                    if (length <= 0 || source is not IPEndPoint address) continue;
                    bool fromServer = address.Address.Equals(_server.Address) &&
                        address.Port == _server.Port;
                    IPEndPoint? target;
                    long sequence;
                    if (fromServer)
                    {
                        target = _client;
                        sequence = ++_outboundSequence;
                    }
                    else
                    {
                        _client = new IPEndPoint(address.Address, address.Port);
                        target = _server;
                        sequence = ++_inboundSequence;
                    }
                    if (target is null) continue;
                    if (_dropPeriod != 0 && sequence % _dropPeriod == 0)
                    {
                        Interlocked.Increment(ref _dropped);
                        continue;
                    }
                    if (_pending.Count == MaximumPending)
                        throw new InvalidOperationException("F1.8A proxy queue exceeded its bound.");
                    int jitter = _jitterMilliseconds == 0 ? 0 :
                        (int)((sequence * 17 + (fromServer ? 3 : 11)) %
                            (2L * _jitterMilliseconds + 1)) - _jitterMilliseconds;
                    long due = Environment.TickCount64 + _delayMilliseconds + jitter;
                    _pending.Enqueue(new Datagram(_receive.AsSpan(0, length).ToArray(), target),
                        due);
                }
                catch (SocketException exception) when (
                    exception.SocketErrorCode is SocketError.TimedOut or SocketError.WouldBlock)
                {
                }
            }
        }
        catch (Exception exception) when (_stopping && exception is
            ObjectDisposedException or SocketException)
        {
        }
        catch (Exception exception)
        {
            _fault = exception;
        }
    }

    private void FlushDue()
    {
        long now = Environment.TickCount64;
        while (_pending.TryPeek(out _, out long due) && due <= now)
        {
            Datagram packet = _pending.Dequeue();
            _socket.SendTo(packet.Payload, SocketFlags.None, packet.Target);
            Interlocked.Increment(ref _forwarded);
        }
    }

    public void Dispose()
    {
        if (_stopping) return;
        _stopping = true;
        _socket.Dispose();
        if (!_worker.Join(2_000))
            throw new InvalidOperationException("F1.8A UDP motion proxy did not retire.");
        _pending.Clear();
        if (_fault is not null)
            throw new InvalidOperationException("F1.8A UDP motion proxy failed.", _fault);
    }

    private readonly record struct Datagram(byte[] Payload, IPEndPoint Target);
}
