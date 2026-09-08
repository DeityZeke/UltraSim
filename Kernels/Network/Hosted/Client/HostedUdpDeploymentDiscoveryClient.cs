using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace UltraSim.Network;

/// <summary>Untrusted pre-handshake deployment-key discovery state.</summary>
public enum HostedUdpDeploymentDiscoveryState : byte
{
    Discovering = 1,
    Discovered = 2,
    Failed = 3,
    Closed = 4,
}

public readonly record struct HostedUdpDeploymentDiscoverySnapshot(
    HostedUdpDeploymentDiscoveryState State,
    int Pumps,
    ulong RequestsSent,
    ulong InvalidAnnouncements,
    int CandidatePublicKeyBytes,
    UdpDatagramEndpointSnapshot Datagrams);

/// <summary>
/// Owner-thread client for an unauthenticated, self-signed deployment-key candidate.
/// A discovered key is not trusted. Human/application policy must approve it before
/// constructing the existing pinned-only <see cref="HostedUdpProtocolClient"/>.
/// </summary>
public sealed class HostedUdpDeploymentDiscoveryClient : IDisposable
{
    private const ulong RetryMilliseconds = 250;
    private const ulong TimeoutMilliseconds = 10_000;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly NetworkProtocolManifest _manifest;
    private readonly INetworkMonotonicClock _clock;
    private readonly UdpDatagramEndpoint _endpoint;
    private readonly byte[] _nonce = new byte[HostedUdpDeploymentDiscoveryCodec.NonceBytes];
    private readonly byte[] _request = new byte[HostedUdpDeploymentDiscoveryCodec.RequestBytes];
    private readonly byte[] _receive;
    private readonly byte[] _candidate = new byte[HostedUdpDeploymentDiscoveryCodec.MaximumPublicKeyBytes];
    private readonly ulong _started;
    private ulong _nextRequest;
    private int _candidateLength;
    private int _pumps;
    private ulong _requestsSent;
    private ulong _invalidAnnouncements;
    private HostedUdpDeploymentDiscoveryState _state;
    private bool _disposed;

    private HostedUdpDeploymentDiscoveryClient(
        NetworkProtocolManifest manifest,
        INetworkMonotonicClock clock,
        UdpDatagramEndpoint endpoint,
        int maximumDatagramBytes)
    {
        _manifest = manifest;
        _clock = clock;
        _endpoint = endpoint;
        _receive = new byte[maximumDatagramBytes];
        RandomNumberGenerator.Fill(_nonce);
        HostedUdpDeploymentDiscoveryCodec.WriteRequest(_request, manifest, _nonce);
        _started = clock.Milliseconds;
        _nextRequest = _started;
        _state = HostedUdpDeploymentDiscoveryState.Discovering;
    }

    public HostedUdpDeploymentDiscoveryState State
    {
        get { EnsureOperational(); return _state; }
    }

    public int CandidatePublicKeyBytes
    {
        get { EnsureOperational(); return _candidateLength; }
    }

    public static bool TryCreate(
        IPEndPoint serverEndPoint,
        UdpProtocolLimits udpLimits,
        out HostedUdpDeploymentDiscoveryClient? client,
        NetworkProtocolManifest? manifest = null,
        IPEndPoint? localEndPoint = null) =>
        TryCreateCore(serverEndPoint, udpLimits, StopwatchNetworkMonotonicClock.Instance,
            out client, manifest, localEndPoint);

    internal static bool TryCreateForTesting(
        IPEndPoint serverEndPoint,
        UdpProtocolLimits udpLimits,
        INetworkMonotonicClock clock,
        out HostedUdpDeploymentDiscoveryClient? client,
        NetworkProtocolManifest? manifest = null,
        IPEndPoint? localEndPoint = null) =>
        TryCreateCore(serverEndPoint, udpLimits, clock, out client, manifest, localEndPoint);

    private static bool TryCreateCore(
        IPEndPoint serverEndPoint,
        UdpProtocolLimits udpLimits,
        INetworkMonotonicClock clock,
        out HostedUdpDeploymentDiscoveryClient? client,
        NetworkProtocolManifest? manifest,
        IPEndPoint? localEndPoint)
    {
        client = null;
        ArgumentNullException.ThrowIfNull(serverEndPoint);
        ArgumentNullException.ThrowIfNull(clock);
        try
        {
            udpLimits.Validate();
            manifest ??= CoreNetworkProtocol.Manifest;
            UdpDatagramEndpoint endpoint = UdpDatagramEndpoint.CreateConnectedClient(
                udpLimits,
                localEndPoint ?? new IPEndPoint(IPAddress.Any, 0),
                serverEndPoint);
            try
            {
                client = new HostedUdpDeploymentDiscoveryClient(
                    manifest, clock, endpoint, udpLimits.MaximumDatagramBytes);
                return true;
            }
            catch
            {
                endpoint.Dispose();
                throw;
            }
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public void Pump()
    {
        EnsureOperational();
        if (_state != HostedUdpDeploymentDiscoveryState.Discovering) return;
        _pumps++;
        ulong now = _clock.Milliseconds;
        if (now - _started >= TimeoutMilliseconds)
        {
            _state = HostedUdpDeploymentDiscoveryState.Failed;
            return;
        }
        if (now >= _nextRequest)
        {
            if (_endpoint.TrySend(_request) == DatagramSendStatus.Accepted)
                _requestsSent++;
            _nextRequest = checked(now + RetryMilliseconds);
        }
        while (_endpoint.TryReceive(_receive, out int length))
        {
            if (!HostedUdpDeploymentDiscoveryCodec.TryReadAnnouncement(
                    _receive.AsSpan(0, length), _manifest, _nonce,
                    out ReadOnlySpan<byte> candidate))
            {
                _invalidAnnouncements++;
                continue;
            }
            candidate.CopyTo(_candidate);
            _candidateLength = candidate.Length;
            _state = HostedUdpDeploymentDiscoveryState.Discovered;
            return;
        }
    }

    public bool TryCopyCandidatePublicKey(Span<byte> destination, out int written)
    {
        EnsureOperational();
        written = 0;
        if (_state != HostedUdpDeploymentDiscoveryState.Discovered ||
            destination.Length < _candidateLength) return false;
        _candidate.AsSpan(0, _candidateLength).CopyTo(destination);
        written = _candidateLength;
        return true;
    }

    public HostedUdpDeploymentDiscoverySnapshot GetSnapshot()
    {
        EnsureOperational();
        return new(_state, _pumps, _requestsSent, _invalidAnnouncements,
            _candidateLength, _endpoint.GetSnapshot());
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _endpoint.Dispose();
        CryptographicOperations.ZeroMemory(_nonce);
        CryptographicOperations.ZeroMemory(_request);
        CryptographicOperations.ZeroMemory(_receive);
        CryptographicOperations.ZeroMemory(_candidate);
        _candidateLength = 0;
        _state = HostedUdpDeploymentDiscoveryState.Closed;
        _disposed = true;
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                "The hosted deployment discovery client is owner-thread-bound.");
    }
}
