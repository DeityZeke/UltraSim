using System.Buffers;
using System.Diagnostics;
using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Network;

public sealed class ServerEcsWorldNetworkPublisher
{
    private readonly World _world;
    private readonly LatestStateStream _latestState;
    private readonly OrderedEventStream _orderedEvents;

    public ServerEcsWorldNetworkPublisher(
        World world,
        LatestStateStream? latestState = null,
        OrderedEventStream? orderedEvents = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _latestState = latestState ?? new LatestStateStream();
        _orderedEvents = orderedEvents ?? new OrderedEventStream(capacity: 256);
    }

    public LatestStateStream LatestStateStream => _latestState;
    public OrderedEventStream OrderedEventStream => _orderedEvents;

    public ServerEcsWorldPublishResult PublishRuntimeState()
    {
        _latestState.Publish(
            ServerEcsNetworkItemKeys.World,
            ServerEcsNetworkScopes.OperatorWorld,
            ServerRuntimeNetworkPayloadCodec.Encode(CreateWorldSnapshot()));

        SystemRuntimeSnapshot[] systems = _world.GetSystemRuntimeSnapshots();
        for (int i = 0; i < systems.Length; i++)
        {
            SystemRuntimeSnapshot snapshot = systems[i];
            _latestState.Publish(
                ServerEcsNetworkItemKeys.GetSystemRuntimeItemKey(snapshot),
                ServerEcsNetworkScopes.OperatorSystems,
                ServerRuntimeNetworkPayloadCodec.Encode(snapshot));
        }

        return new ServerEcsWorldPublishResult(
            WorldCount: 1,
            SystemCount: systems.Length,
            DirtyLatestStateCount: 0);
    }

    public int PublishDirtyLatestState<T>(
        Func<Entity, T, SubscriptionKey> scopeSelector,
        Func<Entity, T, ImmutablePayloadBuffer> payloadSelector,
        Func<Entity, T, ulong>? itemKeySelector = null,
        int batchSize = 256)
        where T : unmanaged, IComponent<T>
    {
        return PublishDirtyLatestState(
            new ServerEcsLatestStateRule<T>(scopeSelector, payloadSelector, itemKeySelector),
            batchSize);
    }

    public int PublishDirtyLatestState<T>(ServerEcsLatestStateRule<T> rule, int batchSize = 256)
        where T : unmanaged, IComponent<T>
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        int dirtyCount = _world.GetDirtyCount<T>();
        if (dirtyCount == 0)
            return 0;

        Entity[] rented = ArrayPool<Entity>.Shared.Rent(Math.Max(batchSize, dirtyCount));

        try
        {
            int count = _world.DrainDirtyTo<T>(rented.AsSpan(0, dirtyCount));
            int published = 0;
            for (int i = 0; i < count; i++)
            {
                Entity entity = rented[i];
                if (!_world.TryGetComponent<T>(entity, out T value))
                    continue;

                _latestState.Publish(
                    rule.ItemKeySelector(entity, value),
                    rule.ScopeSelector(entity, value),
                    rule.PacketClass,
                    rule.PayloadSelector(entity, value));
                published++;
            }

            return published;
        }
        finally
        {
            ArrayPool<Entity>.Shared.Return(rented, clearArray: false);
        }
    }

    public int PublishDirtyOrderedEvents<T>(ServerEcsOrderedEventRule<T> rule, int batchSize = 256)
        where T : unmanaged, IComponent<T>
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        int dirtyCount = _world.GetDirtyCount<T>();
        if (dirtyCount == 0)
            return 0;

        Entity[] rented = ArrayPool<Entity>.Shared.Rent(Math.Max(batchSize, dirtyCount));

        try
        {
            int count = _world.DrainDirtyTo<T>(rented.AsSpan(0, dirtyCount));
            int published = 0;
            for (int i = 0; i < count; i++)
            {
                Entity entity = rented[i];
                if (!_world.TryGetComponent<T>(entity, out T value))
                    continue;

                _orderedEvents.Publish(
                    rule.ScopeSelector(entity, value),
                    rule.PacketClass,
                    rule.PayloadSelector(entity, value));
                published++;
            }

            return published;
        }
        finally
        {
            ArrayPool<Entity>.Shared.Return(rented, clearArray: false);
        }
    }

    private ServerEcsWorldSnapshot CreateWorldSnapshot()
    {
        WorldProfileSnapshot profile = _world.LastProfile;
        return new ServerEcsWorldSnapshot(
            WorldTick: _world.WorldTick,
            SystemCount: _world.SystemCount,
            ExecutionMode: _world.SystemExecutionMode.ToString(),
            ProfilingEnabled: _world.ProfilingEnabled,
            ProfileTick: profile.Tick,
            PreSystemsWorkCount: CountStructuralWork(profile.PreSystems),
            PostSystemsWorkCount: CountStructuralWork(profile.PostSystems),
            PreSystemsMilliseconds: ConvertTicksToMilliseconds(profile.PreSystemsElapsedTicks),
            SystemsMilliseconds: ConvertTicksToMilliseconds(profile.SystemsElapsedTicks),
            PostSystemsMilliseconds: ConvertTicksToMilliseconds(profile.PostSystemsElapsedTicks));
    }

    private static int CountStructuralWork(StructuralApplyStats stats)
    {
        return stats.CreateDefaultCount
            + stats.PrefabCount
            + stats.DestroyCount
            + stats.AddCount
            + stats.RemoveCount;
    }

    private static double ConvertTicksToMilliseconds(long elapsedTicks)
    {
        return elapsedTicks <= 0
            ? 0d
            : elapsedTicks * 1000d / Stopwatch.Frequency;
    }
}
