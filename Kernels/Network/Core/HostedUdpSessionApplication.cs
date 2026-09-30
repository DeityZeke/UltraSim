namespace UltraSim.Network;


/// <summary>
/// Connection-owner application attached after hosted transport authentication.
/// The implementation and every method invocation belong to one connection-owner
/// thread; Network supplies identity and decoded protocol messages, never Game AOI.
/// </summary>
public interface IHostedUdpSessionApplication : IDisposable
{
    bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload);

    /// <summary>Advances bounded retained application work for the current owner pass.</summary>
    bool TryProgress();
}

/// <summary>
/// Optional caller-owned application lifetime signal. A validated application detach may request
/// retirement of its own hosted transport without waiting for the unrelated idle timeout.
/// </summary>
public interface IHostedUdpSessionApplicationLifetime
{
    bool RetirementRequested { get; }
}

/// <summary>
/// Optional immutable operator correlation supplied by a trusted application factory after
/// transport authentication. This is diagnostic/session identity, never a client claim.
/// </summary>
public interface IHostedUdpOperatorSessionIdentity
{
    ulong OperatorParticipantId { get; }
}

/// <summary>
/// Cold factory invoked on the selected connection-owner thread after the secure
/// peer exists and before terminal handshake success is published.
/// </summary>
public interface IHostedUdpSessionApplicationFactory
{
    IHostedUdpSessionApplication Create(
        ConnectionId connection,
        NetworkProtocolPeer peer);
}
