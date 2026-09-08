using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace UltraSim.Network;

/// <summary>Fixed socket/session bounds for one owner-thread localhost UDP topology.</summary>
/// <remarks>
/// <c>CookieBucketMilliseconds</c> is one rotating bucket duration. Validation accepts
/// the current and immediately previous bucket, so maximum cookie age can approach twice it.
/// </remarks>
public readonly record struct UdpProtocolLimits(
    int MaximumDatagramBytes = 1_472,
    int SocketSendBufferBytes = 262_144,
    int SocketReceiveBufferBytes = 262_144,
    int MaximumHandshakePumps = 4_096,
    int CookieBucketMilliseconds = 30_000,
    int IdleTimeoutMilliseconds = 30_000,
    int DrainTimeoutMilliseconds = 5_000,
    int KeepAliveIntervalMilliseconds = 5_000)
{
    public static UdpProtocolLimits Default => new(
        MaximumDatagramBytes: 1_472,
        SocketSendBufferBytes: 262_144,
        SocketReceiveBufferBytes: 262_144,
        MaximumHandshakePumps: 4_096,
        CookieBucketMilliseconds: 30_000,
        IdleTimeoutMilliseconds: 30_000,
        DrainTimeoutMilliseconds: 5_000,
        KeepAliveIntervalMilliseconds: 5_000);

    internal void Validate()
    {
        if (MaximumDatagramBytes is < 256 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(MaximumDatagramBytes));
        if (SocketSendBufferBytes < MaximumDatagramBytes || SocketSendBufferBytes > 1_073_741_824)
            throw new ArgumentOutOfRangeException(nameof(SocketSendBufferBytes));
        if (SocketReceiveBufferBytes < MaximumDatagramBytes || SocketReceiveBufferBytes > 1_073_741_824)
            throw new ArgumentOutOfRangeException(nameof(SocketReceiveBufferBytes));
        if (MaximumHandshakePumps is <= 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(MaximumHandshakePumps));
        if (CookieBucketMilliseconds is <= 0 or > 3_600_000)
            throw new ArgumentOutOfRangeException(nameof(CookieBucketMilliseconds));
        if (IdleTimeoutMilliseconds is <= 0 or > 86_400_000)
            throw new ArgumentOutOfRangeException(nameof(IdleTimeoutMilliseconds));
        if (DrainTimeoutMilliseconds <= 0 || DrainTimeoutMilliseconds > IdleTimeoutMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(DrainTimeoutMilliseconds));
        if (KeepAliveIntervalMilliseconds <= 0 || KeepAliveIntervalMilliseconds >= IdleTimeoutMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(KeepAliveIntervalMilliseconds));
    }

    /// <summary>
    /// Reports configured socket-buffer payload for the two connected endpoints.
    /// The operating system may round or cap the actual kernel reservation.
    /// </summary>
    public long EstimateConfiguredSocketBufferBytes()
    {
        Validate();
        return checked(2L * (SocketSendBufferBytes + SocketReceiveBufferBytes));
    }
}

public enum UdpProtocolSessionState : byte
{
    Established = 0,
    Draining = 1,
    Closed = 2,
}

public readonly record struct UdpDatagramEndpointSnapshot(
    ulong Sent,
    ulong Received,
    ulong SentBytes,
    ulong ReceivedBytes,
    ulong CapacityDrops,
    ulong RejectedDatagrams,
    ulong SendCalls,
    ulong ReceivePollCalls,
    ulong ReceiveCalls,
    ulong WouldBlockEvents,
    int MaximumSentDatagramBytes,
    int MaximumReceivedDatagramBytes,
    bool IsOpen,
    SocketError LastSocketError);

internal interface IUdpKnownDatagramHandler
{
    bool TryHandleKnownDatagram(ReadOnlySpan<byte> datagram);
}

