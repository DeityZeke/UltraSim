using System.Net;
using System.Security.Cryptography;
using UltraSim.Client.Core;
using UltraSim.Game.Application;
using UltraSim.Game.Protocol;
using UltraSim.Network;

namespace UltraSim.Client.Network;

public enum ClientDirectConnectState : byte
{
    Idle = 1,
    Discovering = 2,
    AwaitingTrust = 3,
    Connecting = 4,
    AwaitingRealm = 5,
    Established = 6,
    Draining = 7,
    Closed = 8,
    Failed = 9,
}

public enum ClientDirectConnectFailure : byte
{
    None = 0,
    InvalidEndpoint = 1,
    DiscoveryUnavailable = 2,
    DiscoveryFailed = 3,
    ExplicitFingerprintMismatch = 4,
    TrustedKeyMismatch = 5,
    HandshakeUnavailable = 6,
    HandshakeFailed = 7,
    InvalidApplicationIdentity = 8,
    TrustConflict = 9,
    TrustPersistenceFailed = 10,
    TransportClosed = 11,
}

public readonly record struct ClientDirectConnectSnapshot(
    ClientDirectConnectState State,
    ClientDirectConnectFailure Failure,
    string Endpoint,
    ClientDeploymentKeyFingerprint CandidateFingerprint,
    bool PreviouslyTrusted,
    bool ExplicitFingerprintSupplied,
    ApplicationRealmId Realm,
    ulong ApplicationSessionEpoch,
    ConnectionId Connection,
    ClientSettingsScopeKey SettingsScope,
    string? Detail);

