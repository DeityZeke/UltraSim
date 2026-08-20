using System;
using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Engines.Networking
{
    public sealed class ServerNetworkBridge<TCommand>
    {
        private readonly World _world;
        private readonly NetworkSessionManager _sessions;
        private readonly NetworkInboundCommandPipeline<TCommand> _inboundPipeline;
        private readonly ServerSessionEntityBindings _sessionBindings;
        private readonly ServerInboundCommandApplier<TCommand> _commandApplier;
        private readonly InboundCommandHandler<TCommand> _inboundHandler;

        public ServerNetworkBridge(
            World world,
            NetworkSessionManager sessions,
            NetworkInboundCommandPipeline<TCommand> inboundPipeline,
            ServerInboundCommandApplier<TCommand> commandApplier,
            ServerSessionEntityBindings? sessionBindings = null)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(sessions);
            ArgumentNullException.ThrowIfNull(inboundPipeline);
            ArgumentNullException.ThrowIfNull(commandApplier);

            _world = world;
            _sessions = sessions;
            _inboundPipeline = inboundPipeline;
            _sessionBindings = sessionBindings ?? new ServerSessionEntityBindings();
            _commandApplier = commandApplier;
            _inboundHandler = HandleInboundCommand;
        }

        public World World => _world;

        public NetworkSessionManager Sessions => _sessions;

        public NetworkInboundCommandPipeline<TCommand> InboundPipeline => _inboundPipeline;

        public ServerSessionEntityBindings SessionBindings => _sessionBindings;

        public long CreateSession(int connectionId, long nowTicks) =>
            _sessions.CreateSession(connectionId, nowTicks);

        public void BindSessionEntity(long sessionId, Entity entity) =>
            _sessionBindings.BindSessionEntity(sessionId, entity);

        public bool UnbindSessionEntity(long sessionId) =>
            _sessionBindings.UnbindSessionEntity(sessionId);

        public bool TryGetEntityForSession(long sessionId, out Entity entity) =>
            _sessionBindings.TryGetEntityForSession(sessionId, out entity);

        public bool TryGetEntityForConnection(int connectionId, out Entity entity) =>
            _sessionBindings.TryGetEntityForConnection(_sessions, connectionId, out entity);

        public bool TryResolveControlledEntity(in QueuedInboundCommand<TCommand> command, out Entity entity) =>
            _sessionBindings.TryGetEntityForSession(command.SessionId, out entity);

        public bool DisconnectSession(
            long sessionId,
            SessionDisconnectReason reason,
            long nowTicks,
            bool removeDisconnected = true,
            bool unbindSessionEntity = true)
        {
            bool disconnected = _sessions.TryDisconnectSession(
                sessionId,
                reason,
                nowTicks,
                removeDisconnected: removeDisconnected);

            if (disconnected && unbindSessionEntity && removeDisconnected)
                _sessionBindings.UnbindSessionEntity(sessionId);

            return disconnected;
        }

        public bool DisconnectConnection(
            int connectionId,
            SessionDisconnectReason reason,
            long nowTicks,
            bool removeDisconnected = true,
            bool unbindSessionEntity = true)
        {
            if (!_sessions.TryGetSessionIdByConnection(connectionId, out long sessionId))
                return false;

            return DisconnectSession(
                sessionId,
                reason,
                nowTicks,
                removeDisconnected: removeDisconnected,
                unbindSessionEntity: unbindSessionEntity);
        }

        public int ShutdownSessions(long nowTicks, bool clearBindings = true)
        {
            int disconnected = _sessions.DisconnectAll(
                SessionDisconnectReason.ServerShutdown,
                nowTicks,
                removeDisconnected: true);

            if (clearBindings)
                _sessionBindings.Clear();

            return disconnected;
        }

        public int PollDecodeInbound() => _inboundPipeline.PollAndDecodeInbound();

        public int ApplyPreBarrier() => _inboundPipeline.ApplyPreBarrier(_inboundHandler);

        public void FlushOutbound() => _inboundPipeline.FlushOutbound();

        public ServerNetworkTickResult Tick(float deltaSeconds)
        {
            int decoded = PollDecodeInbound();
            int applied = ApplyPreBarrier();

            uint tickBefore = _world.WorldTick;
            _world.Tick(deltaSeconds);

            FlushOutbound();
            return new ServerNetworkTickResult(tickBefore, _world.WorldTick, decoded, applied);
        }

        public ServerNetworkTickResult TickWithReplication(
            float deltaSeconds,
            ServerNetworkReplicationBridge replicationBridge)
        {
            ArgumentNullException.ThrowIfNull(replicationBridge);

            int decoded = PollDecodeInbound();
            int applied = ApplyPreBarrier();

            uint tickBefore = _world.WorldTick;
            _world.Tick(deltaSeconds);

            long replicationTick = _world.WorldTick;
            ServerReplicationTickResult replication = replicationBridge.ReplicateTick(
                replicationTick,
                flushOutbound: false);

            FlushOutbound();
            return new ServerNetworkTickResult(
                tickBefore,
                _world.WorldTick,
                decoded,
                applied,
                replication.PublishedCandidates,
                replication.SentPackets);
        }

        private void HandleInboundCommand(in QueuedInboundCommand<TCommand> command) =>
            _commandApplier(_world, in command);
    }
}