internal sealed class UdpDatagramEndpoint : INetworkDatagramEndpoint, IDisposable
{
    private readonly Socket _socket;
    private readonly int _maximumDatagramBytes;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _disposed;
    private IUdpKnownDatagramHandler? _knownDatagramHandler;
    internal bool SuppressSendsForTesting { get; set; }
    private ulong _sent;
    private ulong _received;
    private ulong _sentBytes;
    private ulong _receivedBytes;
    private ulong _capacityDrops;
    private ulong _rejected;
    private ulong _sendCalls;
    private ulong _receivePollCalls;
    private ulong _receiveCalls;
    private ulong _wouldBlockEvents;
    private int _maximumSentDatagramBytes;
    private int _maximumReceivedDatagramBytes;
    private SocketError _lastError;

    private UdpDatagramEndpoint(Socket socket, int maximumDatagramBytes)
    {
        _socket = socket;
        _maximumDatagramBytes = maximumDatagramBytes;
        LocalEndPoint = (IPEndPoint)socket.LocalEndPoint!;
        IPEndPoint remote = (IPEndPoint)socket.RemoteEndPoint!;
        RemotePathBinding = CreatePathBinding(remote);
    }

    internal IPEndPoint LocalEndPoint { get; }
    internal byte[] RemotePathBinding { get; }

