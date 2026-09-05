using System.Buffers.Binary;
using System.Security.Cryptography;
using UltraSim.Game.Application;
using UltraSim.Network;

namespace UltraSim.Game.Protocol;

/// <summary>
/// Minimal pre-admission Hosted application. It establishes only authenticated
/// application-session lineage and realm identity; participant/Game admission is later work.
/// </summary>
public sealed class RemoteApplicationSessionIdentityFactory :
    IHostedUdpSessionApplicationFactory
{
    private readonly ApplicationRealmId _realm;

    public RemoteApplicationSessionIdentityFactory(ApplicationRealmId realm)
    {
        if (!realm.IsValid) throw new ArgumentOutOfRangeException(nameof(realm));
        _realm = realm;
    }

    public IHostedUdpSessionApplication Create(
        ConnectionId connection,
        NetworkProtocolPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        Span<byte> random = stackalloc byte[sizeof(ulong)];
        ulong epoch;
        do
        {
            RandomNumberGenerator.Fill(random);
            epoch = BinaryPrimitives.ReadUInt64LittleEndian(random);
        } while (epoch == 0);
        return new SessionIdentityApplication(peer, _realm, epoch);
    }

    private sealed class SessionIdentityApplication : IHostedUdpSessionApplication
    {
        private readonly NetworkProtocolPeer _peer;
        private readonly byte[] _payload = new byte[
            RemoteApplicationSessionIdentityMessageCodec.EncodedSize];
        private bool _queued;

        internal SessionIdentityApplication(
            NetworkProtocolPeer peer,
            ApplicationRealmId realm,
            ulong epoch)
        {
            _peer = peer;
            var writer = new NetworkPacketWriter(_payload);
            var message = new RemoteApplicationSessionIdentityMessage
            {
                ApplicationSessionEpoch = epoch,
                ApplicationRealmHigh = realm.High,
                ApplicationRealmLow = realm.Low,
            };
            if (!RemoteApplicationSessionIdentityMessageCodec.TryWrite(
                    ref writer, in message) || writer.WrittenCount != _payload.Length)
                throw new InvalidOperationException(
                    "The application-session identity could not be encoded.");
        }

        public bool TryProcess(
            in NetworkDeliveredMessage message,
            ReadOnlySpan<byte> payload) => false;

        public bool TryProgress()
        {
            if (_queued) return true;
            NetworkQueueStatus status = _peer.TryQueue(
                RemoteApplicationSessionIdentityMessageCodec.Descriptor.Id, _payload);
            if (status == NetworkQueueStatus.Accepted)
            {
                _queued = true;
                return true;
            }
            return false;
        }

        public void Dispose() => CryptographicOperations.ZeroMemory(_payload);
    }
}
