using UltraSim.Discovery;

namespace UltraSim.Server.Discovery;

public sealed class ServerDiscoveryPublisherCoordinator
{
    private readonly IServerDiscoveryPublisher _publisher;
    private readonly ServerDiscoveryAdvertisedSettings _advertisedSettings;
    private readonly Func<ServerStatusSnapshot> _statusProvider;
    private readonly ServerDiscoveryPublisherOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ServerAdvertisedInfo _advertisedInfo;
    private DateTimeOffset? _lastPublishUtc;
    private bool _hasPublished;

    public ServerDiscoveryPublisherCoordinator(
        IServerDiscoveryPublisher publisher,
        ServerDiscoveryAdvertisedSettings advertisedSettings,
        Func<ServerStatusSnapshot> statusProvider,
        ServerDiscoveryPublisherOptions? options = null,
        Func<DateTimeOffset>? clock = null)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _advertisedSettings = advertisedSettings ?? throw new ArgumentNullException(nameof(advertisedSettings));
        _statusProvider = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));
        _options = options ?? new ServerDiscoveryPublisherOptions();
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        _advertisedInfo = ServerDiscoveryContractBuilder.BuildAdvertisedInfo(_advertisedSettings);
    }

    public bool Enabled => _options.Enabled;

    public bool HasPublished => _hasPublished;

    public DateTimeOffset? LastPublishUtc => _lastPublishUtc;

    public ServerAdvertisedInfo AdvertisedInfo => _advertisedInfo;

    public async ValueTask PublishStartupAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        DateTimeOffset nowUtc = _clock();
        ServerDiscoveryStatusInfo statusInfo = BuildStatusInfo(nowUtc);
        await PublishCoreAsync(statusInfo, nowUtc, cancellationToken).ConfigureAwait(false);

        if (_options.SendStartupPresence)
        {
            await _publisher.SendPresenceAsync(
                ServerDiscoveryPresenceSignal.Startup,
                statusInfo,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask PublishHeartbeatIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        DateTimeOffset nowUtc = _clock();
        if (_lastPublishUtc.HasValue &&
            nowUtc - _lastPublishUtc.Value < _options.EffectiveHeartbeatInterval)
        {
            return;
        }

        await PublishCoreAsync(BuildStatusInfo(nowUtc), nowUtc, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PublishShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !_hasPublished)
            return;

        ServerDiscoveryStatusInfo statusInfo = BuildStatusInfo(_clock());
        if (_options.SendShutdownPresence)
        {
            await _publisher.SendPresenceAsync(
                ServerDiscoveryPresenceSignal.Shutdown,
                statusInfo,
                cancellationToken).ConfigureAwait(false);
        }

        if (_options.UnpublishOnStop)
            await _publisher.UnpublishAsync(_advertisedInfo.ServerId, cancellationToken).ConfigureAwait(false);

        _hasPublished = false;
        _lastPublishUtc = null;
    }

    private ServerDiscoveryStatusInfo BuildStatusInfo(DateTimeOffset nowUtc)
        => ServerDiscoveryContractBuilder.BuildStatusInfo(_statusProvider(), heartbeatUtc: nowUtc);

    private async ValueTask PublishCoreAsync(
        ServerDiscoveryStatusInfo statusInfo,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await _publisher.PublishAsync(_advertisedInfo, statusInfo, cancellationToken).ConfigureAwait(false);
        _hasPublished = true;
        _lastPublishUtc = nowUtc;
    }
}
