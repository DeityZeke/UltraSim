using System;
using System.Diagnostics;
using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Client.Engines.Networking
{
    public sealed class ClientNetworkBridge<TCorrection>
    {
        private readonly World _world;
        private readonly NetworkSessionManager _sessions;
        private readonly NetworkInboundCommandPipeline<ClientInboundCorrection<TCorrection>> _inboundPipeline;
        private readonly ClientReconciliationQueue<TCorrection> _reconciliationQueue;
        private readonly ClientCorrectionApplier<TCorrection> _correctionApplier;
        private readonly ClientCorrectionStats? _correctionStats;
        private readonly ClientAuthoritativeAckObserver? _ackObserver;
        private readonly ClientCorrectionMergeKeySelector<TCorrection>? _mergeKeySelector;
        private readonly int _coalesceQueueDepthThreshold;
        private readonly int _maxCorrectionsPerTick;
        private readonly InboundCommandHandler<ClientInboundCorrection<TCorrection>> _queueInboundHandler;
        private readonly ReconciliationHandler<TCorrection> _applyCorrectionHandler;
        private ClientNetworkTickResult _lastTickResult;
        private long _pollElapsedTicksTotal;
        private long _queueElapsedTicksTotal;
        private long _applyElapsedTicksTotal;
        private long _applyHandlerElapsedTicksTotal;
        private long _applyStatsElapsedTicksTotal;
        private long _worldElapsedTicksTotal;
        private long _flushElapsedTicksTotal;
        private long _maxPollElapsedTicks;
        private long _maxQueueElapsedTicks;
        private long _maxApplyElapsedTicks;
        private long _maxApplyHandlerElapsedTicks;
        private long _maxApplyStatsElapsedTicks;
        private long _maxWorldElapsedTicks;
        private long _maxFlushElapsedTicks;
        private int _tickCount;
        private int _queuedIntoReconciliationThisTick;
        private int _coalescedInQueueThisTick;
        private long _applyHandlerTicksThisTick;
        private long _applyStatsTicksThisTick;

        public ClientNetworkBridge(
            World world,
            NetworkSessionManager sessions,
            NetworkInboundCommandPipeline<ClientInboundCorrection<TCorrection>> inboundPipeline,
            ClientCorrectionApplier<TCorrection> correctionApplier,
            ClientReconciliationQueue<TCorrection>? reconciliationQueue = null,
            ClientCorrectionStats? correctionStats = null,
            ClientAuthoritativeAckObserver? ackObserver = null,
            ClientCorrectionMergeKeySelector<TCorrection>? mergeKeySelector = null,
            int coalesceQueueDepthThreshold = 0,
            int maxCorrectionsPerTick = int.MaxValue)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(sessions);
            ArgumentNullException.ThrowIfNull(inboundPipeline);
            ArgumentNullException.ThrowIfNull(correctionApplier);

            _world = world;
            _sessions = sessions;
            _inboundPipeline = inboundPipeline;
            _correctionApplier = correctionApplier;
            _reconciliationQueue = reconciliationQueue ?? new ClientReconciliationQueue<TCorrection>(threadSafe: true);
            _correctionStats = correctionStats;
            _ackObserver = ackObserver;
            _mergeKeySelector = mergeKeySelector;
            _coalesceQueueDepthThreshold = Math.Max(0, coalesceQueueDepthThreshold);
            _maxCorrectionsPerTick = maxCorrectionsPerTick <= 0 ? int.MaxValue : maxCorrectionsPerTick;
            _queueInboundHandler = QueueInboundCorrection;
            _applyCorrectionHandler = ApplyCorrection;
        }

        public World World => _world;

        public NetworkSessionManager Sessions => _sessions;

        public NetworkInboundCommandPipeline<ClientInboundCorrection<TCorrection>> InboundPipeline => _inboundPipeline;

        public ClientReconciliationQueue<TCorrection> ReconciliationQueue => _reconciliationQueue;

        public ClientCorrectionStats? CorrectionStats => _correctionStats;

        public ClientAuthoritativeAckObserver? AckObserver => _ackObserver;

        public ClientNetworkTickResult LastTickResult => _lastTickResult;

        public ClientNetworkBridgeStatsSnapshot SnapshotStats() => new ClientNetworkBridgeStatsSnapshot(
            _lastTickResult,
            _correctionStats?.Snapshot() ?? default,
            _inboundPipeline.SnapshotStats(),
            _pollElapsedTicksTotal,
            _queueElapsedTicksTotal,
            _applyElapsedTicksTotal,
            _applyHandlerElapsedTicksTotal,
            _applyStatsElapsedTicksTotal,
            _worldElapsedTicksTotal,
            _flushElapsedTicksTotal,
            _maxPollElapsedTicks,
            _maxQueueElapsedTicks,
            _maxApplyElapsedTicks,
            _maxApplyHandlerElapsedTicks,
            _maxApplyStatsElapsedTicks,
            _maxWorldElapsedTicks,
            _maxFlushElapsedTicks,
            _tickCount);

        public long CreateSession(int connectionId, long nowTicks) =>
            _sessions.CreateSession(connectionId, nowTicks);

        public bool DisconnectSession(
            long sessionId,
            SessionDisconnectReason reason,
            long nowTicks,
            bool removeDisconnected = true,
            bool clearQueuedCorrections = true)
        {
            bool disconnected = _sessions.TryDisconnectSession(
                sessionId,
                reason,
                nowTicks,
                removeDisconnected: removeDisconnected);

            if (disconnected && clearQueuedCorrections)
                _reconciliationQueue.Clear();

            return disconnected;
        }

        public bool DisconnectConnection(
            int connectionId,
            SessionDisconnectReason reason,
            long nowTicks,
            bool removeDisconnected = true,
            bool clearQueuedCorrections = true)
        {
            bool disconnected = _sessions.TryDisconnectConnection(
                connectionId,
                reason,
                nowTicks,
                removeDisconnected: removeDisconnected);

            if (disconnected && clearQueuedCorrections)
                _reconciliationQueue.Clear();

            return disconnected;
        }

        public int ShutdownSessions(long nowTicks, bool clearQueuedCorrections = true)
        {
            int disconnected = _sessions.DisconnectAll(
                SessionDisconnectReason.ServerShutdown,
                nowTicks,
                removeDisconnected: true);

            if (clearQueuedCorrections)
                _reconciliationQueue.Clear();

            return disconnected;
        }

        public int PollDecodeInbound() => _inboundPipeline.PollAndDecodeInbound();

        public int QueueCorrectionsPreBarrier()
        {
            _queuedIntoReconciliationThisTick = 0;
            _coalescedInQueueThisTick = 0;
            _inboundPipeline.ApplyPreBarrier(_queueInboundHandler);
            return _queuedIntoReconciliationThisTick;
        }

        public int ApplyCorrectionsPreTick() => _reconciliationQueue.Drain(_applyCorrectionHandler, _maxCorrectionsPerTick);

        public void FlushOutbound() => _inboundPipeline.FlushOutbound();

        public ClientNetworkTickResult Tick(float deltaSeconds)
        {
            long stageStart = Stopwatch.GetTimestamp();
            int decoded = PollDecodeInbound();
            long pollElapsed = Stopwatch.GetTimestamp() - stageStart;

            stageStart = Stopwatch.GetTimestamp();
            int queued = QueueCorrectionsPreBarrier();
            long queueElapsed = Stopwatch.GetTimestamp() - stageStart;
            int queueDepthAfterQueue = _reconciliationQueue.Count;
            _correctionStats?.RecordQueuedCorrections(queued, _coalescedInQueueThisTick, queueDepthAfterQueue);

            stageStart = Stopwatch.GetTimestamp();
            _applyHandlerTicksThisTick = 0;
            _applyStatsTicksThisTick = 0;
            int applied = ApplyCorrectionsPreTick();
            long applyElapsed = Stopwatch.GetTimestamp() - stageStart;
            int queueDepthAfterApply = _reconciliationQueue.Count;

            uint tickBefore = _world.WorldTick;

            stageStart = Stopwatch.GetTimestamp();
            _world.Tick(deltaSeconds);
            long worldElapsed = Stopwatch.GetTimestamp() - stageStart;

            stageStart = Stopwatch.GetTimestamp();
            FlushOutbound();
            long flushElapsed = Stopwatch.GetTimestamp() - stageStart;
            _lastTickResult = new ClientNetworkTickResult(
                tickBefore,
                _world.WorldTick,
                decoded,
                queued,
                _coalescedInQueueThisTick,
                applied,
                queueDepthAfterQueue,
                queueDepthAfterApply,
                pollElapsed,
                queueElapsed,
                applyElapsed,
                worldElapsed,
                flushElapsed);
            _pollElapsedTicksTotal += pollElapsed;
            _queueElapsedTicksTotal += queueElapsed;
            _applyElapsedTicksTotal += applyElapsed;
            _applyHandlerElapsedTicksTotal += _applyHandlerTicksThisTick;
            _applyStatsElapsedTicksTotal += _applyStatsTicksThisTick;
            _worldElapsedTicksTotal += worldElapsed;
            _flushElapsedTicksTotal += flushElapsed;
            if (pollElapsed > _maxPollElapsedTicks)
                _maxPollElapsedTicks = pollElapsed;
            if (queueElapsed > _maxQueueElapsedTicks)
                _maxQueueElapsedTicks = queueElapsed;
            if (applyElapsed > _maxApplyElapsedTicks)
                _maxApplyElapsedTicks = applyElapsed;
            if (_applyHandlerTicksThisTick > _maxApplyHandlerElapsedTicks)
                _maxApplyHandlerElapsedTicks = _applyHandlerTicksThisTick;
            if (_applyStatsTicksThisTick > _maxApplyStatsElapsedTicks)
                _maxApplyStatsElapsedTicks = _applyStatsTicksThisTick;
            if (worldElapsed > _maxWorldElapsedTicks)
                _maxWorldElapsedTicks = worldElapsed;
            if (flushElapsed > _maxFlushElapsedTicks)
                _maxFlushElapsedTicks = flushElapsed;
            _tickCount++;
            return _lastTickResult;
        }

        private void QueueInboundCorrection(in QueuedInboundCommand<ClientInboundCorrection<TCorrection>> command)
        {
            ref readonly ClientInboundCorrection<TCorrection> inbound = ref command.Command;
            _ackObserver?.Invoke(inbound.AuthoritativeSequence);
            if (_mergeKeySelector != null &&
                (_coalesceQueueDepthThreshold <= 0 || _reconciliationQueue.Count >= _coalesceQueueDepthThreshold) &&
                _mergeKeySelector(in inbound.Correction, out ulong mergeKey))
            {
                bool replaced = _reconciliationQueue.EnqueueOrReplace(
                    mergeKey,
                    authoritativeSequence: inbound.AuthoritativeSequence,
                    serverTick: inbound.ServerTick,
                    queuedClientTick: _world.WorldTick,
                    in inbound.Correction);
                if (replaced)
                    _coalescedInQueueThisTick++;
                else
                    _queuedIntoReconciliationThisTick++;
                return;
            }

            _reconciliationQueue.Enqueue(
                authoritativeSequence: inbound.AuthoritativeSequence,
                serverTick: inbound.ServerTick,
                queuedClientTick: _world.WorldTick,
                in inbound.Correction);
            _queuedIntoReconciliationThisTick++;
        }

        private void ApplyCorrection(in ReconciliationEntry<TCorrection> correction)
        {
            long stageStart = Stopwatch.GetTimestamp();
            _correctionApplier(_world, in correction);
            _applyHandlerTicksThisTick += Stopwatch.GetTimestamp() - stageStart;
            stageStart = Stopwatch.GetTimestamp();
            _correctionStats?.RecordApplyLatency(correction.QueuedClientTick, _world.WorldTick);
            _applyStatsTicksThisTick += Stopwatch.GetTimestamp() - stageStart;
        }
    }
}
