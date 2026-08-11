using UltraSim.Network;

namespace UltraSim.Server.Network;

public sealed class ServerRuntimeNetworkPublisher : IDisposable
{
    private readonly IServerApplication _application;
    private readonly LatestStateStream _latestState;
    private readonly OrderedEventStream _orderedEvents;
    private bool _disposed;

    public ServerRuntimeNetworkPublisher(
        IServerApplication application,
        LatestStateStream? latestState = null,
        OrderedEventStream? orderedEvents = null,
        int orderedEventCapacity = 1024)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        if (orderedEventCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(orderedEventCapacity));
        _latestState = latestState ?? new LatestStateStream();
        _orderedEvents = orderedEvents ?? new OrderedEventStream(capacity: orderedEventCapacity);
        _application.EventRaised += HandleApplicationEvent;
    }

    public LatestStateStream LatestStateStream => _latestState;
    public OrderedEventStream OrderedEventStream => _orderedEvents;

    public ServerRuntimeNetworkPublishResult PublishCurrentState()
    {
        ThrowIfDisposed();

        _latestState.Publish(
            ServerRuntimeNetworkItemKeys.Status,
            ServerRuntimeNetworkScopes.OperatorStatus,
            ServerRuntimeNetworkPayloadCodec.Encode(_application.GetStatusSnapshot()));

        _latestState.Publish(
            ServerRuntimeNetworkItemKeys.Metrics,
            ServerRuntimeNetworkScopes.OperatorStatus,
            ServerRuntimeNetworkPayloadCodec.Encode(_application.GetMetricsSnapshot()));

        IReadOnlyList<ServerSystemRuntimeSnapshot> systems = _application.GetSystemRuntimeSnapshots();
        for (int i = 0; i < systems.Count; i++)
        {
            ServerSystemRuntimeSnapshot snapshot = systems[i];
            _latestState.Publish(
                ServerRuntimeNetworkItemKeys.GetSystemRuntimeItemKey(snapshot),
                ServerRuntimeNetworkScopes.OperatorSystems,
                ServerRuntimeNetworkPayloadCodec.Encode(snapshot));
        }

        IReadOnlyList<ServerPlayerSessionSnapshot> players = _application.GetPlayerSessionsSnapshot();
        for (int i = 0; i < players.Count; i++)
        {
            ServerPlayerSessionSnapshot snapshot = players[i];
            _latestState.Publish(
                ServerRuntimeNetworkItemKeys.GetPlayerSessionItemKey(snapshot),
                ServerRuntimeNetworkScopes.OperatorPlayers,
                ServerRuntimeNetworkPayloadCodec.Encode(snapshot));
        }

        IReadOnlyList<ServerAccountSummarySnapshot> accounts = _application.GetAccountSummariesSnapshot();
        for (int i = 0; i < accounts.Count; i++)
        {
            ServerAccountSummarySnapshot snapshot = accounts[i];
            _latestState.Publish(
                ServerRuntimeNetworkItemKeys.GetAccountItemKey(snapshot),
                ServerRuntimeNetworkScopes.OperatorAccounts,
                ServerRuntimeNetworkPayloadCodec.Encode(snapshot));
        }

        return new ServerRuntimeNetworkPublishResult(
            StatusCount: 1,
            MetricsCount: 1,
            SystemCount: systems.Count,
            PlayerCount: players.Count,
            AccountCount: accounts.Count,
            EventCount: 0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _application.EventRaised -= HandleApplicationEvent;
        _disposed = true;
    }

    private void HandleApplicationEvent(ServerApplicationEvent evt)
    {
        if (_disposed)
            return;

        _orderedEvents.Publish(
            ServerRuntimeNetworkScopes.OperatorEvents,
            ServerRuntimeNetworkPayloadCodec.Encode(evt));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ServerRuntimeNetworkPublisher));
    }
}