    internal void BindKnownDatagramHandler(IUdpKnownDatagramHandler handler)
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(handler);
        if (_knownDatagramHandler is not null)
            throw new InvalidOperationException("The connected UDP handshake handler is already bound.");
        _knownDatagramHandler = handler;
    }

    internal static void CreateConnectedLoopbackPair(
        UdpProtocolLimits limits,
        out UdpDatagramEndpoint client,
        out UdpDatagramEndpoint server)
    {
        Socket? clientSocket = null;
        Socket? serverSocket = null;
        try
        {
            clientSocket = CreateSocket(limits);
            serverSocket = CreateSocket(limits);
            clientSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            serverSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            clientSocket.Connect(serverSocket.LocalEndPoint!);
            serverSocket.Connect(clientSocket.LocalEndPoint!);
            clientSocket.Blocking = false;
            serverSocket.Blocking = false;
            client = new UdpDatagramEndpoint(clientSocket, limits.MaximumDatagramBytes);
            server = new UdpDatagramEndpoint(serverSocket, limits.MaximumDatagramBytes);
        }
        catch
        {
            clientSocket?.Dispose();
            serverSocket?.Dispose();
            throw;
        }
    }

    internal static UdpDatagramEndpoint CreateConnectedLoopbackClient(
        UdpProtocolLimits limits,
        IPEndPoint serverEndPoint)
        => CreateConnectedClient(
            limits,
            new IPEndPoint(IPAddress.Loopback, 0),
            serverEndPoint);

    internal static UdpDatagramEndpoint CreateConnectedClient(
        UdpProtocolLimits limits,
        IPEndPoint localEndPoint,
        IPEndPoint serverEndPoint)
    {
        limits.Validate();
        ArgumentNullException.ThrowIfNull(localEndPoint);
        ArgumentNullException.ThrowIfNull(serverEndPoint);
        if (localEndPoint.AddressFamily != AddressFamily.InterNetwork ||
            serverEndPoint.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Hosted H0 currently supports IPv4 endpoints only.");
        Socket? socket = null;
        try
        {
            socket = CreateSocket(limits);
            socket.Bind(localEndPoint);
            socket.Connect(serverEndPoint);
            socket.Blocking = false;
            return new UdpDatagramEndpoint(socket, limits.MaximumDatagramBytes);
        }
        catch
        {
            socket?.Dispose();
            throw;
        }
    }

    public DatagramSendStatus TrySend(ReadOnlySpan<byte> datagram)
    {
        EnsureOwnerThread();
        if (_disposed) return DatagramSendStatus.CapacityExceeded;
        if (datagram.Length > _maximumDatagramBytes)
        {
            _rejected++;
            return DatagramSendStatus.DatagramTooLarge;
        }
        if (SuppressSendsForTesting) return DatagramSendStatus.DroppedByImpairment;
        try
        {
            _sendCalls++;
            if (_socket.Send(datagram, SocketFlags.None) != datagram.Length)
            {
                _capacityDrops++;
                return DatagramSendStatus.CapacityExceeded;
            }
            _sent++;
            _sentBytes = SaturatingAdd(_sentBytes, checked((ulong)datagram.Length));
            _maximumSentDatagramBytes = Math.Max(_maximumSentDatagramBytes, datagram.Length);
            return DatagramSendStatus.Accepted;
        }
        catch (SocketException exception) when (IsTransient(exception.SocketErrorCode))
        {
            _lastError = exception.SocketErrorCode;
            _capacityDrops++;
            _wouldBlockEvents++;
            return DatagramSendStatus.CapacityExceeded;
        }
        catch (SocketException exception)
        {
            _lastError = exception.SocketErrorCode;
            _rejected++;
            return DatagramSendStatus.CapacityExceeded;
        }
    }

    public bool TryReceive(Span<byte> destination, out int length)
    {
        EnsureOwnerThread();
        length = 0;
        if (_disposed) return false;
        if (destination.Length < _maximumDatagramBytes)
            throw new ArgumentException("The UDP receive destination is below the admitted datagram size.", nameof(destination));
        // Avoid using WouldBlock exceptions as the normal empty-queue branch;
        // Linux SocketException construction otherwise allocates on every pump.
        _receivePollCalls++;
        if (!_socket.Poll(0, SelectMode.SelectRead)) return false;
        try
        {
            _receiveCalls++;
            length = _socket.Receive(destination, SocketFlags.None);
            if (length == 0) return false;
            _received++;
            _receivedBytes = SaturatingAdd(_receivedBytes, checked((ulong)length));
            _maximumReceivedDatagramBytes = Math.Max(_maximumReceivedDatagramBytes, length);
            if (_knownDatagramHandler?.TryHandleKnownDatagram(
                    destination[..length]) == true)
            {
                length = 0;
                return false;
            }
            return true;
        }
        catch (SocketException exception) when (exception.SocketErrorCode is
            SocketError.WouldBlock or SocketError.IOPending or SocketError.NoBufferSpaceAvailable)
        {
            _lastError = exception.SocketErrorCode;
            _wouldBlockEvents++;
            return false;
        }
        catch (SocketException exception)
        {
            _lastError = exception.SocketErrorCode;
            _rejected++;
            return false;
        }
    }

    internal UdpDatagramEndpointSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(_sent, _received, _sentBytes, _receivedBytes,
            _capacityDrops, _rejected, _sendCalls, _receivePollCalls, _receiveCalls,
            _wouldBlockEvents, _maximumSentDatagramBytes, _maximumReceivedDatagramBytes,
            !_disposed, _lastError);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _disposed = true;
        try { _socket.Shutdown(SocketShutdown.Both); } catch (SocketException) { }
        _socket.Dispose();
        _knownDatagramHandler = null;
        CryptographicOperations.ZeroMemory(RemotePathBinding);
    }

    internal static Socket CreateSocket(UdpProtocolLimits limits)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            SendBufferSize = limits.SocketSendBufferBytes,
            ReceiveBufferSize = limits.SocketReceiveBufferBytes,
        };
        return socket;
    }

    internal static byte[] CreatePathBinding(IPEndPoint remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        byte[] address = remote.Address.GetAddressBytes();
        byte[] binding = new byte[checked(1 + address.Length + 2)];
        binding[0] = checked((byte)address.Length);
        address.CopyTo(binding, 1);
        binding[^2] = checked((byte)(remote.Port >> 8));
        binding[^1] = unchecked((byte)remote.Port);
        return binding;
    }

    private static bool IsTransient(SocketError error) => error is
        SocketError.WouldBlock or SocketError.IOPending or SocketError.NoBufferSpaceAvailable;

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The UDP endpoint is bound to its creating owner thread.");
    }
}
