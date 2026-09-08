using System.Security.Cryptography;

namespace UltraSim.Network;

public readonly record struct ProtocolLoopbackSnapshot(
    ulong Step,
    ProtocolHandshakeSnapshot Handshake,
    NetworkProtocolSnapshot Client,
    NetworkProtocolSnapshot Server,
    DeterministicDatagramSnapshot Datagram);

/// <summary>
/// Complete single-owner N0.3 protocol topology over deterministic datagrams.
/// </summary>
/// <remarks>
/// The secure channel authenticates the pinned server endpoint. Game/account identity
/// authentication remains an application Control-lane concern. This topology deliberately
/// does not claim cross-thread packet-memory ownership; real transport handoff is N0.5.
/// </remarks>
public sealed class ProtocolLoopback : IDisposable
{
    private readonly int _ownerThreadId;
    private readonly DeterministicDatagramLink _link;
    private readonly NetworkConnectionTable _connections;
    private readonly bool _ownsIdentity;
    private NetworkServerIdentity? _identity;
    private bool _disposed;
    private ulong _step;

    private ProtocolLoopback(
        DeterministicDatagramLink link,
        NetworkProtocolManifest clientManifest,
        NetworkProtocolManifest serverManifest,
        NetworkProtocolLimits limits,
        NetworkServerIdentity identity,
        bool ownsIdentity,
        ProtocolHandshakeResult handshake)
    {
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _link = link;
        _connections = handshake.Connections;
        _identity = identity;
        _ownsIdentity = ownsIdentity;
        _step = link.GetSnapshot().Step;
        Handshake = handshake.Snapshot;
        Connection = handshake.Connection;
        Client = new NetworkProtocolPeer(
            link.A, clientManifest, limits, link.Limits, handshake.Connection, handshake.SecurityEpoch,
            handshake.MasterSecret, sendsClientToServer: true);
        Server = new NetworkProtocolPeer(
            link.B, serverManifest, limits, link.Limits, handshake.Connection, handshake.SecurityEpoch,
            handshake.MasterSecret, sendsClientToServer: false);
        CryptographicOperations.ZeroMemory(handshake.MasterSecret);
    }

    public ConnectionId Connection { get; }
    public ProtocolHandshakeSnapshot Handshake { get; }
    public NetworkProtocolPeer Client { get; }
    public NetworkProtocolPeer Server { get; }

    public static bool TryCreate(
        NetworkProtocolLimits limits,
        DeterministicDatagramLimits datagramLimits,
        NetworkImpairmentOptions impairment,
        out ProtocolLoopback? loopback,
        out ProtocolHandshakeSnapshot handshake,
        NetworkProtocolManifest? manifest = null,
        NetworkServerIdentity? serverIdentity = null,
        ReadOnlyMemory<byte> trustedServerPublicKey = default,
        int maximumHandshakeSteps = 4_096,
        NetworkProtocolManifest? serverManifest = null)
    {
        limits.Validate(datagramLimits);
        manifest ??= CoreNetworkProtocol.Manifest;
        serverManifest ??= manifest;
        bool ownsIdentity = serverIdentity is null;
        serverIdentity ??= NetworkServerIdentity.CreateEphemeral();
        if (trustedServerPublicKey.IsEmpty) trustedServerPublicKey = serverIdentity.PublicKey.ToArray();
        var link = new DeterministicDatagramLink(datagramLimits, impairment);
        if (!ProtocolLoopbackHandshakeHarness.TryEstablish(
                link, manifest, serverManifest, serverIdentity, trustedServerPublicKey.Span,
                maximumHandshakeSteps, out ProtocolHandshakeResult result))
        {
            handshake = default;
            loopback = null;
            if (ownsIdentity) serverIdentity.Dispose();
            return false;
        }

        DrainHandshakeResidue(link, impairment.MaximumDelaySteps);
        loopback = new ProtocolLoopback(link, manifest, serverManifest, limits, serverIdentity, ownsIdentity, result);
        handshake = result.Snapshot;
        return true;
    }

    public void Pump()
    {
        EnsureOperational();
        Client.ProcessInbound(_step);
        Server.ProcessInbound(_step);
        Client.Emit(_step);
        Server.Emit(_step);
        _link.Advance();
        _step++;
    }

    /// <summary>Rotates both send directions only at a drained reliable boundary.</summary>
    public bool TryRotateSecurityEpoch()
    {
        EnsureOperational();
        if (!Client.CanRotateSendSecurityEpoch || !Server.CanRotateSendSecurityEpoch) return false;
        if (!_connections.TryRotateSecurityEpoch(Connection, out SecurityEpoch epoch)) return false;
        if (!Client.TryRotateSendSecurityEpoch() || !Server.TryRotateSendSecurityEpoch() ||
            Client.GetSnapshot().SendSecurityEpoch != epoch || Server.GetSnapshot().SendSecurityEpoch != epoch)
            throw new InvalidOperationException("The connection and secure peers did not rotate atomically.");
        return true;
    }

    public ProtocolLoopbackSnapshot GetSnapshot()
    {
        EnsureOperational();
        return new(_step, Handshake, Client.GetSnapshot(), Server.GetSnapshot(), _link.GetSnapshot());
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        Client.Dispose();
        Server.Dispose();
        if (_connections.TryGetState(Connection, out NetworkConnectionState state) &&
            state != NetworkConnectionState.Closed)
            _ = _connections.TryAdvance(Connection, NetworkConnectionState.Closed);
        _ = _connections.TryReleaseClosed(Connection);
        if (_ownsIdentity) _identity!.Dispose();
        _identity = null;
        _disposed = true;
    }

    private static void DrainHandshakeResidue(DeterministicDatagramLink link, ushort maximumDelay)
    {
        byte[] scratch = new byte[link.Limits.MaximumDatagramBytes];
        for (int step = 0; step <= maximumDelay + 1; step++)
        {
            while (link.A.TryReceive(scratch, out _)) { }
            while (link.B.TryReceive(scratch, out _)) { }
            link.Advance();
        }
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        if (_disposed) throw new ObjectDisposedException(nameof(ProtocolLoopback));
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("ProtocolLoopback is bound to its creating owner thread.");
    }
}
