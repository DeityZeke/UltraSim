using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace UltraSim.Network
{
    public sealed class NetworkInboundCommandPipeline<TCommand>
    {
        private const int SessionCacheSize = 64;

        private struct RateLimitState
        {
            public long WindowStartTick;
            public long LastStrikeTick;
            public long LastStrikeWindowStartTick;
            public int WindowCount;
            public int StrikeCount;
            public bool DisconnectSuggested;
        }

        private readonly INetworkTransport _transport;
        private readonly ConnectionSessionResolver _sessionResolver;
        private readonly InboundCommandDecoder<TCommand> _decoder;
        private readonly InboundDatagram[] _pollBuffer;
        private readonly NetworkInboundCommandQueue<TCommand> _queue;
        private readonly bool _threadSafeStats;
        private readonly bool _hasRateLimitPolicy;
        private readonly InboundRateLimitPolicy _rateLimitPolicy;
        private readonly InboundRateLimitStrikeHandler? _rateLimitStrikeHandler;
        private readonly Dictionary<long, RateLimitState>? _rateLimitStates;
        private readonly Func<long> _rateLimitNowTicks;
        private readonly int[] _sessionCacheConnectionIds = new int[SessionCacheSize];
        private readonly long[] _sessionCacheSessionIds = new long[SessionCacheSize];
        private readonly byte[] _sessionCacheState = new byte[SessionCacheSize]; // 0=unknown, 1=hit
        private readonly int[] _sessionCacheSeenPollGeneration = new int[SessionCacheSize];
        private readonly int _sessionCacheValidationInterval;
        private readonly int _sessionCacheValidationMask;

        private long _sessionCacheValidationCounter;
        private int _sessionCachePollGeneration = 1;
        private long _polledDatagrams;
        private long _decodedDatagrams;
        private long _droppedUnknownSessionDatagrams;
        private long _droppedRateLimitedDatagrams;
        private long _rateLimitStrikeEvents;
        private long _rateLimitDisconnectSuggestions;
        private long _appliedCommands;
        private long _flushCalls;
        private long _pollCalls;
        private long _sessionCacheHits;
        private long _sessionCacheMisses;
        private long _pollElapsedTicksTotal;
        private long _resolveElapsedTicksTotal;
        private long _rateLimitElapsedTicksTotal;
        private long _decodeElapsedTicksTotal;
        private long _consumeElapsedTicksTotal;
        private long _maxPollElapsedTicks;
        private long _maxResolveElapsedTicks;
        private long _maxRateLimitElapsedTicks;
        private long _maxDecodeElapsedTicks;
        private long _maxConsumeElapsedTicks;

        public NetworkInboundCommandPipeline(
            INetworkTransport transport,
            ConnectionSessionResolver sessionResolver,
            InboundCommandDecoder<TCommand> decoder,
            NetworkInboundCommandQueue<TCommand>? queue = null,
            int maxPollBatch = 256,
            bool queueThreadSafe = true,
            bool statsThreadSafe = true,
            InboundRateLimitPolicy? rateLimitPolicy = null,
            InboundRateLimitStrikeHandler? rateLimitStrikeHandler = null,
            int sessionCacheValidationInterval = 1,
            Func<long>? rateLimitNowTicks = null)
        {
            ArgumentNullException.ThrowIfNull(transport);
            ArgumentNullException.ThrowIfNull(sessionResolver);
            ArgumentNullException.ThrowIfNull(decoder);
            if (maxPollBatch <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPollBatch));

            _transport = transport;
            _sessionResolver = sessionResolver;
            _decoder = decoder;
            _pollBuffer = new InboundDatagram[maxPollBatch];
            _queue = queue ?? new NetworkInboundCommandQueue<TCommand>(threadSafe: queueThreadSafe);
            _threadSafeStats = statsThreadSafe;
            _rateLimitNowTicks = rateLimitNowTicks ?? Stopwatch.GetTimestamp;
            _sessionCacheValidationInterval = sessionCacheValidationInterval <= 1 ? 1 : sessionCacheValidationInterval;
            _sessionCacheValidationMask = (_sessionCacheValidationInterval & (_sessionCacheValidationInterval - 1)) == 0
                ? _sessionCacheValidationInterval - 1
                : -1;
            _hasRateLimitPolicy = rateLimitPolicy.HasValue;
            if (_hasRateLimitPolicy)
            {
                _rateLimitPolicy = rateLimitPolicy!.Value;
                _rateLimitStrikeHandler = rateLimitStrikeHandler;
                _rateLimitStates = new Dictionary<long, RateLimitState>(256);
            }
        }

        public NetworkInboundCommandQueue<TCommand> Queue => _queue;

        public int PollAndDecodeInbound()
        {
            int decodedCount = 0;
            int droppedUnknownCount = 0;
            int droppedRateLimitedCount = 0;
            int strikeEventCount = 0;
            int disconnectSuggestionCount = 0;
            int polledDatagrams = 0;
            int sessionCacheHits = 0;
            int sessionCacheMisses = 0;
            int cachedConnectionId = int.MinValue;
            bool hasCachedSession = false;
            int pollGeneration = NextPollGeneration();
            long totalPollElapsedTicks = 0;
            long totalResolveElapsedTicks = 0;
            long totalRateLimitElapsedTicks = 0;
            long totalDecodeElapsedTicks = 0;
            long totalConsumeElapsedTicks = 0;
            long maxPollElapsedTicks = 0;
            long maxResolveElapsedTicks = 0;
            long maxRateLimitElapsedTicks = 0;
            long maxDecodeElapsedTicks = 0;
            long maxConsumeElapsedTicks = 0;

            try
            {
                while (true)
                {
                    long pollStarted = Stopwatch.GetTimestamp();
                    int polledCount = _transport.PollInbound(_pollBuffer);
                    long pollElapsedTicks = Stopwatch.GetTimestamp() - pollStarted;
                    totalPollElapsedTicks += pollElapsedTicks;
                    if (pollElapsedTicks > maxPollElapsedTicks)
                        maxPollElapsedTicks = pollElapsedTicks;
                    if (polledCount == 0)
                        break;

                    try
                    {
                        polledDatagrams += polledCount;

                        for (int i = 0; i < polledCount; i++)
                        {
                            ref readonly InboundDatagram datagram = ref _pollBuffer[i];
                            int connectionId = datagram.ConnectionId;
                            int slot = connectionId & (SessionCacheSize - 1);
                            long sessionId;
                            bool cachedSlotHit = _sessionCacheState[slot] == 1 && _sessionCacheConnectionIds[slot] == connectionId;
                            if (cachedSlotHit)
                                sessionCacheHits++;
                            else
                                sessionCacheMisses++;
                            bool validateCachedHit =
                                _sessionCacheSeenPollGeneration[slot] != pollGeneration ||
                                _sessionCacheConnectionIds[slot] != connectionId ||
                                ShouldValidateSessionCacheHit();
                            long resolveStarted = Stopwatch.GetTimestamp();
                            if (hasCachedSession && connectionId == cachedConnectionId)
                            {
                                if (!TryResolveSessionCached(connectionId, slot, validateCachedHit, out sessionId))
                                {
                                    long resolveElapsedTicks = Stopwatch.GetTimestamp() - resolveStarted;
                                    totalResolveElapsedTicks += resolveElapsedTicks;
                                    if (resolveElapsedTicks > maxResolveElapsedTicks)
                                        maxResolveElapsedTicks = resolveElapsedTicks;
                                    droppedUnknownCount++;
                                    continue;
                                }
                            }
                            else if (TryResolveSessionCached(connectionId, slot, validateCachedHit, out sessionId))
                            {
                                cachedConnectionId = connectionId;
                                hasCachedSession = true;
                            }
                            else
                            {
                                long resolveElapsedTicks = Stopwatch.GetTimestamp() - resolveStarted;
                                totalResolveElapsedTicks += resolveElapsedTicks;
                                if (resolveElapsedTicks > maxResolveElapsedTicks)
                                    maxResolveElapsedTicks = resolveElapsedTicks;
                                droppedUnknownCount++;
                                continue;
                            }
                            long resolveElapsed = Stopwatch.GetTimestamp() - resolveStarted;
                            totalResolveElapsedTicks += resolveElapsed;
                            if (resolveElapsed > maxResolveElapsedTicks)
                                maxResolveElapsedTicks = resolveElapsed;
                            _sessionCacheSeenPollGeneration[slot] = pollGeneration;
                            if (_hasRateLimitPolicy)
                            {
                                long nowTick = _rateLimitNowTicks();
                                long rateLimitStarted = Stopwatch.GetTimestamp();
                                if (ShouldDropForRateLimit(
                                        sessionId,
                                        nowTick,
                                        out bool strikeRaised,
                                        out bool disconnectSuggested,
                                        out int strikeCount))
                                {
                                    long rateLimitElapsedTicks = Stopwatch.GetTimestamp() - rateLimitStarted;
                                    totalRateLimitElapsedTicks += rateLimitElapsedTicks;
                                    if (rateLimitElapsedTicks > maxRateLimitElapsedTicks)
                                        maxRateLimitElapsedTicks = rateLimitElapsedTicks;
                                    droppedRateLimitedCount++;
                                    if (strikeRaised)
                                        strikeEventCount++;

                                    if (disconnectSuggested)
                                    {
                                        disconnectSuggestionCount++;
                                        _rateLimitStrikeHandler?.Invoke(sessionId, strikeCount);
                                    }

                                    continue;
                                }

                                long rateLimitElapsed = Stopwatch.GetTimestamp() - rateLimitStarted;
                                totalRateLimitElapsedTicks += rateLimitElapsed;
                                if (rateLimitElapsed > maxRateLimitElapsedTicks)
                                    maxRateLimitElapsedTicks = rateLimitElapsed;
                            }

                            long decodeStarted = Stopwatch.GetTimestamp();
                            _decoder(sessionId, in datagram, _queue);
                            long decodeElapsedTicks = Stopwatch.GetTimestamp() - decodeStarted;
                            totalDecodeElapsedTicks += decodeElapsedTicks;
                            if (decodeElapsedTicks > maxDecodeElapsedTicks)
                                maxDecodeElapsedTicks = decodeElapsedTicks;
                            decodedCount++;
                        }
                    }
                    finally
                    {
                        long consumeStarted = Stopwatch.GetTimestamp();
                        _transport.CompleteInboundConsume();
                        long consumeElapsedTicks = Stopwatch.GetTimestamp() - consumeStarted;
                        totalConsumeElapsedTicks += consumeElapsedTicks;
                        if (consumeElapsedTicks > maxConsumeElapsedTicks)
                            maxConsumeElapsedTicks = consumeElapsedTicks;
                    }

                    if (polledCount < _pollBuffer.Length)
                        break;
                }
            }
            finally
            {
                AddCounter(ref _polledDatagrams, polledDatagrams);
                AddCounter(ref _droppedUnknownSessionDatagrams, droppedUnknownCount);
                AddCounter(ref _droppedRateLimitedDatagrams, droppedRateLimitedCount);
                AddCounter(ref _rateLimitStrikeEvents, strikeEventCount);
                AddCounter(ref _rateLimitDisconnectSuggestions, disconnectSuggestionCount);
                if (decodedCount > 0)
                    AddCounter(ref _decodedDatagrams, decodedCount);
                AddCounter(ref _pollCalls, 1);
                AddCounter(ref _sessionCacheHits, sessionCacheHits);
                AddCounter(ref _sessionCacheMisses, sessionCacheMisses);
                AddCounter(ref _pollElapsedTicksTotal, totalPollElapsedTicks);
                AddCounter(ref _resolveElapsedTicksTotal, totalResolveElapsedTicks);
                AddCounter(ref _rateLimitElapsedTicksTotal, totalRateLimitElapsedTicks);
                AddCounter(ref _decodeElapsedTicksTotal, totalDecodeElapsedTicks);
                AddCounter(ref _consumeElapsedTicksTotal, totalConsumeElapsedTicks);
                UpdateMax(ref _maxPollElapsedTicks, maxPollElapsedTicks);
                UpdateMax(ref _maxResolveElapsedTicks, maxResolveElapsedTicks);
                UpdateMax(ref _maxRateLimitElapsedTicks, maxRateLimitElapsedTicks);
                UpdateMax(ref _maxDecodeElapsedTicks, maxDecodeElapsedTicks);
                UpdateMax(ref _maxConsumeElapsedTicks, maxConsumeElapsedTicks);
            }

            return decodedCount;
        }

        public int ApplyPreBarrier(InboundCommandHandler<TCommand> handler)
        {
            int applied = _queue.Drain(handler);
            if (applied > 0)
                AddCounter(ref _appliedCommands, applied);

            return applied;
        }

        public void FlushOutbound()
        {
            _transport.FlushOutbound();
            AddCounter(ref _flushCalls, 1);
        }

        public InboundPipelineStatsSnapshot SnapshotStats() => new InboundPipelineStatsSnapshot(
            polledDatagrams: ReadCounter(ref _polledDatagrams),
            decodedDatagrams: ReadCounter(ref _decodedDatagrams),
            droppedUnknownSessionDatagrams: ReadCounter(ref _droppedUnknownSessionDatagrams),
            droppedRateLimitedDatagrams: ReadCounter(ref _droppedRateLimitedDatagrams),
            rateLimitStrikeEvents: ReadCounter(ref _rateLimitStrikeEvents),
            rateLimitDisconnectSuggestions: ReadCounter(ref _rateLimitDisconnectSuggestions),
            appliedCommands: ReadCounter(ref _appliedCommands),
            flushCalls: ReadCounter(ref _flushCalls),
            pollCalls: ReadCounter(ref _pollCalls),
            sessionCacheHits: ReadCounter(ref _sessionCacheHits),
            sessionCacheMisses: ReadCounter(ref _sessionCacheMisses),
            pollElapsedTicksTotal: ReadCounter(ref _pollElapsedTicksTotal),
            resolveElapsedTicksTotal: ReadCounter(ref _resolveElapsedTicksTotal),
            rateLimitElapsedTicksTotal: ReadCounter(ref _rateLimitElapsedTicksTotal),
            decodeElapsedTicksTotal: ReadCounter(ref _decodeElapsedTicksTotal),
            consumeElapsedTicksTotal: ReadCounter(ref _consumeElapsedTicksTotal),
            maxPollElapsedTicks: ReadCounter(ref _maxPollElapsedTicks),
            maxResolveElapsedTicks: ReadCounter(ref _maxResolveElapsedTicks),
            maxRateLimitElapsedTicks: ReadCounter(ref _maxRateLimitElapsedTicks),
            maxDecodeElapsedTicks: ReadCounter(ref _maxDecodeElapsedTicks),
            maxConsumeElapsedTicks: ReadCounter(ref _maxConsumeElapsedTicks));

        private void AddCounter(ref long counter, long delta)
        {
            if (delta == 0)
                return;

            if (_threadSafeStats)
            {
                Interlocked.Add(ref counter, delta);
                return;
            }

            counter += delta;
        }

        private long ReadCounter(ref long counter)
        {
            if (_threadSafeStats)
                return Interlocked.Read(ref counter);

            Thread.MemoryBarrier();
            return counter;
        }

        private void UpdateMax(ref long target, long candidate)
        {
            if (candidate <= 0)
                return;

            if (_threadSafeStats)
            {
                long current;
                do
                {
                    current = Interlocked.Read(ref target);
                    if (candidate <= current)
                        return;
                }
                while (Interlocked.CompareExchange(ref target, candidate, current) != current);
                return;
            }

            if (candidate > target)
                target = candidate;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldValidateSessionCacheHit()
        {
            if (_sessionCacheValidationInterval <= 1)
                return true;

            long sample = ++_sessionCacheValidationCounter;
            if (_sessionCacheValidationMask >= 0)
                return (sample & _sessionCacheValidationMask) == 0;

            return (sample % _sessionCacheValidationInterval) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryResolveSessionCached(int connectionId, int slot, bool validateCachedHit, out long sessionId)
        {
            if (_sessionCacheState[slot] == 1 && _sessionCacheConnectionIds[slot] == connectionId)
            {
                if (!validateCachedHit)
                {
                    sessionId = _sessionCacheSessionIds[slot];
                    return true;
                }
            }

            if (_sessionResolver(connectionId, out sessionId))
            {
                _sessionCacheConnectionIds[slot] = connectionId;
                _sessionCacheSessionIds[slot] = sessionId;
                _sessionCacheState[slot] = 1;
                return true;
            }

            _sessionCacheConnectionIds[slot] = 0;
            _sessionCacheSessionIds[slot] = 0;
            _sessionCacheState[slot] = 0;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int NextPollGeneration()
        {
            if (_sessionCachePollGeneration == int.MaxValue)
            {
                Array.Clear(_sessionCacheSeenPollGeneration, 0, _sessionCacheSeenPollGeneration.Length);
                _sessionCachePollGeneration = 1;
                return _sessionCachePollGeneration;
            }

            _sessionCachePollGeneration++;
            return _sessionCachePollGeneration;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldDropForRateLimit(
            long sessionId,
            long nowTick,
            out bool strikeRaised,
            out bool disconnectSuggested,
            out int strikeCount)
        {
            strikeRaised = false;
            disconnectSuggested = false;
            strikeCount = 0;

            if (!_hasRateLimitPolicy || _rateLimitStates is null)
                return false;

            // Mo10: use ref to mutate the dictionary value in-place — eliminates the struct
            // copy-out / copy-back pattern and the double dictionary lookup on the common path.
            ref RateLimitState state = ref CollectionsMarshal.GetValueRefOrAddDefault(
                _rateLimitStates, sessionId, out bool existed);
            if (!existed)
            {
                state.WindowStartTick = nowTick;
                state.WindowCount = 1;
                return false;
            }

            if (_rateLimitPolicy.StrikeResetTicks > 0 &&
                state.StrikeCount > 0 &&
                nowTick - state.LastStrikeTick >= _rateLimitPolicy.StrikeResetTicks)
            {
                state.StrikeCount = 0;
                state.DisconnectSuggested = false;
            }

            if (nowTick - state.WindowStartTick >= _rateLimitPolicy.WindowTicks)
            {
                state.WindowStartTick = nowTick;
                state.WindowCount = 0;
            }

            int saturatedWindowCount = _rateLimitPolicy.MaxDatagramsPerWindow < int.MaxValue
                ? _rateLimitPolicy.MaxDatagramsPerWindow + 1
                : int.MaxValue;
            if (state.WindowCount < saturatedWindowCount)
                state.WindowCount++;
            if (state.WindowCount <= _rateLimitPolicy.MaxDatagramsPerWindow)
                return false;

            if (state.LastStrikeWindowStartTick != state.WindowStartTick)
            {
                state.LastStrikeWindowStartTick = state.WindowStartTick;
                state.LastStrikeTick = nowTick;
                state.StrikeCount++;
                strikeRaised = true;
            }

            if (!state.DisconnectSuggested &&
                _rateLimitPolicy.StrikeThreshold > 0 &&
                state.StrikeCount >= _rateLimitPolicy.StrikeThreshold)
            {
                state.DisconnectSuggested = true;
                disconnectSuggested = true;
            }

            strikeCount = state.StrikeCount;
            return true;
        }
    }
}
