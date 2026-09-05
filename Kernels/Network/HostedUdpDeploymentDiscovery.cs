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

internal static class HostedUdpDeploymentDiscoveryCodec
{
    private const byte RequestKind = 0xD1;
    private const byte AnnouncementKind = 0xD2;
    private static ReadOnlySpan<byte> SignatureDomain =>
        "UltraSim/deployment-key/v1"u8;
    internal const int NonceBytes = 32;
    internal const int MaximumPublicKeyBytes = 128;
    internal const int RequestBytes = 256;
    private const int SignatureBytes = 64;

    internal static void WriteRequest(
        Span<byte> destination,
        NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> nonce)
    {
        if (destination.Length < RequestBytes || nonce.Length != NonceBytes)
            throw new ArgumentException("The deployment discovery request buffer is invalid.");
        Span<byte> request = destination[..RequestBytes];
        request.Clear();
        var writer = new NetworkPacketWriter(request);
        if (!writer.TryWriteByte(RequestKind) ||
            !writer.TryWriteUInt16(manifest.Version) ||
            !writer.TryWriteUInt64(manifest.Fingerprint) ||
            !writer.TryWriteBytes(nonce))
            throw new InvalidOperationException("The deployment discovery request cannot be encoded.");
    }

    internal static bool TryReadRequest(
        ReadOnlySpan<byte> source,
        NetworkProtocolManifest manifest,
        out ReadOnlySpan<byte> nonce)
    {
        nonce = default;
        if (source.Length != RequestBytes) return false;
        var reader = new NetworkPacketReader(source);
        if (!reader.TryReadByte(out byte kind) || kind != RequestKind ||
            !reader.TryReadUInt16(out ushort version) || version != manifest.Version ||
            !reader.TryReadUInt64(out ulong fingerprint) || fingerprint != manifest.Fingerprint ||
            !reader.TryReadBytes(NonceBytes, out nonce) ||
            !reader.TryReadBytes(reader.Remaining, out ReadOnlySpan<byte> padding))
        {
            nonce = default;
            return false;
        }
        for (int index = 0; index < padding.Length; index++)
            if (padding[index] != 0)
            {
                nonce = default;
                return false;
            }
        return true;
    }

    internal static int WriteAnnouncement(
        Span<byte> destination,
        NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> nonce,
        NetworkServerIdentity identity)
    {
        ReadOnlySpan<byte> publicKey = identity.PublicKey;
        if (nonce.Length != NonceBytes || publicKey.Length is < 64 or > MaximumPublicKeyBytes)
            throw new ArgumentException("The deployment announcement identity is invalid.");
        Span<byte> signed = stackalloc byte[
            SignatureDomain.Length + sizeof(ushort) + sizeof(ulong) + NonceBytes +
            sizeof(ushort) + MaximumPublicKeyBytes];
        int signedLength = WriteSignedPrefix(signed, manifest, nonce, publicKey);
        Span<byte> signature = stackalloc byte[SignatureBytes];
        if (!identity.TrySign(signed[..signedLength], signature, out int signatureLength) ||
            signatureLength != SignatureBytes)
            throw new CryptographicException("The deployment candidate could not be signed.");
        var writer = new NetworkPacketWriter(destination);
        if (!writer.TryWriteByte(AnnouncementKind) ||
            !writer.TryWriteUInt16(manifest.Version) ||
            !writer.TryWriteUInt64(manifest.Fingerprint) ||
            !writer.TryWriteBytes(nonce) ||
            !writer.TryWriteUInt16(checked((ushort)publicKey.Length)) ||
            !writer.TryWriteBytes(publicKey) ||
            !writer.TryWriteBytes(signature))
            throw new InvalidOperationException("The deployment announcement buffer is too small.");
        return writer.WrittenCount;
    }

    internal static bool TryReadAnnouncement(
        ReadOnlySpan<byte> source,
        NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> expectedNonce,
        out ReadOnlySpan<byte> publicKey)
    {
        publicKey = default;
        var reader = new NetworkPacketReader(source);
        if (!reader.TryReadByte(out byte kind) || kind != AnnouncementKind ||
            !reader.TryReadUInt16(out ushort version) || version != manifest.Version ||
            !reader.TryReadUInt64(out ulong fingerprint) || fingerprint != manifest.Fingerprint ||
            !reader.TryReadBytes(NonceBytes, out ReadOnlySpan<byte> nonce) ||
            !nonce.SequenceEqual(expectedNonce) ||
            !reader.TryReadUInt16(out ushort publicLength) ||
            publicLength is < 64 or > MaximumPublicKeyBytes ||
            !reader.TryReadBytes(publicLength, out publicKey) ||
            !reader.TryReadBytes(SignatureBytes, out ReadOnlySpan<byte> signature) ||
            !reader.IsComplete)
        {
            publicKey = default;
            return false;
        }
        try
        {
            using ECDsa candidate = ECDsa.Create();
            candidate.ImportSubjectPublicKeyInfo(publicKey, out int read);
            if (read != publicKey.Length || candidate.KeySize != 256)
                throw new CryptographicException();
            Span<byte> signed = stackalloc byte[
                SignatureDomain.Length + sizeof(ushort) + sizeof(ulong) + NonceBytes +
                sizeof(ushort) + MaximumPublicKeyBytes];
            int signedLength = WriteSignedPrefix(signed, manifest, nonce, publicKey);
            if (!candidate.VerifyData(signed[..signedLength], signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new CryptographicException();
            return true;
        }
        catch (CryptographicException)
        {
            publicKey = default;
            return false;
        }
    }

    private static int WriteSignedPrefix(
        Span<byte> destination,
        NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey)
    {
        var writer = new NetworkPacketWriter(destination);
        if (!writer.TryWriteBytes(SignatureDomain) ||
            !writer.TryWriteUInt16(manifest.Version) ||
            !writer.TryWriteUInt64(manifest.Fingerprint) ||
            !writer.TryWriteBytes(nonce) ||
            !writer.TryWriteUInt16(checked((ushort)publicKey.Length)) ||
            !writer.TryWriteBytes(publicKey))
            throw new InvalidOperationException("The deployment signature transcript is too small.");
        return writer.WrittenCount;
    }
}
