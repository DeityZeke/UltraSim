using System.Threading;
using System.Threading.Tasks;

namespace UltraSim.Discovery
{
    public interface IServerDiscoveryPublisher
    {
        ValueTask PublishAsync(
            ServerAdvertisedInfo advertisedInfo,
            ServerDiscoveryStatusInfo statusInfo,
            CancellationToken cancellationToken = default);

        ValueTask UnpublishAsync(
            string serverId,
            CancellationToken cancellationToken = default);

        ValueTask SendPresenceAsync(
            ServerDiscoveryPresenceSignal signal,
            ServerDiscoveryStatusInfo? statusInfo = null,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