/// <summary>Canonical endpoint accepted by the current IPv4 Hosted deployment.</summary>
public readonly record struct ClientDirectConnectEndpoint
{
    private ClientDirectConnectEndpoint(IPEndPoint value, string canonical)
    {
        Value = value;
        Canonical = canonical;
    }

    public IPEndPoint? Value { get; }
    public string? Canonical { get; }
    public bool IsValid => Value is not null && Canonical is not null;

    public static bool TryParse(string? text, out ClientDirectConnectEndpoint endpoint)
    {
        endpoint = default;
        string value = text?.Trim() ?? string.Empty;
        int separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1 ||
            !IPAddress.TryParse(value.AsSpan(0, separator), out IPAddress? address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !int.TryParse(value.AsSpan(separator + 1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int port) ||
            port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
            return false;
        var valueEndpoint = new IPEndPoint(address, port);
        endpoint = new ClientDirectConnectEndpoint(
            valueEndpoint, $"{address}:{port}");
        return true;
    }

    public override string ToString() => Canonical ?? string.Empty;
}

/// <summary>
/// Owner-thread Direct Connect lifecycle. Candidate discovery remains untrusted;
/// only a pinned Hosted handshake followed by the in-session realm message can
/// publish/persist a verified deployment record.
/// </summary>
public sealed class ClientDirectConnectSession : IDisposable
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly ClientServerTrustRegistry _trust;
    private readonly ClientServerTrustFileStore _trustStore;
    private readonly NetworkProtocolLimits _protocolLimits;
    private readonly UdpProtocolLimits _udpLimits;
    private readonly byte[] _candidateKey = new byte[128];
    private HostedUdpDeploymentDiscoveryClient? _discovery;
    private HostedUdpProtocolClient? _transport;
    private HostedUdpConnectionTrafficSnapshot? _finalTraffic;
    private ClientDirectConnectEndpoint _endpoint;
    private ClientTrustedDeployment? _priorTrust;
    private ClientDeploymentKeyFingerprint _candidateFingerprint;
    private ClientDeploymentKeyFingerprint _explicitFingerprint;
    private ApplicationRealmId _realm;
    private ClientSettingsScopeKey _settingsScope;
    private ulong _applicationSessionEpoch;
    private int _candidateKeyLength;
    private ClientDirectConnectState _state = ClientDirectConnectState.Idle;
    private ClientDirectConnectFailure _failure;
    private string? _detail;
    private bool _disposed;

    public ClientDirectConnectSession(
        ClientServerTrustRegistry trust,
        ClientServerTrustFileStore trustStore,
        NetworkProtocolLimits? protocolLimits = null,
        UdpProtocolLimits? udpLimits = null)
    {
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(trustStore);
        _trust = trust;
        _trustStore = trustStore;
        _protocolLimits = protocolLimits ?? NetworkProtocolLimits.Default;
        _udpLimits = udpLimits ?? UdpProtocolLimits.Default;
    }

    public ClientDirectConnectState State
    {
        get { EnsureOperational(); return _state; }
    }

    /// <summary>
    /// Copies the current real Hosted UDP connection accounting. This is a cold
    /// diagnostic operation and is unavailable before a transport exists.
    /// </summary>
    public bool TryGetTrafficSnapshot(out HostedUdpConnectionTrafficSnapshot snapshot)
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_transport is null)
        {
            if (_finalTraffic is HostedUdpConnectionTrafficSnapshot finalized)
            {
                snapshot = finalized;
                return true;
            }
            snapshot = default;
            return false;
        }
        snapshot = _transport.GetTrafficSnapshot();
        return true;
    }

    public NetworkProtocolPeer? Peer
    {
        get { EnsureOperational(); return _state == ClientDirectConnectState.Established ? _transport?.Peer : null; }
    }

    public bool TryStart(string endpoint, string? explicitFingerprint = null)
    {
        EnsureOperational();
        if (_state is not (ClientDirectConnectState.Idle or ClientDirectConnectState.Closed or
            ClientDirectConnectState.Failed)) return false;
        ResetTransient();
        if (!ClientDirectConnectEndpoint.TryParse(endpoint, out _endpoint))
        {
            Fail(ClientDirectConnectFailure.InvalidEndpoint,
                "Direct Connect currently requires an IPv4 address and port, for example 127.0.0.1:7777.");
            return false;
        }
        if (!string.IsNullOrWhiteSpace(explicitFingerprint) &&
            !ClientDeploymentKeyFingerprint.TryParse(
                explicitFingerprint, out _explicitFingerprint))
        {
            Fail(ClientDirectConnectFailure.ExplicitFingerprintMismatch,
                "The supplied fingerprint must contain exactly 64 hexadecimal characters.");
            return false;
        }
        _ = _trust.TryFindByEndpoint(_endpoint.ToString(), out _priorTrust);
        if (!HostedUdpDeploymentDiscoveryClient.TryCreate(
                _endpoint.Value!, _udpLimits, out _discovery,
                UltraSimGameProtocol.Manifest))
        {
            Fail(ClientDirectConnectFailure.DiscoveryUnavailable,
                "The deployment-key discovery socket could not be opened.");
            return false;
        }
        _state = ClientDirectConnectState.Discovering;
        return true;
    }

    public bool TryAcceptCandidate()
    {
        EnsureOperational();
        if (_state != ClientDirectConnectState.AwaitingTrust || _candidateKeyLength == 0)
            return false;
        return BeginPinnedHandshake();
    }

    public bool TryForgetMismatchedTrust()
    {
        EnsureOperational();
        if (_state != ClientDirectConnectState.Failed ||
            _failure != ClientDirectConnectFailure.TrustedKeyMismatch ||
            !_endpoint.IsValid) return false;
        if (!_trust.TryFindByEndpoint(
                _endpoint.ToString(), out ClientTrustedDeployment? retained) || retained is null)
            return false;
        byte[] retainedKey = new byte[retained.PublicKeyBytes];
        if (!retained.TryCopyPublicKey(retainedKey, out int written) || written != retainedKey.Length)
            return false;
        if (!_trust.TryForgetEndpoint(_endpoint.ToString()))
        {
            CryptographicOperations.ZeroMemory(retainedKey);
            return false;
        }
        try
        {
            _trustStore.Save(_trust);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException)
        {
            _ = _trust.TryCommitAuthenticated(retained.Realm, retained.Endpoint, retainedKey);
            _detail = $"The existing deployment pin could not be forgotten: {exception.Message}";
            CryptographicOperations.ZeroMemory(retainedKey);
            return false;
        }
        CryptographicOperations.ZeroMemory(retainedKey);
        return true;
    }

    public void Pump()
    {
        EnsureOperational();
        if (_state == ClientDirectConnectState.Discovering)
        {
            _discovery!.Pump();
            if (_discovery.State == HostedUdpDeploymentDiscoveryState.Failed)
            {
                Fail(ClientDirectConnectFailure.DiscoveryFailed,
                    "No compatible deployment-key announcement was received.");
                return;
            }
            if (_discovery.State == HostedUdpDeploymentDiscoveryState.Discovered)
                AcceptDiscovery();
            return;
        }
        if (_state is not (ClientDirectConnectState.Connecting or
            ClientDirectConnectState.AwaitingRealm or ClientDirectConnectState.Established or
            ClientDirectConnectState.Draining))
            return;
        _transport!.Pump();
        if (_transport.State == HostedUdpClientState.Failed)
        {
            Fail(ClientDirectConnectFailure.HandshakeFailed,
                "The pinned Hosted handshake failed.");
            return;
        }
        if (_transport.State == HostedUdpClientState.Closed)
        {
            if (_state != ClientDirectConnectState.Draining)
            {
                _failure = ClientDirectConnectFailure.TransportClosed;
                _detail = "The authenticated transport closed without a local disconnect request.";
            }
            CloseTransport();
            return;
        }
        if (_transport.State == HostedUdpClientState.Draining &&
            _state != ClientDirectConnectState.Draining)
        {
            _failure = ClientDirectConnectFailure.TransportClosed;
            _detail = "The authenticated transport stopped receiving the remote deployment.";
            _state = ClientDirectConnectState.Draining;
        }
        if (_state == ClientDirectConnectState.Draining)
        {
            return;
        }
        // F0.3a stopped at establishment. F0.3b keeps the accepted transport pumping while
        // higher Game-owned adapters consume application messages from the exposed peer.
        if (_state == ClientDirectConnectState.Established) return;
        if (_transport.State != HostedUdpClientState.Established) return;
        if (_state == ClientDirectConnectState.Connecting)
            _state = ClientDirectConnectState.AwaitingRealm;
        ConsumeApplicationIdentity();
    }

    public void BeginDisconnect()
    {
        EnsureOperational();
        _discovery?.Dispose();
        _discovery = null;
        if (_transport is not null &&
            _transport.State == HostedUdpClientState.Established)
        {
            _transport.BeginDrain();
            _state = ClientDirectConnectState.Draining;
            return;
        }
        CloseTransport();
    }

    public ClientDirectConnectSnapshot GetSnapshot()
    {
        EnsureOperational();
        return new(_state, _failure, _endpoint.ToString(), _candidateFingerprint,
            _priorTrust is not null, _explicitFingerprint.IsValid, _realm,
            _applicationSessionEpoch, _transport?.Connection ?? default,
            _settingsScope, _detail);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _discovery?.Dispose();
        DisposeTransport();
        _discovery = null;
        CryptographicOperations.ZeroMemory(_candidateKey);
        _candidateKeyLength = 0;
        _state = ClientDirectConnectState.Closed;
        _disposed = true;
    }

    private void AcceptDiscovery()
    {
        if (!_discovery!.TryCopyCandidatePublicKey(
                _candidateKey, out _candidateKeyLength))
        {
            Fail(ClientDirectConnectFailure.DiscoveryFailed,
                "The deployment candidate exceeded the admitted key bound.");
            return;
        }
        _candidateFingerprint = ClientDeploymentKeyFingerprint.Compute(
            _candidateKey.AsSpan(0, _candidateKeyLength));
        _discovery.Dispose();
        _discovery = null;
        if (_explicitFingerprint.IsValid && _candidateFingerprint != _explicitFingerprint)
        {
            Fail(ClientDirectConnectFailure.ExplicitFingerprintMismatch,
                "The live deployment key does not match the explicitly supplied fingerprint.");
            return;
        }
        if (_priorTrust is not null)
        {
            if (_candidateFingerprint != _priorTrust.Fingerprint)
            {
                Fail(ClientDirectConnectFailure.TrustedKeyMismatch,
                    "The endpoint now presents a different deployment key. The existing pin was not replaced.");
                return;
            }
            _ = BeginPinnedHandshake();
            return;
        }
        if (_explicitFingerprint.IsValid)
        {
            _ = BeginPinnedHandshake();
            return;
        }
        _state = ClientDirectConnectState.AwaitingTrust;
    }

    private bool BeginPinnedHandshake()
    {
        if (!HostedUdpProtocolClient.TryCreate(
                _endpoint.Value!, _candidateKey.AsSpan(0, _candidateKeyLength),
                _protocolLimits, _udpLimits, out _transport,
                UltraSimGameProtocol.Manifest))
        {
            Fail(ClientDirectConnectFailure.HandshakeUnavailable,
                "The pinned Hosted transport could not be created.");
            return false;
        }
        _state = ClientDirectConnectState.Connecting;
        return true;
    }

    private void ConsumeApplicationIdentity()
    {
        NetworkProtocolPeer peer = _transport!.Peer!;
        if (!peer.TryPeekReceived(
                out NetworkDeliveredMessage delivered,
                out ReadOnlySpan<byte> payload)) return;
        bool valid = false;
        RemoteApplicationSessionIdentityMessage identity = default;
        if (delivered.MessageId == RemoteApplicationSessionIdentityMessageCodec.Descriptor.Id)
        {
            var reader = new NetworkPacketReader(payload);
            valid = RemoteApplicationSessionIdentityMessageCodec.TryRead(ref reader, out identity) &&
                reader.IsComplete && identity.ApplicationSessionEpoch != 0 &&
                (identity.ApplicationRealmHigh != 0 || identity.ApplicationRealmLow != 0);
        }
        _ = peer.TryConsumeReceived();
        if (!valid)
        {
            Fail(ClientDirectConnectFailure.InvalidApplicationIdentity,
                "The authenticated deployment did not provide a valid first application identity.");
            return;
        }
        var realm = new ApplicationRealmId(
            identity.ApplicationRealmHigh, identity.ApplicationRealmLow);
        if (!ClientSettingsScopeKey.TryCreate($"realm-{realm}", out ClientSettingsScopeKey settingsScope))
        {
            Fail(ClientDirectConnectFailure.InvalidApplicationIdentity,
                "The authenticated realm could not form a client settings scope.");
            return;
        }
        _trust.TryFindByRealm(realm, out ClientTrustedDeployment? previousRealm);
        byte[]? previousKey = null;
        if (previousRealm is not null)
        {
            previousKey = new byte[previousRealm.PublicKeyBytes];
            if (!previousRealm.TryCopyPublicKey(previousKey, out int written) ||
                written != previousKey.Length)
            {
                CryptographicOperations.ZeroMemory(previousKey);
                Fail(ClientDirectConnectFailure.TrustConflict,
                    "The prior deployment pin could not be retained transactionally.");
                return;
            }
        }
        ClientServerTrustCommitStatus status = _trust.TryCommitAuthenticated(
            realm, _endpoint.ToString(), _candidateKey.AsSpan(0, _candidateKeyLength));
        if (status is ClientServerTrustCommitStatus.Conflict or
            ClientServerTrustCommitStatus.Invalid)
        {
            if (previousKey is not null) CryptographicOperations.ZeroMemory(previousKey);
            Fail(ClientDirectConnectFailure.TrustConflict,
                "The authenticated realm conflicts with an existing deployment pin.");
            return;
        }
        try
        {
            _trustStore.Save(_trust);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException)
        {
            if (status == ClientServerTrustCommitStatus.Added)
                _ = _trust.TryForgetEndpoint(_endpoint.ToString());
            else if (status == ClientServerTrustCommitStatus.EndpointUpdated &&
                     previousRealm is not null && previousKey is not null)
                _ = _trust.TryCommitAuthenticated(
                    previousRealm.Realm, previousRealm.Endpoint, previousKey);
            if (previousKey is not null) CryptographicOperations.ZeroMemory(previousKey);
            Fail(ClientDirectConnectFailure.TrustPersistenceFailed,
                $"The verified deployment could not be persisted: {exception.Message}");
            return;
        }
        if (previousKey is not null) CryptographicOperations.ZeroMemory(previousKey);
        _settingsScope = settingsScope;
        _realm = realm;
        _applicationSessionEpoch = identity.ApplicationSessionEpoch;
        _failure = ClientDirectConnectFailure.None;
        _detail = null;
        _state = ClientDirectConnectState.Established;
    }

    private void Fail(ClientDirectConnectFailure failure, string detail)
    {
        _discovery?.Dispose();
        DisposeTransport();
        _discovery = null;
        _failure = failure;
        _detail = detail;
        _state = ClientDirectConnectState.Failed;
    }

    private void CloseTransport()
    {
        DisposeTransport();
        _state = ClientDirectConnectState.Closed;
    }

    private void ResetTransient()
    {
        _discovery?.Dispose();
        DisposeTransport();
        _discovery = null;
        _finalTraffic = null;
        CryptographicOperations.ZeroMemory(_candidateKey);
        _candidateKeyLength = 0;
        _endpoint = default;
        _priorTrust = null;
        _candidateFingerprint = default;
        _explicitFingerprint = default;
        _realm = default;
        _settingsScope = default;
        _applicationSessionEpoch = 0;
        _failure = ClientDirectConnectFailure.None;
        _detail = null;
        _state = ClientDirectConnectState.Idle;
    }

    private void DisposeTransport()
    {
        if (_transport is null) return;
        _finalTraffic = _transport.GetTrafficSnapshot();
        _transport.Dispose();
        _transport = null;
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Direct Connect is owner-thread-bound.");
    }
}
