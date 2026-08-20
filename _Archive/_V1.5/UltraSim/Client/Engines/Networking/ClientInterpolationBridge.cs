using System;
using System.Collections.Generic;
using System.Diagnostics;
using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Client.Engines.Networking
{
    public sealed class ClientInterpolationBridge<TState>
    {
        private readonly World _world;
        private readonly ClientNetworkEntityMap _entityMap;
        private readonly Dictionary<ulong, ClientInterpolationBuffer<TState>> _buffers =
            new Dictionary<ulong, ClientInterpolationBuffer<TState>>();
        private readonly int _bufferInitialCapacity;
        private readonly int _maxSamplesPerEntity;
        private int _totalBufferedSamples;
        private int _maxBufferedSamples;
        private int _maxTrackedEntities;
        private int _lastAppliedSamples;
        private int _lastPrunedSamples;
        private long _appliedSamplesTotal;
        private long _prunedSamplesTotal;
        private int _lastTrimmedSamples;
        private long _trimmedSamplesTotal;
        private int _lastApplyAttempts;
        private int _lastApplyMisses;
        private long _applyAttemptsTotal;
        private long _applyMissesTotal;
        private int _lastStaleBufferRemovals;
        private long _staleBufferRemovalsTotal;
        private long _lastApplyElapsedTicks;
        private long _maxApplyElapsedTicks;
        private long _applyElapsedTicksTotal;
        private int _applyPassCount;
        private readonly List<ulong> _staleBufferKeys = new List<ulong>();

        public ClientInterpolationBridge(
            World world,
            ClientNetworkEntityMap entityMap,
            int bufferInitialCapacity = 32,
            int maxSamplesPerEntity = 8)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(entityMap);
            if (bufferInitialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(bufferInitialCapacity));
            if (maxSamplesPerEntity <= 1)
                throw new ArgumentOutOfRangeException(nameof(maxSamplesPerEntity));

            _world = world;
            _entityMap = entityMap;
            _bufferInitialCapacity = bufferInitialCapacity;
            _maxSamplesPerEntity = maxSamplesPerEntity;
        }

        public World World => _world;

        public ClientNetworkEntityMap EntityMap => _entityMap;

        public int TrackedEntityCount => _buffers.Count;

        public int TotalBufferedSamples => _totalBufferedSamples;

        public int MaxBufferedSamples => _maxBufferedSamples;

        public int MaxTrackedEntityCount => _maxTrackedEntities;

        public int LastAppliedSamples => _lastAppliedSamples;

        public int LastPrunedSamples => _lastPrunedSamples;

        public long AppliedSamplesTotal => _appliedSamplesTotal;

        public long PrunedSamplesTotal => _prunedSamplesTotal;

        public int LastTrimmedSamples => _lastTrimmedSamples;

        public long TrimmedSamplesTotal => _trimmedSamplesTotal;

        public int LastApplyAttempts => _lastApplyAttempts;

        public int LastApplyMisses => _lastApplyMisses;

        public long ApplyAttemptsTotal => _applyAttemptsTotal;

        public long ApplyMissesTotal => _applyMissesTotal;

        public int LastStaleBufferRemovals => _lastStaleBufferRemovals;

        public long StaleBufferRemovalsTotal => _staleBufferRemovalsTotal;

        public long LastApplyElapsedTicks => _lastApplyElapsedTicks;

        public long MaxApplyElapsedTicks => _maxApplyElapsedTicks;

        public long ApplyElapsedTicksTotal => _applyElapsedTicksTotal;

        public int ApplyPassCount => _applyPassCount;

        public ClientInterpolationStatsSnapshot SnapshotStats() => new ClientInterpolationStatsSnapshot(
            _buffers.Count,
            _totalBufferedSamples,
            _maxBufferedSamples,
            _maxTrackedEntities,
            _lastAppliedSamples,
            _lastPrunedSamples,
            _appliedSamplesTotal,
            _prunedSamplesTotal,
            _lastTrimmedSamples,
            _trimmedSamplesTotal,
            _lastApplyAttempts,
            _lastApplyMisses,
            _applyAttemptsTotal,
            _applyMissesTotal,
            _lastStaleBufferRemovals,
            _staleBufferRemovalsTotal,
            _lastApplyElapsedTicks,
            _maxApplyElapsedTicks,
            _applyElapsedTicksTotal,
            _applyPassCount);

        public void AddSample(NetworkEntityId networkEntityId, long serverTick, in TState state)
        {
            ClientInterpolationBuffer<TState> buffer = GetOrCreateBuffer(networkEntityId.Value);
            int before = buffer.Count;
            buffer.AddSample(serverTick, in state);
            int delta = buffer.Count - before;
            if (delta > 0)
            {
                _totalBufferedSamples += delta;
                if (_totalBufferedSamples > _maxBufferedSamples)
                    _maxBufferedSamples = _totalBufferedSamples;
            }

            int trimmed = buffer.TrimToMaxCount(_maxSamplesPerEntity);
            _lastTrimmedSamples = trimmed;
            _trimmedSamplesTotal += trimmed;
            _totalBufferedSamples -= trimmed;
            if (_totalBufferedSamples < 0)
                _totalBufferedSamples = 0;
        }

        public int PruneBefore(long minServerTick)
        {
            int pruned = 0;
            foreach (ClientInterpolationBuffer<TState> buffer in _buffers.Values)
                pruned += buffer.PruneBefore(minServerTick);

            _lastPrunedSamples = pruned;
            _prunedSamplesTotal += pruned;
            _totalBufferedSamples -= pruned;
            if (_totalBufferedSamples < 0)
                _totalBufferedSamples = 0;

            return pruned;
        }

        public bool TrySample(
            NetworkEntityId networkEntityId,
            long targetServerTick,
            InterpolationLerp<TState> lerp,
            out TState state)
        {
            if (!_buffers.TryGetValue(networkEntityId.Value, out ClientInterpolationBuffer<TState>? buffer))
            {
                state = default!;
                return false;
            }

            return buffer.TrySample(targetServerTick, lerp, out state);
        }

        public bool TryGetBufferTickBounds(
            NetworkEntityId networkEntityId,
            out long minServerTick,
            out long maxServerTick,
            out int sampleCount)
        {
            if (!_buffers.TryGetValue(networkEntityId.Value, out ClientInterpolationBuffer<TState>? buffer))
            {
                minServerTick = 0;
                maxServerTick = 0;
                sampleCount = 0;
                return false;
            }

            sampleCount = buffer.Count;
            return buffer.TryGetTickBounds(out minServerTick, out maxServerTick);
        }

        public int ApplySamples(
            long targetServerTick,
            InterpolationLerp<TState> lerp,
            ClientInterpolatedStateApplier<TState> applier)
        {
            ArgumentNullException.ThrowIfNull(lerp);
            ArgumentNullException.ThrowIfNull(applier);

            long started = Stopwatch.GetTimestamp();
            int applied = 0;
            int attempts = 0;
            int misses = 0;
            _staleBufferKeys.Clear();
            foreach ((ulong rawNetworkEntityId, ClientInterpolationBuffer<TState> buffer) in _buffers)
            {
                attempts++;
                if (!buffer.TrySample(targetServerTick, lerp, out TState state))
                {
                    misses++;
                    continue;
                }

                var networkEntityId = new NetworkEntityId(rawNetworkEntityId);
                if (!_entityMap.TryGetEntity(networkEntityId, out Entity entity))
                {
                    misses++;
                    _staleBufferKeys.Add(rawNetworkEntityId);
                    continue;
                }
                if (!_world.IsAlive(entity))
                {
                    misses++;
                    _staleBufferKeys.Add(rawNetworkEntityId);
                    continue;
                }

                applier(_world, entity, in state);
                applied++;
            }

            int staleRemovals = 0;
            for (int i = 0; i < _staleBufferKeys.Count; i++)
            {
                if (RemoveBufferByKey(_staleBufferKeys[i]))
                    staleRemovals++;
            }

            _lastAppliedSamples = applied;
            _appliedSamplesTotal += applied;
            _lastApplyAttempts = attempts;
            _lastApplyMisses = misses;
            _applyAttemptsTotal += attempts;
            _applyMissesTotal += misses;
            _lastStaleBufferRemovals = staleRemovals;
            _staleBufferRemovalsTotal += staleRemovals;
            long elapsedTicks = Stopwatch.GetTimestamp() - started;
            _lastApplyElapsedTicks = elapsedTicks;
            if (elapsedTicks > _maxApplyElapsedTicks)
                _maxApplyElapsedTicks = elapsedTicks;
            _applyElapsedTicksTotal += elapsedTicks;
            _applyPassCount++;
            return applied;
        }

        public bool Remove(NetworkEntityId networkEntityId)
        {
            if (!_buffers.Remove(networkEntityId.Value, out ClientInterpolationBuffer<TState>? buffer))
                return false;

            _totalBufferedSamples -= buffer.Count;
            if (_totalBufferedSamples < 0)
                _totalBufferedSamples = 0;
            return true;
        }

        public void Clear()
        {
            _buffers.Clear();
            _totalBufferedSamples = 0;
            _lastAppliedSamples = 0;
            _lastPrunedSamples = 0;
            _lastTrimmedSamples = 0;
            _lastApplyAttempts = 0;
            _lastApplyMisses = 0;
            _lastStaleBufferRemovals = 0;
            _lastApplyElapsedTicks = 0;
            _staleBufferKeys.Clear();
        }

        private ClientInterpolationBuffer<TState> GetOrCreateBuffer(ulong rawNetworkEntityId)
        {
            if (_buffers.TryGetValue(rawNetworkEntityId, out ClientInterpolationBuffer<TState>? existing))
                return existing!;

            var created = new ClientInterpolationBuffer<TState>(_bufferInitialCapacity);
            _buffers.Add(rawNetworkEntityId, created);
            if (_buffers.Count > _maxTrackedEntities)
                _maxTrackedEntities = _buffers.Count;
            return created;
        }

        private bool RemoveBufferByKey(ulong rawNetworkEntityId)
        {
            if (!_buffers.Remove(rawNetworkEntityId, out ClientInterpolationBuffer<TState>? buffer))
                return false;

            _totalBufferedSamples -= buffer.Count;
            if (_totalBufferedSamples < 0)
                _totalBufferedSamples = 0;
            return true;
        }
    }
}
