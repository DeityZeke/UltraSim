using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace UltraSim.Network
{
    public sealed class NetworkReplicationPipeline : IDisposable
    {
        public delegate void SupplementalBuildCallback(BuildSession session);

        public sealed class BuildSession
        {
            private readonly NetworkReplicationPipeline _pipeline;

            internal BuildSession(NetworkReplicationPipeline pipeline)
            {
                _pipeline = pipeline;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryGetConnectionStartTick(int connectionId, out long startTick)
            {
                return _pipeline.TryGetSupplementalConnectionStartTick(connectionId, out startTick);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryDispatchSelectedRecipient(int connectionId, in EncodedReplicationDelta delta)
            {
                return _pipeline.TryDispatchSupplementalRecipient(connectionId, in delta);
            }
        }

        public readonly struct BuildTimingSnapshot
        {
            public BuildTimingSnapshot(
                long replayVisitCalls,
                long replayVisitElapsedTicksTotal,
                long replayVisitElapsedTicksMax,
                long coalesceCommitCalls,
                long coalesceCommitElapsedTicksTotal,
                long coalesceCommitElapsedTicksMax,
                long deferredFlushCalls,
                long deferredFlushElapsedTicksTotal,
                long deferredFlushElapsedTicksMax,
                long recipientSelectionCalls,
                long recipientSelectionElapsedTicksTotal,
                long recipientSelectionElapsedTicksMax,
                long selectedDispatchCalls,
                long selectedDispatchElapsedTicksTotal,
                long selectedDispatchElapsedTicksMax,
                long trySendCalls,
                long trySendElapsedTicksTotal,
                long trySendElapsedTicksMax)
            {
                ReplayVisitCalls = replayVisitCalls;
                ReplayVisitElapsedTicksTotal = replayVisitElapsedTicksTotal;
                ReplayVisitElapsedTicksMax = replayVisitElapsedTicksMax;
                CoalesceCommitCalls = coalesceCommitCalls;
                CoalesceCommitElapsedTicksTotal = coalesceCommitElapsedTicksTotal;
                CoalesceCommitElapsedTicksMax = coalesceCommitElapsedTicksMax;
                DeferredFlushCalls = deferredFlushCalls;
                DeferredFlushElapsedTicksTotal = deferredFlushElapsedTicksTotal;
                DeferredFlushElapsedTicksMax = deferredFlushElapsedTicksMax;
                RecipientSelectionCalls = recipientSelectionCalls;
                RecipientSelectionElapsedTicksTotal = recipientSelectionElapsedTicksTotal;
                RecipientSelectionElapsedTicksMax = recipientSelectionElapsedTicksMax;
                SelectedDispatchCalls = selectedDispatchCalls;
                SelectedDispatchElapsedTicksTotal = selectedDispatchElapsedTicksTotal;
                SelectedDispatchElapsedTicksMax = selectedDispatchElapsedTicksMax;
                TrySendCalls = trySendCalls;
                TrySendElapsedTicksTotal = trySendElapsedTicksTotal;
                TrySendElapsedTicksMax = trySendElapsedTicksMax;
            }

            public long ReplayVisitCalls { get; }
            public long ReplayVisitElapsedTicksTotal { get; }
            public long ReplayVisitElapsedTicksMax { get; }
            public long CoalesceCommitCalls { get; }
            public long CoalesceCommitElapsedTicksTotal { get; }
            public long CoalesceCommitElapsedTicksMax { get; }
            public long DeferredFlushCalls { get; }
            public long DeferredFlushElapsedTicksTotal { get; }
            public long DeferredFlushElapsedTicksMax { get; }
            public long RecipientSelectionCalls { get; }
            public long RecipientSelectionElapsedTicksTotal { get; }
            public long RecipientSelectionElapsedTicksMax { get; }
            public long SelectedDispatchCalls { get; }
            public long SelectedDispatchElapsedTicksTotal { get; }
            public long SelectedDispatchElapsedTicksMax { get; }
            public long TrySendCalls { get; }
            public long TrySendElapsedTicksTotal { get; }
            public long TrySendElapsedTicksMax { get; }
        }

        private const int MinDeferredArenaSlabBytes = 4 * 1024;
        private const int MaxDeferredArenaSlabBytes = 256 * 1024;
        private const int MaxRecipientSelectionCacheEntries = 262_144;
        private const int MaxInterestCacheEntries = 262_144;
        private const int MaxSentDeferredKeyGenerationEntries = 65_536;

        private struct DeferredDelta
        {
            public long Tick;
            public NetworkEntityId EntityId;
            public ushort StreamId;
            public ReplicationLane Lane;
            public ulong CoalesceKey;
            public ulong SentKeyHash;
            public byte[] PayloadBuffer;
            public int PayloadOffset;
            public int PayloadLength;
            public int PayloadCapacity;

            public DeferredDelta(
                long tick,
                NetworkEntityId entityId,
                ushort streamId,
                ReplicationLane lane,
                ulong coalesceKey,
                ulong sentKeyHash,
                byte[] payloadBuffer,
                int payloadOffset,
                int payloadLength)
            {
                Tick = tick;
                EntityId = entityId;
                StreamId = streamId;
                Lane = lane;
                CoalesceKey = coalesceKey;
                SentKeyHash = sentKeyHash;
                PayloadBuffer = payloadBuffer;
                PayloadOffset = payloadOffset;
                PayloadLength = payloadLength;
                PayloadCapacity = payloadLength;
            }
        }

        private readonly struct PendingCoalescedDelta
        {
            public readonly long Tick;
            public readonly NetworkEntityId EntityId;
            public readonly ushort StreamId;
            public readonly ReplicationLane Lane;
            public readonly ulong CoalesceKey;
            public readonly byte[] PayloadBuffer;
            public readonly int PayloadOffset;
            public readonly int PayloadLength;

            public PendingCoalescedDelta(
                long tick,
                NetworkEntityId entityId,
                ushort streamId,
                ReplicationLane lane,
                ulong coalesceKey,
                byte[] payloadBuffer,
                int payloadOffset,
                int payloadLength)
            {
                Tick = tick;
                EntityId = entityId;
                StreamId = streamId;
                Lane = lane;
                CoalesceKey = coalesceKey;
                PayloadBuffer = payloadBuffer;
                PayloadOffset = payloadOffset;
                PayloadLength = payloadLength;
            }
        }

        private sealed class DeferredDeltaQueue
        {
            private readonly DeferredDelta[] _items;
            private int _head;
            private int _tail;
            private int _count;

            public DeferredDeltaQueue(int capacity)
            {
                if (capacity <= 0)
                    capacity = 1;

                _items = new DeferredDelta[capacity];
            }

            public int Count
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _count;
            }

            public int Capacity
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _items.Length;
            }

            public bool TryEnqueue(in DeferredDelta delta)
            {
                if (_count == _items.Length)
                    return false;

                EnqueueAssumeCapacity(in delta);
                return true;
            }

            public void EnqueueAssumeCapacity(in DeferredDelta delta)
            {
                _items[_tail] = delta;
                _tail++;
                if (_tail == _items.Length)
                    _tail = 0;
                _count++;
            }

            public bool TryPeek(out DeferredDelta delta)
            {
                if (_count == 0)
                {
                    delta = default;
                    return false;
                }

                delta = _items[_head];
                return true;
            }

            public bool TryDequeue(out DeferredDelta delta)
            {
                if (_count == 0)
                {
                    delta = default;
                    return false;
                }

                delta = _items[_head];
                _items[_head] = default;
                _head++;
                if (_head == _items.Length)
                    _head = 0;
                _count--;
                return true;
            }

            public void Clear()
            {
                if (_count == 0)
                    return;

                // Clear only the occupied slots to release PayloadBuffer references.
                if (_head < _tail)
                {
                    Array.Clear(_items, _head, _count);
                }
                else
                {
                    // Either wraps around or is exactly full (head >= tail, count > 0).
                    Array.Clear(_items, _head, _items.Length - _head);
                    if (_tail > 0)
                        Array.Clear(_items, 0, _tail);
                }

                _head = 0;
                _tail = 0;
                _count = 0;
            }

            public void ForEach(Action<DeferredDelta> visitor)
            {
                if (_count == 0)
                    return;

                int index = _head;
                for (int i = 0; i < _count; i++)
                {
                    visitor(_items[index]);
                    index++;
                    if (index == _items.Length)
                        index = 0;
                }
            }

            public bool TryGetMinTick(out long minTick)
            {
                if (_count == 0)
                {
                    minTick = 0;
                    return false;
                }

                // Queue is FIFO and filled in ascending-tick replay order, so the head
                // always holds the oldest (minimum) tick. O(1) vs the previous O(N) scan.
                minTick = _items[_head].Tick;
                return true;
            }
        }

        private sealed class DeferredArena
        {
            private byte[][] _slabs = Array.Empty<byte[]>();
            private int[] _usedBytes = Array.Empty<int>();
            private int _slabCount;
            private readonly int _defaultSlabBytes;

            public DeferredArena(int defaultSlabBytes)
            {
                _defaultSlabBytes = defaultSlabBytes <= 0 ? MinDeferredArenaSlabBytes : defaultSlabBytes;
            }

            public void Allocate(int length, out byte[] buffer, out int offset)
            {
                if (length <= 0)
                {
                    buffer = Array.Empty<byte>();
                    offset = 0;
                    return;
                }

                int slabIndex = _slabCount - 1;
                if (slabIndex < 0 || _usedBytes[slabIndex] + length > _slabs[slabIndex].Length)
                {
                    AddSlab(length);
                    slabIndex = _slabCount - 1;
                }

                buffer = _slabs[slabIndex];
                offset = _usedBytes[slabIndex];
                _usedBytes[slabIndex] += length;
            }

            public void Reset()
            {
                for (int i = 0; i < _slabCount; i++)
                {
                    ArrayPool<byte>.Shared.Return(_slabs[i]);
                    _slabs[i] = Array.Empty<byte>();
                    _usedBytes[i] = 0;
                }

                _slabCount = 0;
            }

            private void AddSlab(int minimumBytes)
            {
                if (_slabCount == _slabs.Length)
                    GrowArrays();

                int slabBytes = _defaultSlabBytes;
                if (minimumBytes > slabBytes)
                    slabBytes = NextPowerOfTwo(minimumBytes);

                _slabs[_slabCount] = ArrayPool<byte>.Shared.Rent(slabBytes);
                _usedBytes[_slabCount] = 0;
                _slabCount++;
            }

            private void GrowArrays()
            {
                int newSize = _slabs.Length == 0 ? 4 : _slabs.Length << 1;
                Array.Resize(ref _slabs, newSize);
                Array.Resize(ref _usedBytes, newSize);
            }

            private static int NextPowerOfTwo(int value)
            {
                if (value <= 1)
                    return 1;
                if (value > (1 << 30))
                    ThrowDeferredArenaSizeTooLarge(value);

                uint v = (uint)(value - 1);
                v |= v >> 1;
                v |= v >> 2;
                v |= v >> 4;
                v |= v >> 8;
                v |= v >> 16;
                return (int)(v + 1);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void ThrowDeferredArenaSizeTooLarge(int value) =>
                throw new InvalidOperationException(
                    $"Deferred arena slab size request {value} is too large.");
        }

        private sealed class ConnectionState
        {
            public int ConnectionId;
            public uint GroupId;
            public long LastAckedTick;
            public long LastBuiltTick;
            public int UsedBytes;
            public int UsedPackets;
            public bool IsBudgetExhausted;
            // Index of this connection in the current build's _connectionStateScratch array.
            // Set at the start of BuildTick; used by the M2 interest bitmask cache.
            public int BuildSlotIndex;
            public long StartTick;
            public bool HasDeferredWork;
            public int DeferredWorkListIndex;
            public readonly int[] DeferredByLane;
            public readonly int[] CoalescedByLane;
            public readonly DeferredDeltaQueue DeferredQueue;
            public readonly Dictionary<ulong, DeferredDelta> CoalescedDeferred;
            public readonly PriorityQueue<ulong, ulong> CoalescedDeferredOrder;
            public readonly Dictionary<ulong, PendingCoalescedDelta> PendingCoalesced;
            public readonly DeferredArena DeferredArena;
            public readonly Dictionary<ulong, int> SentDeferredKeyGenerationByHash;
            public int SentDeferredGeneration;
            public int SentDeferredCountThisBuild;
            public ulong NextCoalescedOrder;

            public ConnectionState(int maxDeferredPackets, int deferredArenaSlabBytes)
            {
                int capacity = maxDeferredPackets <= 0 ? 1 : maxDeferredPackets;
                DeferredByLane = new int[ReplicationLaneCount];
                CoalescedByLane = new int[ReplicationLaneCount];
                DeferredQueue = new DeferredDeltaQueue(capacity);
                CoalescedDeferred = new Dictionary<ulong, DeferredDelta>(capacity);
                CoalescedDeferredOrder = new PriorityQueue<ulong, ulong>(capacity);
                PendingCoalesced = new Dictionary<ulong, PendingCoalescedDelta>(capacity);
                DeferredArena = new DeferredArena(deferredArenaSlabBytes);
                SentDeferredKeyGenerationByHash = new Dictionary<ulong, int>(capacity);
                SentDeferredGeneration = 1;
                SentDeferredCountThisBuild = 0;
                NextCoalescedOrder = 0;
            }

            public void Reset(int connectionId, uint groupId, long lastAckedTick)
            {
                ConnectionId = connectionId;
                GroupId = groupId;
                LastAckedTick = lastAckedTick;
                LastBuiltTick = lastAckedTick;
                UsedBytes = 0;
                UsedPackets = 0;
                IsBudgetExhausted = false;
                StartTick = 0;
                HasDeferredWork = false;
                DeferredWorkListIndex = -1;
                Array.Clear(DeferredByLane);
                Array.Clear(CoalescedByLane);
                DeferredQueue.Clear();
                if (CoalescedDeferred.Count != 0)
                    CoalescedDeferred.Clear();
                if (CoalescedDeferredOrder.Count != 0)
                    CoalescedDeferredOrder.Clear();
                if (PendingCoalesced.Count != 0)
                    PendingCoalesced.Clear();
                DeferredArena.Reset();
                if (SentDeferredKeyGenerationByHash.Count != 0)
                    SentDeferredKeyGenerationByHash.Clear();
                SentDeferredGeneration = 1;
                SentDeferredCountThisBuild = 0;
                NextCoalescedOrder = 0;
            }
        }

        private static readonly IComparer<ReplicationCandidate> CandidateComparer =
            Comparer<ReplicationCandidate>.Create(CompareCandidates);

        private struct BuildDispatchContext
        {
            public NetworkReplicationPipeline Pipeline;
            public ConnectionState[] ConnectionStates;
            public Dictionary<int, ConnectionState>? ConnectionStateById;
            public List<ConnectionState>?[] DenseConnectionGroups;
            public int DenseConnectionGroupCount;
            public Dictionary<uint, List<ConnectionState>> ConnectionGroups;
            public ReplicationPacketSink Sink;
            public int ConnectionCount;
            public int InterestedSelected;
            public int Sent;
            public int NonExhaustedConnections;
            public int VisitedNonDropOverflowDeltas;
            public int RemainingNonDropOverflowDeltas;
            public long ReplayMinTick;
            public long ReplayMaxTick;
            public bool StopReplayVisit;
        }

        private sealed class RecipientSelectionCacheEntry
        {
            public ulong Key;
            public int[] ConnectionIds = Array.Empty<int>();
            public int ConnectionCount;
            public ConnectionState[] States = Array.Empty<ConnectionState>();
            public int StateCount;
            public bool StatesValid;
            public ulong RecipientSignature;

            // Grows ConnectionIds only (called at path-1: new entry creation).
            // States is intentionally left alone here — it is only needed and grown
            // at path-2 (validation/dispatch) via EnsureStatesCapacity.
            public void EnsureConnectionIdsCapacity(int count)
            {
                if (ConnectionIds.Length < count)
                {
                    if (ConnectionIds.Length > 0)
                        ArrayPool<int>.Shared.Return(ConnectionIds);
                    ConnectionIds = ArrayPool<int>.Shared.Rent(count);
                }
            }

            // Grows States only (called at path-2: validation before dispatch).
            public void EnsureStatesCapacity(int count)
            {
                if (States.Length < count)
                {
                    if (States.Length > 0)
                        ArrayPool<ConnectionState>.Shared.Return(States, clearArray: false);
                    States = ArrayPool<ConnectionState>.Shared.Rent(count);
                }
            }

            public void ReturnPooledArrays()
            {
                if (ConnectionIds.Length > 0)
                {
                    ArrayPool<int>.Shared.Return(ConnectionIds);
                    ConnectionIds = Array.Empty<int>();
                }
                if (States.Length > 0)
                {
                    ArrayPool<ConnectionState>.Shared.Return(States, clearArray: false);
                    States = Array.Empty<ConnectionState>();
                }
            }
        }

        // M2: per-entity interest filter result cache, keyed by (entity,stream).
        // Each entry stores a pair of per-connection-slot bitmasks:
        //   EvalBits[slot/8] bit (slot%8)  = 1 -> interest has been evaluated for this slot
        //   IntBits[slot/8]  bit (slot%8)  = 1 -> _interestFilter returned true for this slot
        // Both arrays are zeroed when the entry is first allocated in a build.
        private sealed class InterestCacheEntry
        {
            public int[] EvalGenerationBySlot = Array.Empty<int>();
            public int[] TrueGenerationBySlot = Array.Empty<int>();

            // Grows both slot arrays to at least connectionCount using the shared ArrayPool,
            // eliminating the per-transition plain-array allocations from Array.Resize.
            public void EnsureSlotCapacity(int connectionCount)
            {
                if (EvalGenerationBySlot.Length < connectionCount)
                {
                    int[] old = EvalGenerationBySlot;
                    EvalGenerationBySlot = ArrayPool<int>.Shared.Rent(connectionCount);
                    if (old.Length > 0)
                    {
                        Array.Copy(old, EvalGenerationBySlot, old.Length);
                        ArrayPool<int>.Shared.Return(old);
                    }
                    // Zero new slots — ArrayPool returns dirty arrays; an uncleared eval slot
                    // whose stale value equals _interestCacheBuildGeneration would be a false hit.
                    Array.Clear(EvalGenerationBySlot, old.Length, EvalGenerationBySlot.Length - old.Length);
                }
                if (TrueGenerationBySlot.Length < connectionCount)
                {
                    int[] old = TrueGenerationBySlot;
                    TrueGenerationBySlot = ArrayPool<int>.Shared.Rent(connectionCount);
                    if (old.Length > 0)
                    {
                        Array.Copy(old, TrueGenerationBySlot, old.Length);
                        ArrayPool<int>.Shared.Return(old);
                    }
                    // New true-slots need not be zeroed — they are only read when the
                    // corresponding eval slot matches the current generation.
                }
            }

            public void ReturnPooledArrays()
            {
                if (EvalGenerationBySlot.Length > 0)
                {
                    ArrayPool<int>.Shared.Return(EvalGenerationBySlot);
                    EvalGenerationBySlot = Array.Empty<int>();
                }
                if (TrueGenerationBySlot.Length > 0)
                {
                    ArrayPool<int>.Shared.Return(TrueGenerationBySlot);
                    TrueGenerationBySlot = Array.Empty<int>();
                }
            }
        }

        private readonly Dictionary<int, ConnectionState> _connections =
            new Dictionary<int, ConnectionState>(64);
        private readonly List<ConnectionState> _connectionStates =
            new List<ConnectionState>(64);
        private readonly Dictionary<int, int> _connectionStateIndexById =
            new Dictionary<int, int>(64);
        private readonly Dictionary<uint, List<ConnectionState>> _connectionsByGroup =
            new Dictionary<uint, List<ConnectionState>>();
        private readonly List<ConnectionState> _connectionsWithDeferred =
            new List<ConnectionState>(64);
        private ConnectionState[] _connectionStateScratch = Array.Empty<ConnectionState>();
        private List<ConnectionState>?[] _denseConnectionGroups = Array.Empty<List<ConnectionState>?>();
        private readonly Stack<ConnectionState> _connectionPool = new Stack<ConnectionState>();
        private readonly ReplicationReplayWindow _replayWindow;
        private readonly ReplicationPayloadEncoder _encoder;
        private readonly ReplicationInterestFilter _interestFilter;
        private readonly ReplicationLodFilter? _lodFilter;
        private readonly ReplicationDeltaGroupSelector? _groupSelector;
        private readonly ReplicationRecipientSelector? _recipientSelector;
        private readonly ReplicationRecipientSelectionKeySelector? _recipientSelectionKeySelector;
        private readonly bool _assumeGroupSelectorFiltersInterest;
        private readonly bool _assumeGroupSelectorDefinesInterestGroups;
        private readonly bool _assumeRecipientSelectorDefinesInterest;
        private readonly bool _assumeRecipientSelectorDefinesLod;
        private readonly bool _assumeRecipientSelectorEntityScoped;
        private readonly bool _assumeInterestFilterAlwaysTrue;
        private readonly bool _allOverflowActionsDrop;
        private readonly ReplicationBudget _budget;
        private readonly int _deferredFlushPacketCapPerTick;
        private readonly int _deferredFlushByteCapPerTick;
        private readonly ReplicationOverflowPolicy _overflowPolicy;
        private readonly bool _hasCoalesceOverflow;
        private readonly ReplicationStats _stats;
        private readonly int _maxEncodedPayloadBytes;
        private readonly int _deferredArenaSlabBytes;
        private readonly bool _assumePublishedCandidatesOrdered;
        private static readonly int ReplicationLaneCount = Enum.GetValues<ReplicationLane>().Length;
        private const int DenseGroupLookupMaxGroupId = 4096;
        private ReplicationCandidate[] _scratchCandidates = Array.Empty<ReplicationCandidate>();
        private int[] _recipientConnectionIdScratch = new int[256]; // Pre-allocated to avoid first-call grow+double-invoke on selector
        private readonly Dictionary<ulong, int> _recipientSelectionEntryByKey = new Dictionary<ulong, int>(256);
        private RecipientSelectionCacheEntry[] _recipientSelectionEntries = Array.Empty<RecipientSelectionCacheEntry>();
        private int _recipientSelectionEntryCount;
        private bool _hasLastRecipientSelectionEntry;
        private ulong _lastRecipientSelectionKey;
        private int _lastRecipientSelectionEntryIndex;
        private readonly HashSet<ulong> _fanoutReuseSignatureSeen = new HashSet<ulong>();

        // M2: interest bitmask cache (active only when _assumeInterestFilterEntityScoped is set).
        private readonly bool _assumeInterestFilterEntityScoped;
        private readonly Dictionary<ulong, int> _interestCacheByKey = new Dictionary<ulong, int>(512);
        private InterestCacheEntry[] _interestCacheEntries = Array.Empty<InterestCacheEntry>();
        private int _interestCacheEntryCount;
        private int _interestCacheBuildGeneration = 1;
        private bool _hasLastInterestCacheEntry;
        private ulong _lastInterestCacheKey;
        private InterestCacheEntry? _lastInterestCacheEntry;
        private byte[] _encodeScratch;
        private long _replayVisitCalls;
        private long _replayVisitElapsedTicksTotal;
        private long _replayVisitElapsedTicksMax;
        private long _coalesceCommitCalls;
        private long _coalesceCommitElapsedTicksTotal;
        private long _coalesceCommitElapsedTicksMax;
        private long _deferredFlushCalls;
        private long _deferredFlushElapsedTicksTotal;
        private long _deferredFlushElapsedTicksMax;
        private BuildSession? _buildSession;
        private BuildDispatchContext _supplementalBuildContext;
        private bool _hasSupplementalBuildContext;
        private readonly ReplicationPacketSink _batchForwardSink;
        private ReplicationPacketBatchSink? _activeBatchSink;
        private ReplicationPacket[] _batchPacketBuffer = Array.Empty<ReplicationPacket>();
        private int _batchPacketCount;
        private int _batchPacketLimit;
        private bool _reportedMissingGroupThisBuild;
        private static readonly ConnectionStateIdComparer GroupConnectionComparer = new ConnectionStateIdComparer();
        public NetworkReplicationPipeline(
            int maxReplayTicks,
            ReplicationBudget budget,
            ReplicationOverflowPolicy overflowPolicy,
            ReplicationPayloadEncoder encoder,
            ReplicationInterestFilter interestFilter,
            ReplicationDeltaGroupSelector? groupSelector = null,
            ReplicationRecipientSelector? recipientSelector = null,
            ReplicationStats? stats = null,
            int maxEncodedPayloadBytes = 1024,
            bool assumePublishedCandidatesOrdered = false,
            bool assumeGroupSelectorFiltersInterest = false,
            bool assumeInterestFilterAlwaysTrue = false,
            bool assumeGroupSelectorDefinesInterestGroups = false,
            bool assumeRecipientSelectorDefinesInterest = false,
            bool assumeRecipientSelectorDefinesLod = false,
            bool assumeRecipientSelectorEntityScoped = false,
            ReplicationLodFilter? lodFilter = null,
            bool assumeInterestFilterEntityScoped = false,
            ReplicationRecipientSelectionKeySelector? recipientSelectionKeySelector = null,
            ReplicationReplayWindow? replayWindow = null)
        {
            ArgumentNullException.ThrowIfNull(encoder);
            ArgumentNullException.ThrowIfNull(interestFilter);
            if (maxEncodedPayloadBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxEncodedPayloadBytes));
            if (maxEncodedPayloadBytes > int.MaxValue / 8)
                throw new ArgumentOutOfRangeException(nameof(maxEncodedPayloadBytes));

            _replayWindow = replayWindow ?? new ReplicationReplayWindow(maxReplayTicks, initialBucketBytes: maxEncodedPayloadBytes * 8);
            _budget = budget;
            _deferredFlushPacketCapPerTick = ComputeDeferredFlushCap(_budget.MaxPacketsPerTick);
            _deferredFlushByteCapPerTick = ComputeDeferredFlushCap(_budget.MaxBytesPerTick);
            _overflowPolicy = overflowPolicy;
            _hasCoalesceOverflow =
                overflowPolicy.Critical == ReplicationOverflowAction.Coalesce ||
                overflowPolicy.State == ReplicationOverflowAction.Coalesce ||
                overflowPolicy.Cosmetic == ReplicationOverflowAction.Coalesce;
            _allOverflowActionsDrop =
                overflowPolicy.Critical == ReplicationOverflowAction.Drop &&
                overflowPolicy.State == ReplicationOverflowAction.Drop &&
                overflowPolicy.Cosmetic == ReplicationOverflowAction.Drop;
            _encoder = encoder;
            _interestFilter = interestFilter;
            _lodFilter = lodFilter;
            Debug.Assert(!(assumeRecipientSelectorDefinesLod && lodFilter != null),
                "lodFilter must be null when assumeRecipientSelectorDefinesLod=true — LOD is applied by the selector; a separate lodFilter would double-apply it.");
            _groupSelector = groupSelector;
            _recipientSelector = recipientSelector;
            _recipientSelectionKeySelector = recipientSelectionKeySelector;
            _assumeGroupSelectorFiltersInterest = assumeGroupSelectorFiltersInterest;
            _assumeInterestFilterAlwaysTrue = assumeInterestFilterAlwaysTrue;
            _assumeGroupSelectorDefinesInterestGroups = assumeGroupSelectorDefinesInterestGroups;
            _assumeRecipientSelectorDefinesInterest = assumeRecipientSelectorDefinesInterest;
            _assumeRecipientSelectorDefinesLod = assumeRecipientSelectorDefinesLod;
            _assumeRecipientSelectorEntityScoped = assumeRecipientSelectorEntityScoped;
            _assumeInterestFilterEntityScoped = assumeInterestFilterEntityScoped;
            _stats = stats ?? new ReplicationStats(threadSafe: false);
            _maxEncodedPayloadBytes = maxEncodedPayloadBytes;
            int deferredPackets = _budget.MaxDeferredPackets <= 0 ? 1 : _budget.MaxDeferredPackets;
            long derivedSlabBytesLong = (long)_maxEncodedPayloadBytes * deferredPackets;
            int derivedSlabBytes = derivedSlabBytesLong >= int.MaxValue ? int.MaxValue : (int)derivedSlabBytesLong;
            _deferredArenaSlabBytes = Math.Clamp(derivedSlabBytes, MinDeferredArenaSlabBytes, MaxDeferredArenaSlabBytes);
            _assumePublishedCandidatesOrdered = assumePublishedCandidatesOrdered;
            _encodeScratch = new byte[maxEncodedPayloadBytes];
            _batchForwardSink = ForwardPacketToBatch;
        }

        public NetworkReplicationPipeline(
            int maxReplayTicks,
            ReplicationBudget budget,
            ReplicationOverflowPolicy overflowPolicy,
            AllocatingReplicationPayloadEncoder allocatingEncoder,
            ReplicationInterestFilter interestFilter,
            ReplicationDeltaGroupSelector? groupSelector = null,
            ReplicationRecipientSelector? recipientSelector = null,
            ReplicationStats? stats = null,
            int maxEncodedPayloadBytes = 1024,
            bool assumePublishedCandidatesOrdered = false,
            bool assumeGroupSelectorFiltersInterest = false,
            bool assumeInterestFilterAlwaysTrue = false,
            bool assumeGroupSelectorDefinesInterestGroups = false,
            bool assumeRecipientSelectorDefinesInterest = false,
            bool assumeRecipientSelectorDefinesLod = false,
            bool assumeRecipientSelectorEntityScoped = false,
            ReplicationLodFilter? lodFilter = null,
            bool assumeInterestFilterEntityScoped = false,
            ReplicationRecipientSelectionKeySelector? recipientSelectionKeySelector = null,
            ReplicationReplayWindow? replayWindow = null)
            : this(
                maxReplayTicks,
                budget,
                overflowPolicy,
                AdaptAllocatingEncoder(allocatingEncoder, maxEncodedPayloadBytes),
                interestFilter,
                groupSelector,
                recipientSelector,
                stats,
                maxEncodedPayloadBytes,
                assumePublishedCandidatesOrdered,
                assumeGroupSelectorFiltersInterest,
                assumeInterestFilterAlwaysTrue,
                assumeGroupSelectorDefinesInterestGroups,
                assumeRecipientSelectorDefinesInterest,
                assumeRecipientSelectorDefinesLod,
                assumeRecipientSelectorEntityScoped,
                lodFilter,
                assumeInterestFilterEntityScoped,
                recipientSelectionKeySelector,
                replayWindow)
        {
        }

        public ReplicationStats Stats
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _stats;
        }

        public BuildTimingSnapshot GetBuildTimingSnapshot() =>
            new BuildTimingSnapshot(
                _replayVisitCalls,
                _replayVisitElapsedTicksTotal,
                _replayVisitElapsedTicksMax,
                _coalesceCommitCalls,
                _coalesceCommitElapsedTicksTotal,
                _coalesceCommitElapsedTicksMax,
                _deferredFlushCalls,
                _deferredFlushElapsedTicksTotal,
                _deferredFlushElapsedTicksMax,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0);

        public ReplicationReplayWindow.ReplayWindowSnapshot GetReplayWindowSnapshot() =>
            _replayWindow.Snapshot();

        public int ConnectionCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _connections.Count;
        }

        public int MaxPacketsPerTick => _budget.MaxPacketsPerTick;

        public void TrackConnection(int connectionId, long lastAckedTick = 0, uint groupId = 0)
        {
            if (_connections.ContainsKey(connectionId))
                return;

            ConnectionState state = RentConnectionState(connectionId, groupId, lastAckedTick);
            _connections.Add(connectionId, state);
            _connectionStateIndexById[connectionId] = _connectionStates.Count;
            _connectionStates.Add(state);
            AddConnectionToGroup(state);
        }

        public bool UntrackConnection(int connectionId)
        {
            if (!_connections.TryGetValue(connectionId, out ConnectionState? state))
                return false;

            if (state.HasDeferredWork)
                RemoveConnectionDeferredWork(state);
            RemoveConnectionFromGroup(state);
            _connections.Remove(connectionId);
            if (_connectionStateIndexById.TryGetValue(connectionId, out int index))
            {
                int last = _connectionStates.Count - 1;
                ConnectionState lastState = _connectionStates[last];
                _connectionStates[index] = lastState;
                _connectionStates.RemoveAt(last);
                _connectionStateIndexById.Remove(connectionId);
                if (index < last)
                {
                    _connectionStateIndexById[lastState.ConnectionId] = index;
                    lastState.BuildSlotIndex = index;
                }
            }
            ReturnConnectionState(state);
            return true;
        }

        public bool SetConnectionGroup(int connectionId, uint groupId)
        {
            if (!_connections.TryGetValue(connectionId, out ConnectionState? state))
                return false;
            if (state.GroupId == groupId)
                return true;

            RemoveConnectionFromGroup(state);
            state.GroupId = groupId;
            AddConnectionToGroup(state);
            return true;
        }

        public bool SetLastAckedTick(int connectionId, long lastAckedTick)
        {
            if (!_connections.TryGetValue(connectionId, out ConnectionState? state))
                return false;

            if (lastAckedTick > state.LastAckedTick)
                state.LastAckedTick = lastAckedTick;

            return true;
        }

        public bool RequestReplayFromTick(int connectionId, long replayFromTick)
        {
            if (!_connections.TryGetValue(connectionId, out ConnectionState? state))
                return false;

            state.LastBuiltTick = replayFromTick == long.MinValue ? long.MinValue : replayFromTick - 1;
            return true;
        }

        public bool TryGetCursor(int connectionId, out ConnectionReplicationCursor cursor)
        {
            if (_connections.TryGetValue(connectionId, out ConnectionState? state))
            {
                cursor = new ConnectionReplicationCursor(
                    lastAckedTick: state.LastAckedTick,
                    lastBuiltTick: state.LastBuiltTick,
                    deferredCount: state.DeferredQueue.Count,
                    coalescedDeferredCount: state.CoalescedDeferred.Count);
                return true;
            }

            cursor = default;
            return false;
        }

        public bool TryGetBudgetDebt(int connectionId, long currentTick, out ConnectionReplicationDebt debt)
        {
            if (!_connections.TryGetValue(connectionId, out ConnectionState? state))
            {
                debt = default;
                return false;
            }

            bool hasOutstanding = (state.DeferredQueue.Count + state.CoalescedDeferred.Count) > 0;
            long oldestTick = 0;
            long oldestAgeTicks = 0;
            if (hasOutstanding && TryGetOldestDeferredTick(state, out oldestTick))
            {
                oldestAgeTicks = currentTick - oldestTick;
                if (oldestAgeTicks < 0)
                    oldestAgeTicks = 0;
            }

            debt = new ConnectionReplicationDebt(
                connectionId: state.ConnectionId,
                deferredCritical: state.DeferredByLane[(int)ReplicationLane.Critical],
                deferredState: state.DeferredByLane[(int)ReplicationLane.State],
                deferredCosmetic: state.DeferredByLane[(int)ReplicationLane.Cosmetic],
                coalescedCritical: state.CoalescedByLane[(int)ReplicationLane.Critical],
                coalescedState: state.CoalescedByLane[(int)ReplicationLane.State],
                coalescedCosmetic: state.CoalescedByLane[(int)ReplicationLane.Cosmetic],
                hasOutstanding: hasOutstanding,
                oldestDeferredTick: oldestTick,
                oldestDeferredAgeTicks: oldestAgeTicks);
            return true;
        }

        public int PublishCandidates(ReadOnlySpan<ReplicationCandidate> candidates)
        {
            int count = candidates.Length;
            if (count == 0)
                return 0;

            _stats.RecordDirtyDrained(count);

            ReadOnlySpan<ReplicationCandidate> source;
            if (_assumePublishedCandidatesOrdered || IsCandidateOrderStable(candidates))
            {
                source = candidates;
            }
            else
            {
                EnsureCandidateScratchCapacity(count);
                candidates.CopyTo(_scratchCandidates);
                if (count > 1)
                    Array.Sort(_scratchCandidates, 0, count, CandidateComparer);
                source = _scratchCandidates.AsSpan(0, count);
            }

            int encodedDeltas = 0;
            int encodedBytes = 0;
            for (int i = 0; i < count; i++)
            {
                ref readonly ReplicationCandidate candidate = ref source[i];
                int payloadLength = _encoder(in candidate, _encodeScratch);
                if (payloadLength < 0 || payloadLength > _maxEncodedPayloadBytes)
                    ThrowEncoderOutOfRange(payloadLength, _maxEncodedPayloadBytes);
                if (payloadLength == 0)
                    continue;

                encodedDeltas++;
                encodedBytes += payloadLength;
                _replayWindow.Add(
                    tick: candidate.Tick,
                    entityId: candidate.EntityId,
                    streamId: candidate.StreamId,
                    lane: candidate.Lane,
                    coalesceKey: candidate.CoalesceKey,
                    payload: _encodeScratch.AsSpan(0, payloadLength));
            }

            _stats.RecordEncodedBatch(encodedDeltas, encodedBytes);
            return count;
        }

        private static bool IsCandidateOrderStable(ReadOnlySpan<ReplicationCandidate> candidates)
        {
            int count = candidates.Length;
            if (count <= 1)
                return true;

            ref readonly ReplicationCandidate first = ref candidates[0];
            long previousTick = first.Tick;
            ReplicationLane previousLane = first.Lane;
            ulong previousEntity = first.EntityId.Value;
            ushort previousStream = first.StreamId;
            ulong previousCoalesceKey = first.CoalesceKey;

            for (int i = 1; i < count; i++)
            {
                ref readonly ReplicationCandidate current = ref candidates[i];
                long currentTick = current.Tick;
                if (currentTick < previousTick)
                    return false;
                if (currentTick > previousTick)
                    goto update;

                ReplicationLane currentLane = current.Lane;
                if (currentLane < previousLane)
                    return false;
                if (currentLane > previousLane)
                    goto update;

                ulong currentEntity = current.EntityId.Value;
                if (currentEntity < previousEntity)
                    return false;
                if (currentEntity > previousEntity)
                    goto update;

                ushort currentStream = current.StreamId;
                if (currentStream < previousStream)
                    return false;
                if (currentStream > previousStream)
                    goto update;

                ulong currentCoalesceKey = current.CoalesceKey;
                if (currentCoalesceKey < previousCoalesceKey)
                    return false;

            update:
                previousTick = currentTick;
                previousLane = current.Lane;
                previousEntity = current.EntityId.Value;
                previousStream = current.StreamId;
                previousCoalesceKey = current.CoalesceKey;
            }

            return true;
        }

        private void EnsureCandidateScratchCapacity(int required)
        {
            if (_scratchCandidates.Length >= required)
                return;

            int newSize = _scratchCandidates.Length == 0 ? 64 : _scratchCandidates.Length;
            while (newSize < required)
            {
                if (newSize > int.MaxValue / 2)
                {
                    newSize = required;
                    break;
                }

                newSize <<= 1;
            }

            _scratchCandidates = new ReplicationCandidate[newSize];
        }

        private static ReplicationPayloadEncoder AdaptAllocatingEncoder(
            AllocatingReplicationPayloadEncoder allocatingEncoder,
            int maxPayloadBytes)
        {
            ArgumentNullException.ThrowIfNull(allocatingEncoder);
            if (maxPayloadBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

            return (in ReplicationCandidate candidate, Span<byte> destination) =>
            {
                byte[] payload = allocatingEncoder(in candidate);
                if (payload == null || payload.Length == 0)
                    return 0;
                if (payload.Length > destination.Length || payload.Length > maxPayloadBytes)
                    ThrowEncoderPayloadTooLarge(payload.Length, maxPayloadBytes);

                payload.AsSpan().CopyTo(destination);
                return payload.Length;
            };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowEncoderOutOfRange(int payloadLength, int maxPayloadBytes) =>
            throw new InvalidOperationException(
                $"Replication encoder returned invalid length {payloadLength}. Max is {maxPayloadBytes}.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowEncoderPayloadTooLarge(int payloadLength, int maxPayloadBytes) =>
            throw new InvalidOperationException(
                $"Allocating replication encoder returned payload length {payloadLength} which exceeds max {maxPayloadBytes}.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowRecipientSelectorOutOfRange(int returnedCount, int capacity) =>
            throw new InvalidOperationException(
                $"Replication recipient selector returned {returnedCount} recipients after resize. Capacity was {capacity}.");

        private static void RecordElapsed(
            long startTimestamp,
            ref long callCount,
            ref long elapsedTicksTotal,
            ref long elapsedTicksMax)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
            callCount++;
            elapsedTicksTotal += elapsedTicks;
            if (elapsedTicks > elapsedTicksMax)
                elapsedTicksMax = elapsedTicks;
        }

        public int PruneReplayWindow(long currentTick)
        {
            int pruned = _replayWindow.Prune(currentTick);
            _stats.RecordReplayTicksPruned(pruned);
            return pruned;
        }

        public int BuildTick(
            long currentTick,
            ReplicationPacketSink sink,
            bool pruneReplayWindow = true,
            SupplementalBuildCallback? supplementalBuild = null)
        {
            ArgumentNullException.ThrowIfNull(sink);

            int connectionCount = _connectionStates.Count;
            if (connectionCount == 0)
                return 0;

            EnsureConnectionScratchCapacity(connectionCount);
            long minStartTick = long.MaxValue;

            for (int i = 0; i < connectionCount; i++)
            {
                ConnectionState state = _connectionStates[i];
                _connectionStateScratch[i] = state;
                BeginSentDeferredGeneration(state);
                state.UsedBytes = 0;
                state.UsedPackets = 0;
                state.IsBudgetExhausted = false;
                state.StartTick = GetStartTick(state);
                state.BuildSlotIndex = i;
                if (state.StartTick < minStartTick)
                    minStartTick = state.StartTick;
            }

            Dictionary<int, ConnectionState>? connectionStateById =
                (_recipientSelector is null && supplementalBuild is null) ? null : _connections;

            if (pruneReplayWindow)
                PruneReplayWindow(currentTick);

            int sent = 0;
            if (minStartTick == long.MaxValue)
            {
                if (_connectionsWithDeferred.Count != 0)
                {
                    long deferredFlushStartTimestamp = Stopwatch.GetTimestamp();
                    try
                    {
                        sent += FlushDeferredTrackedConnections(
                            sink,
                            _deferredFlushPacketCapPerTick,
                            _deferredFlushByteCapPerTick);
                    }
                    finally
                    {
                        RecordElapsed(
                            deferredFlushStartTimestamp,
                            ref _deferredFlushCalls,
                            ref _deferredFlushElapsedTicksTotal,
                            ref _deferredFlushElapsedTicksMax);
                    }
                }

                return sent;
            }

            int nonExhaustedConnections = 0;
            for (int i = 0; i < connectionCount; i++)
            {
                if (!_connectionStateScratch[i].IsBudgetExhausted)
                    nonExhaustedConnections++;
            }

            var context = new BuildDispatchContext
            {
                Pipeline = this,
                ConnectionStates = _connectionStateScratch,
                ConnectionStateById = connectionStateById,
                DenseConnectionGroups = _denseConnectionGroups,
                DenseConnectionGroupCount = _denseConnectionGroups.Length,
                ConnectionGroups = _connectionsByGroup,
                Sink = sink,
                ConnectionCount = connectionCount,
                Sent = sent,
                NonExhaustedConnections = nonExhaustedConnections,
                VisitedNonDropOverflowDeltas = 0,
                RemainingNonDropOverflowDeltas = _allOverflowActionsDrop
                    ? 0
                    : CountNonDropOverflowDeltasInRange(minStartTick, currentTick),
                ReplayMinTick = minStartTick,
                ReplayMaxTick = currentTick,
                StopReplayVisit = false,
            };
            _reportedMissingGroupThisBuild = false;
            TrimRecipientSelectionCacheIfNeeded();
            if (_recipientSelectionEntryByKey.Count != 0)
                _recipientSelectionEntryByKey.Clear();
            _recipientSelectionEntryCount = 0;
            _hasLastRecipientSelectionEntry = false;
            _lastRecipientSelectionKey = 0;
            _lastRecipientSelectionEntryIndex = -1;
            if (_fanoutReuseSignatureSeen.Count != 0)
                _fanoutReuseSignatureSeen.Clear();
            // M2/C5: per-build interest cache generation bump (no per-build bitmask clears).
            if (_assumeInterestFilterEntityScoped)
            {
                TrimInterestCacheIfNeeded();
                if (_interestCacheBuildGeneration == int.MaxValue)
                {
                    ResetInterestCacheForGenerationWrap();
                }
                else
                {
                    _interestCacheBuildGeneration++;
                }
                _hasLastInterestCacheEntry = false;
                _lastInterestCacheKey = 0;
                _lastInterestCacheEntry = null;
            }
            TrySetStopReplayVisit(ref context);
            bool canSkipReplayVisit = context.StopReplayVisit;
            if (!canSkipReplayVisit)
            {
                long replayVisitStartTimestamp = Stopwatch.GetTimestamp();
                try
                {
                    if (_groupSelector is null)
                    {
                        if (_assumeInterestFilterAlwaysTrue)
                        {
                            _replayWindow.VisitRangeBucketsWhile(
                                minStartTick,
                                currentTick,
                                ref context,
                                VisitDeltaBucketForBuildUngroupedTrustedWhile);
                        }
                        else
                        {
                            _replayWindow.VisitRangeBucketsWhile(
                                minStartTick,
                                currentTick,
                                ref context,
                                VisitDeltaBucketForBuildUngroupedWhile);
                        }
                    }
                    else if (_assumeGroupSelectorFiltersInterest || _assumeInterestFilterAlwaysTrue)
                    {
                        _replayWindow.VisitRangeBucketsWhile(
                            minStartTick,
                            currentTick,
                            ref context,
                            VisitDeltaBucketForBuildGroupedTrustedWhile);
                    }
                    else
                    {
                        _replayWindow.VisitRangeBucketsWhile(
                            minStartTick,
                            currentTick,
                            ref context,
                            VisitDeltaBucketForBuildGroupedWhile);
                    }
                    sent = context.Sent;
                }
                finally
                {
                    RecordElapsed(
                        replayVisitStartTimestamp,
                        ref _replayVisitCalls,
                        ref _replayVisitElapsedTicksTotal,
                        ref _replayVisitElapsedTicksMax);
                }
            }
            sent = context.Sent;
            if (supplementalBuild != null)
            {
                BuildSession session = _buildSession ??= new BuildSession(this);
                _supplementalBuildContext = context;
                _hasSupplementalBuildContext = true;
                try
                {
                    supplementalBuild(session);
                    context = _supplementalBuildContext;
                    sent = context.Sent;
                }
                finally
                {
                    _hasSupplementalBuildContext = false;
                }
            }

            _stats.RecordInterestedSelected(context.InterestedSelected);

            if (_hasCoalesceOverflow)
            {
                long coalesceCommitStartTimestamp = Stopwatch.GetTimestamp();
                try
                {
                    for (int i = 0; i < connectionCount; i++)
                    {
                        ConnectionState state = _connectionStateScratch[i];
                        if (state.PendingCoalesced.Count != 0)
                            CommitPendingCoalesced(state);
                    }
                }
                finally
                {
                    RecordElapsed(
                        coalesceCommitStartTimestamp,
                        ref _coalesceCommitCalls,
                        ref _coalesceCommitElapsedTicksTotal,
                        ref _coalesceCommitElapsedTicksMax);
                }
            }

            if (_connectionsWithDeferred.Count != 0)
            {
                long deferredFlushStartTimestamp = Stopwatch.GetTimestamp();
                try
                {
                    sent += FlushDeferredTrackedConnections(
                        sink,
                        _deferredFlushPacketCapPerTick,
                        _deferredFlushByteCapPerTick);
                }
                finally
                {
                    RecordElapsed(
                        deferredFlushStartTimestamp,
                        ref _deferredFlushCalls,
                        ref _deferredFlushElapsedTicksTotal,
                        ref _deferredFlushElapsedTicksMax);
                }
            }

            for (int i = 0; i < connectionCount; i++)
            {
                ConnectionState state = _connectionStateScratch[i];
                if (currentTick > state.LastBuiltTick)
                    state.LastBuiltTick = currentTick;
            }

            return sent;
        }


        public int BuildTickBatched(
            long currentTick,
            int maxBatchPackets,
            ReplicationPacketBatchSink sink,
            bool pruneReplayWindow = true,
            SupplementalBuildCallback? supplementalBuild = null)
        {
            ArgumentNullException.ThrowIfNull(sink);
            if (maxBatchPackets <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBatchPackets));

            EnsureBatchPacketCapacity(maxBatchPackets);
            _activeBatchSink = sink;
            _batchPacketLimit = maxBatchPackets;
            _batchPacketCount = 0;

            try
            {
                int sent = BuildTick(currentTick, _batchForwardSink, pruneReplayWindow, supplementalBuild);
                FlushPacketBatch();
                return sent;
            }
            finally
            {
                _activeBatchSink = null;
                _batchPacketCount = 0;
                _batchPacketLimit = 0;
            }
        }

        private bool TryDispatchSelectedRecipients(
            ref BuildDispatchContext context,
            in EncodedReplicationDelta delta)
        {
            if (_recipientSelector is null)
                return false;

            ulong selectionCacheKey = _recipientSelectionKeySelector is null
                ? 0UL
                : _recipientSelectionKeySelector(in delta);
            if (selectionCacheKey == 0)
                selectionCacheKey = ComputeRecipientSelectionCacheKey(in delta, _assumeRecipientSelectorEntityScoped);
            Dictionary<int, ConnectionState>? byId = context.ConnectionStateById;
            if (byId is null)
                return false;

            RecipientSelectionCacheEntry entry;
            int entryIndex;
            if (_hasLastRecipientSelectionEntry && _lastRecipientSelectionKey == selectionCacheKey)
            {
                entryIndex = _lastRecipientSelectionEntryIndex;
                entry = _recipientSelectionEntries[entryIndex];
            }
            else if (!_recipientSelectionEntryByKey.TryGetValue(selectionCacheKey, out entryIndex))
            {
                entryIndex = _recipientSelectionEntryCount++;
                EnsureRecipientSelectionEntryCapacity(entryIndex + 1);
                entry = _recipientSelectionEntries[entryIndex];
                entry.Key = selectionCacheKey;
                _recipientSelectionEntryByKey[selectionCacheKey] = entryIndex;
                entry.ConnectionCount = 0;
                entry.StateCount = 0;
                entry.StatesValid = false;
                entry.RecipientSignature = 0;

                int selectedConnectionCount = _recipientSelector(in delta, _recipientConnectionIdScratch);
                if (selectedConnectionCount < 0)
                    return false;
                if (selectedConnectionCount <= 0)
                {
                    _recipientSelectionEntries[entryIndex] = entry;
                    return true;
                }

                if (selectedConnectionCount > _recipientConnectionIdScratch.Length)
                {
                    EnsureRecipientConnectionScratchCapacity(selectedConnectionCount);
                    selectedConnectionCount = _recipientSelector(in delta, _recipientConnectionIdScratch);
                    if (selectedConnectionCount < 0)
                        return false;
                    if (selectedConnectionCount <= 0)
                    {
                        _recipientSelectionEntries[entryIndex] = entry;
                        return true;
                    }
                    if (selectedConnectionCount > _recipientConnectionIdScratch.Length)
                        ThrowRecipientSelectorOutOfRange(selectedConnectionCount, _recipientConnectionIdScratch.Length);
                }

                entry.EnsureConnectionIdsCapacity(selectedConnectionCount);
                _recipientConnectionIdScratch.AsSpan(0, selectedConnectionCount).CopyTo(entry.ConnectionIds);
                entry.ConnectionCount = selectedConnectionCount;
                entry.StatesValid = false;
                _recipientSelectionEntries[entryIndex] = entry;
                _hasLastRecipientSelectionEntry = true;
                _lastRecipientSelectionKey = selectionCacheKey;
                _lastRecipientSelectionEntryIndex = entryIndex;
            }
            else
            {
                entry = _recipientSelectionEntries[entryIndex];
                if (entry.ConnectionCount <= 0)
                    return true;
                _hasLastRecipientSelectionEntry = true;
                _lastRecipientSelectionKey = selectionCacheKey;
                _lastRecipientSelectionEntryIndex = entryIndex;
            }

            int eligibleStateCount;
            if (!entry.StatesValid)
            {
                int selectedConnectionCount = entry.ConnectionCount;
                entry.EnsureStatesCapacity(selectedConnectionCount);

                eligibleStateCount = 0;
                ulong recipientSignature = 14695981039346656037UL;
                for (int i = 0; i < selectedConnectionCount; i++)
                {
                    int connectionId = entry.ConnectionIds[i];
                    if (!byId.TryGetValue(connectionId, out ConnectionState? state))
                        continue;
                    entry.States[eligibleStateCount++] = state;
                    recipientSignature = (recipientSignature ^ (uint)connectionId) * 1099511628211UL;
                }

                entry.StateCount = eligibleStateCount;
                entry.StatesValid = true;
                entry.RecipientSignature = recipientSignature;
                _recipientSelectionEntries[entryIndex] = entry;
            }
            else
            {
                eligibleStateCount = entry.StateCount;
            }
            if (eligibleStateCount > 1)
                RecordFanoutReuseSample(entry.RecipientSignature, eligibleStateCount, in delta);

            bool hasDeferredSentKeyHash = false;
            ulong deferredSentKeyHash = 0;

            for (int i = 0; i < eligibleStateCount; i++)
            {
                ConnectionState state = entry.States[i];
                if (delta.Tick < state.StartTick)
                    continue;
                int connectionId = state.ConnectionId;
                bool selectorDefinesLod = _assumeRecipientSelectorDefinesLod;

                if (_assumeRecipientSelectorDefinesInterest || _assumeInterestFilterAlwaysTrue)
                {
                    if (!selectorDefinesLod &&
                        !pipelinePassesLod(_lodFilter, connectionId, in delta))
                        continue;
                    context.InterestedSelected++;
                    if (TryHandleBudgetExhaustedPostInterest(
                        connectionId,
                        state,
                        in delta,
                        ref hasDeferredSentKeyHash,
                        ref deferredSentKeyHash))
                        continue;
                    bool wasBudgetExhausted = state.IsBudgetExhausted;
                    if (TrySendReplayDelta(
                        connectionId,
                        state,
                        in delta,
                        context.Sink,
                        ref hasDeferredSentKeyHash,
                        ref deferredSentKeyHash))
                        context.Sent++;
                    NoteBudgetExhaustionTransition(ref context, state, wasBudgetExhausted);
                    continue;
                }

                if (!PassesInterestFilter(ref context, pipeline: this, state, connectionId, in delta))
                    continue;
                if (!selectorDefinesLod &&
                    !pipelinePassesLod(_lodFilter, connectionId, in delta))
                    continue;

                context.InterestedSelected++;
                if (TryHandleBudgetExhaustedPostInterest(
                    connectionId,
                    state,
                    in delta,
                    ref hasDeferredSentKeyHash,
                    ref deferredSentKeyHash))
                    continue;
                bool wasBudgetExhaustedAfterFilter = state.IsBudgetExhausted;
                if (TrySendReplayDelta(
                    connectionId,
                    state,
                    in delta,
                    context.Sink,
                    ref hasDeferredSentKeyHash,
                    ref deferredSentKeyHash))
                    context.Sent++;
                NoteBudgetExhaustionTransition(ref context, state, wasBudgetExhaustedAfterFilter);
                if (context.StopReplayVisit)
                    break;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryDispatchExplicitRecipient(
            ref BuildDispatchContext context,
            int connectionId,
            in EncodedReplicationDelta delta)
        {
            Dictionary<int, ConnectionState>? byId = context.ConnectionStateById;
            if (byId is null || !byId.TryGetValue(connectionId, out ConnectionState? state))
                return false;

            if (delta.Tick < state.StartTick)
                return false;

            context.InterestedSelected++;

            bool hasDeferredSentKeyHash = false;
            ulong deferredSentKeyHash = 0;
            if (TryHandleBudgetExhaustedPostInterest(
                connectionId,
                state,
                in delta,
                ref hasDeferredSentKeyHash,
                ref deferredSentKeyHash))
            {
                return false;
            }

            bool wasBudgetExhausted = state.IsBudgetExhausted;
            bool sent = TrySendReplayDelta(
                connectionId,
                state,
                in delta,
                context.Sink,
                ref hasDeferredSentKeyHash,
                ref deferredSentKeyHash);
            if (sent)
                context.Sent++;

            NoteBudgetExhaustionTransition(ref context, state, wasBudgetExhausted);
            return sent;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetSupplementalConnectionStartTick(int connectionId, out long startTick)
        {
            if (_hasSupplementalBuildContext)
            {
                Dictionary<int, ConnectionState>? byId = _supplementalBuildContext.ConnectionStateById;
                if (byId != null &&
                    byId.TryGetValue(connectionId, out ConnectionState? state))
                {
                    startTick = state.StartTick;
                    return true;
                }
            }

            startTick = 0;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryDispatchSupplementalRecipient(int connectionId, in EncodedReplicationDelta delta)
        {
            if (!_hasSupplementalBuildContext)
                return false;

            return TryDispatchExplicitRecipient(ref _supplementalBuildContext, connectionId, in delta);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool PassesInterestFilter(
            ref BuildDispatchContext context,
            NetworkReplicationPipeline pipeline,
            ConnectionState state,
            int connectionId,
            in EncodedReplicationDelta delta)
        {
            if (!pipeline._assumeInterestFilterEntityScoped)
            {
                pipeline._stats.RecordInterestFilterCall();
                return pipeline._interestFilter(connectionId, in delta);
            }

            ulong cacheKey = ComputeInterestCacheKey(in delta);
            int slot = state.BuildSlotIndex;
            if ((uint)slot >= (uint)context.ConnectionCount)
            {
                // Defensive fallback: preserve correctness if a stale/non-built state reaches dispatch.
                pipeline._stats.RecordInterestFilterCall();
                return pipeline._interestFilter(connectionId, in delta);
            }

            InterestCacheEntry cacheEntry = pipeline.GetOrCreateInterestCacheEntryCached(cacheKey, context.ConnectionCount);
            if (pipeline.TryGetCachedInterest(cacheEntry, slot, out bool interested))
            {
                pipeline._stats.RecordInterestCacheHit();
                return interested;
            }

            pipeline._stats.RecordInterestCacheMiss();
            pipeline._stats.RecordInterestFilterCall();
            interested = pipeline._interestFilter(connectionId, in delta);
            pipeline.SetCachedInterest(cacheEntry, slot, interested);
            return interested;
        }

        private static void VisitDeltaForBuildUngrouped(ref BuildDispatchContext context, in EncodedReplicationDelta delta)
        {
            NetworkReplicationPipeline pipeline = context.Pipeline;
            if (pipeline.TryDispatchSelectedRecipients(ref context, in delta))
            {
                NoteReplayDeltaVisited(ref context, delta.Lane);
                return;
            }

            bool hasDeferredSentKeyHash = false;
            ulong deferredSentKeyHash = 0;
            for (int i = 0, safeConnectionCount = GetSafeConnectionCount(in context); i < safeConnectionCount; i++)
            {
                ProcessConnection(
                    ref context,
                    pipeline,
                    context.ConnectionStates[i],
                    in delta,
                    ref hasDeferredSentKeyHash,
                    ref deferredSentKeyHash);
                if (context.StopReplayVisit)
                    break;
            }
            NoteReplayDeltaVisited(ref context, delta.Lane);
        }

        private static bool VisitDeltaForBuildUngroupedWhile(ref BuildDispatchContext context, in EncodedReplicationDelta delta)
        {
            VisitDeltaForBuildUngrouped(ref context, in delta);
            return !context.StopReplayVisit;
        }

        private static void VisitDeltaForBuildUngroupedTrusted(ref BuildDispatchContext context, in EncodedReplicationDelta delta)
        {
            NetworkReplicationPipeline pipeline = context.Pipeline;
            if (pipeline.TryDispatchSelectedRecipients(ref context, in delta))
            {
                NoteReplayDeltaVisited(ref context, delta.Lane);
                return;
            }

            bool hasDeferredSentKeyHash = false;
            ulong deferredSentKeyHash = 0;
            for (int i = 0, safeConnectionCount = GetSafeConnectionCount(in context); i < safeConnectionCount; i++)
            {
                ConnectionState state = context.ConnectionStates[i];
                ProcessTrustedConnection(
                    ref context,
                    pipeline,
                    state,
                    in delta,
                    ref hasDeferredSentKeyHash,
                    ref deferredSentKeyHash);
                if (context.StopReplayVisit)
                    break;
            }
            NoteReplayDeltaVisited(ref context, delta.Lane);
        }

        private static bool VisitDeltaForBuildUngroupedTrustedWhile(ref BuildDispatchContext context, in EncodedReplicationDelta delta)
        {
            VisitDeltaForBuildUngroupedTrusted(ref context, in delta);
            return !context.StopReplayVisit;
        }

        private static bool VisitDeltaBucketForBuildUngroupedWhile(
            ref BuildDispatchContext context,
            long tick,
            byte[] buffer,
            ReadOnlySpan<ReplicationReplayWindow.DeltaEntry> deltas)
        {
            NetworkReplicationPipeline pipeline = context.Pipeline;
            int safeConnectionCount = GetSafeConnectionCount(in context);

            for (int d = 0; d < deltas.Length; d++)
            {
                EncodedReplicationDelta delta = CreateDelta(tick, buffer, in deltas[d]);

                if (pipeline.TryDispatchSelectedRecipients(ref context, in delta))
                {
                    NoteReplayDeltaVisited(ref context, delta.Lane);
                    if (context.StopReplayVisit)
                        return false;
                    continue;
                }

                bool hasDeferredSentKeyHash = false;
                ulong deferredSentKeyHash = 0;
                for (int i = 0; i < safeConnectionCount; i++)
                {
                    ProcessConnection(
                        ref context,
                        pipeline,
                        context.ConnectionStates[i],
                        in delta,
                        ref hasDeferredSentKeyHash,
                        ref deferredSentKeyHash);
                    if (context.StopReplayVisit)
                        break;
                }

                NoteReplayDeltaVisited(ref context, delta.Lane);
                if (context.StopReplayVisit)
                    return false;
            }

            return true;
        }

        private static bool VisitDeltaBucketForBuildUngroupedTrustedWhile(
            ref BuildDispatchContext context,
            long tick,
            byte[] buffer,
            ReadOnlySpan<ReplicationReplayWindow.DeltaEntry> deltas)
        {
            NetworkReplicationPipeline pipeline = context.Pipeline;
            int safeConnectionCount = GetSafeConnectionCount(in context);

            for (int d = 0; d < deltas.Length; d++)
            {
                EncodedReplicationDelta delta = CreateDelta(tick, buffer, in deltas[d]);

                if (pipeline.TryDispatchSelectedRecipients(ref context, in delta))
                {
                    NoteReplayDeltaVisited(ref context, delta.Lane);
                    if (context.StopReplayVisit)
                        return false;
                    continue;
                }

                bool hasDeferredSentKeyHash = false;
                ulong deferredSentKeyHash = 0;
                for (int i = 0; i < safeConnectionCount; i++)
                {
                    ConnectionState state = context.ConnectionStates[i];
                    ProcessTrustedConnection(
                        ref context,
                        pipeline,
                        state,
                        in delta,
                        ref hasDeferredSentKeyHash,
                        ref deferredSentKeyHash);
                    if (context.StopReplayVisit)
                        break;
                }

                NoteReplayDeltaVisited(ref context, delta.Lane);
                if (context.StopReplayVisit)
                    return false;
            }

            return true;
        }

        private static void VisitDeltaForBuildGrouped(ref BuildDispatchContext context, in EncodedReplicationDelta delta)
        {
            NetworkReplicationPipeline pipeline = context.Pipeline;
            if (pipeline.TryDispatchSelectedRecipients(ref context, in delta))
            {
                NoteReplayDeltaVisited(ref context, delta.Lane);
                return;
            }

            uint groupId = pipeline._groupSelector!(in delta);
            if (groupId < (uint)context.DenseConnectionGroupCount)
            {
                List<ConnectionState>? denseGroupStates = context.DenseConnectionGroups[(int)groupId];
                if (denseGroupStates != null)
                {
                    bool hasDeferredSentKeyHash = false;
                    ulong deferredSentKeyHash = 0;
                    Span<ConnectionState> denseStates = CollectionsMarshal.AsSpan(denseGroupStates);
                    for (int i = 0; i < denseStates.Length; i++)
                    {
                        ProcessConnection(
                            ref context,
                            pipeline,
                            denseStates[i],
                            in delta,
                            ref hasDeferredSentKeyHash,
                            ref deferredSentKeyHash);
                        if (context.StopReplayVisit)
                            break;
                    }
                    NoteReplayDeltaVisited(ref context, delta.Lane);
                    return;
                }
            }

            if (context.ConnectionGroups.TryGetValue(groupId, out List<ConnectionState>? groupStates))
            {
                bool hasDeferredSentKeyHash = false;
                ulong deferredSentKeyHash = 0;
                Span<ConnectionState> sparseStates = CollectionsMarshal.AsSpan(groupStates);
                for (int i = 0; i < sparseStates.Length; i++)
                {
                    ProcessConnection(
                        ref context,
                        pipeline,
                        sparseStates[i],
                        in delta,
                        ref hasDeferredSentKeyHash,
                        ref deferredSentKeyHash);
                    if (context.StopReplayVisit)
                        break;
                }
                NoteReplayDeltaVisited(ref context, delta.Lane);
                return;
            }

            if (pipeline._assumeGroupSelectorDefinesInterestGroups)
            {
                pipeline.ReportMissingGroup(groupId, context.DenseConnectionGroupCount);
                NoteReplayDeltaVisited(ref context, delta.Lane);
                return;
            }

            bool hasDeferredSentKeyHashFallback = false;
            ulong deferredSentKeyHashFallback = 0;
            for (int i = 0, safeConnectionCount = GetSafeConnectionCount(in context); i < safeConnectionCount; i++)
            {
                ProcessConnection(
                    ref context,
                    pipeline,
                    context.ConnectionStates[i],
                    in delta,
                    ref hasDeferredSentKeyHashFallback,
                    ref deferredSentKeyHashFallback);
                if (context.StopReplayVisit)
                    break;
            }
            NoteReplayDeltaVisited(ref context, delta.Lane);
        }

        private static bool VisitDeltaForBuildGroupedWhile(ref BuildDispatchContext context, in EncodedReplicationDelta delta)
        {
            VisitDeltaForBuildGrouped(ref context, in delta);
            return !context.StopReplayVisit;
        }

        private static void VisitDeltaForBuildGroupedTrusted(ref BuildDispatchContext context, in EncodedReplicationDelta delta)
        {
            NetworkReplicationPipeline pipeline = context.Pipeline;
            if (pipeline.TryDispatchSelectedRecipients(ref context, in delta))
            {
                NoteReplayDeltaVisited(ref context, delta.Lane);
                return;
            }

            uint groupId = pipeline._groupSelector!(in delta);
            if (groupId < (uint)context.DenseConnectionGroupCount)
            {
                List<ConnectionState>? denseGroupStates = context.DenseConnectionGroups[(int)groupId];
                if (denseGroupStates != null)
                {
                    bool hasDeferredSentKeyHash = false;
                    ulong deferredSentKeyHash = 0;
                    Span<ConnectionState> denseStates = CollectionsMarshal.AsSpan(denseGroupStates);
                    for (int i = 0; i < denseStates.Length; i++)
                    {
                        ProcessTrustedConnection(
                            ref context,
                            pipeline,
                            denseStates[i],
                            in delta,
                            ref hasDeferredSentKeyHash,
                            ref deferredSentKeyHash);
                        if (context.StopReplayVisit)
                            break;
                    }
                    NoteReplayDeltaVisited(ref context, delta.Lane);
                    return;
                }
            }

            if (context.ConnectionGroups.TryGetValue(groupId, out List<ConnectionState>? groupStates))
            {
                bool hasDeferredSentKeyHash = false;
                ulong deferredSentKeyHash = 0;
                Span<ConnectionState> sparseStates = CollectionsMarshal.AsSpan(groupStates);
                for (int i = 0; i < sparseStates.Length; i++)
                {
                    ProcessTrustedConnection(
                        ref context,
                        pipeline,
                        sparseStates[i],
                        in delta,
                        ref hasDeferredSentKeyHash,
                        ref deferredSentKeyHash);
                    if (context.StopReplayVisit)
                        break;
                }
                NoteReplayDeltaVisited(ref context, delta.Lane);
                return;
            }

            if (pipeline._assumeGroupSelectorFiltersInterest || pipeline._assumeGroupSelectorDefinesInterestGroups)
            {
                pipeline.ReportMissingGroup(groupId, context.DenseConnectionGroupCount);
                NoteReplayDeltaVisited(ref context, delta.Lane);
                return;
            }

            bool hasDeferredSentKeyHashFallback = false;
            ulong deferredSentKeyHashFallback = 0;
            for (int i = 0, safeConnectionCount = GetSafeConnectionCount(in context); i < safeConnectionCount; i++)
            {
                ProcessTrustedConnection(
                    ref context,
                    pipeline,
                    context.ConnectionStates[i],
                    in delta,
                    ref hasDeferredSentKeyHashFallback,
                    ref deferredSentKeyHashFallback);
                if (context.StopReplayVisit)
                    break;
            }
            NoteReplayDeltaVisited(ref context, delta.Lane);
        }

        private static bool VisitDeltaForBuildGroupedTrustedWhile(ref BuildDispatchContext context, in EncodedReplicationDelta delta)
        {
            VisitDeltaForBuildGroupedTrusted(ref context, in delta);
            return !context.StopReplayVisit;
        }

        private static bool VisitDeltaBucketForBuildGroupedWhile(
            ref BuildDispatchContext context,
            long tick,
            byte[] buffer,
            ReadOnlySpan<ReplicationReplayWindow.DeltaEntry> deltas)
        {
            VisitBucketForBuildGrouped(ref context, tick, buffer, deltas);
            return !context.StopReplayVisit;
        }

        private static bool VisitDeltaBucketForBuildGroupedTrustedWhile(
            ref BuildDispatchContext context,
            long tick,
            byte[] buffer,
            ReadOnlySpan<ReplicationReplayWindow.DeltaEntry> deltas)
        {
            VisitBucketForBuildGroupedTrusted(ref context, tick, buffer, deltas);
            return !context.StopReplayVisit;
        }

        private static void VisitBucketForBuildUngrouped(
            ref BuildDispatchContext context,
            long tick,
            byte[] buffer,
            ReadOnlySpan<ReplicationReplayWindow.DeltaEntry> deltas)
        {
            NetworkReplicationPipeline pipeline = context.Pipeline;
            for (int d = 0; d < deltas.Length; d++)
            {
                EncodedReplicationDelta delta = CreateDelta(tick, buffer, in deltas[d]);
                if (pipeline.TryDispatchSelectedRecipients(ref context, in delta))
                {
                    continue;
                }
                bool hasDeferredSentKeyHash = false;
                ulong deferredSentKeyHash = 0;
                for (int i = 0, safeConnectionCount = GetSafeConnectionCount(in context); i < safeConnectionCount; i++)
                {
                    ProcessConnection(
                        ref context,
                        pipeline,
                        context.ConnectionStates[i],
                        in delta,
                        ref hasDeferredSentKeyHash,
                        ref deferredSentKeyHash);
                    if (context.StopReplayVisit)
                        break;
                }
                if (context.StopReplayVisit)
                    return;
            }
        }

        private static void VisitBucketForBuildUngroupedTrusted(
            ref BuildDispatchContext context,
            long tick,
            byte[] buffer,
            ReadOnlySpan<ReplicationReplayWindow.DeltaEntry> deltas)
        {
            NetworkReplicationPipeline pipeline = context.Pipeline;
            for (int d = 0; d < deltas.Length; d++)
            {
                EncodedReplicationDelta delta = CreateDelta(tick, buffer, in deltas[d]);
                if (pipeline.TryDispatchSelectedRecipients(ref context, in delta))
                {
                    continue;
                }
                bool hasDeferredSentKeyHash = false;
                ulong deferredSentKeyHash = 0;
                for (int i = 0, safeConnectionCount = GetSafeConnectionCount(in context); i < safeConnectionCount; i++)
                {
                    ProcessTrustedConnection(
                        ref context,
                        pipeline,
                        context.ConnectionStates[i],
                        in delta,
                        ref hasDeferredSentKeyHash,
                        ref deferredSentKeyHash);
                    if (context.StopReplayVisit)
                        break;
                }
                if (context.StopReplayVisit)
                    return;
            }
        }

        private static void VisitBucketForBuildGrouped(
            ref BuildDispatchContext context,
            long tick,
            byte[] buffer,
            ReadOnlySpan<ReplicationReplayWindow.DeltaEntry> deltas)
        {
            NetworkReplicationPipeline pipeline = context.Pipeline;
            for (int d = 0; d < deltas.Length; d++)
            {
                EncodedReplicationDelta delta = CreateDelta(tick, buffer, in deltas[d]);
                if (pipeline.TryDispatchSelectedRecipients(ref context, in delta))
                {
                    continue;
                }
                uint groupId = pipeline._groupSelector!(in delta);
                if (groupId < (uint)context.DenseConnectionGroupCount)
                {
                    List<ConnectionState>? denseGroupStates = context.DenseConnectionGroups[(int)groupId];
                    if (denseGroupStates != null)
                    {
                        bool hasDeferredSentKeyHash = false;
                        ulong deferredSentKeyHash = 0;
                        Span<ConnectionState> denseStates = CollectionsMarshal.AsSpan(denseGroupStates);
                        for (int i = 0; i < denseStates.Length; i++)
                        {
                            ProcessConnection(
                                ref context,
                                pipeline,
                                denseStates[i],
                                in delta,
                                ref hasDeferredSentKeyHash,
                                ref deferredSentKeyHash);
                            if (context.StopReplayVisit)
                                break;
                        }
                        if (context.StopReplayVisit)
                            return;
                        continue;
                    }
                }

                if (context.ConnectionGroups.TryGetValue(groupId, out List<ConnectionState>? groupStates))
                {
                    bool hasDeferredSentKeyHash = false;
                    ulong deferredSentKeyHash = 0;
                    Span<ConnectionState> sparseStates = CollectionsMarshal.AsSpan(groupStates);
                    for (int i = 0; i < sparseStates.Length; i++)
                    {
                        ProcessConnection(
                            ref context,
                            pipeline,
                            sparseStates[i],
                            in delta,
                            ref hasDeferredSentKeyHash,
                            ref deferredSentKeyHash);
                        if (context.StopReplayVisit)
                            break;
                    }
                    if (context.StopReplayVisit)
                        return;
                    continue;
                }

                if (pipeline._assumeGroupSelectorDefinesInterestGroups)
                {
                    pipeline.ReportMissingGroup(groupId, context.DenseConnectionGroupCount);
                    continue;
                }

                bool hasDeferredSentKeyHashFallback = false;
                ulong deferredSentKeyHashFallback = 0;
                for (int i = 0, safeConnectionCount = GetSafeConnectionCount(in context); i < safeConnectionCount; i++)
                {
                    ProcessConnection(
                        ref context,
                        pipeline,
                        context.ConnectionStates[i],
                        in delta,
                        ref hasDeferredSentKeyHashFallback,
                        ref deferredSentKeyHashFallback);
                    if (context.StopReplayVisit)
                        break;
                }
                if (context.StopReplayVisit)
                    return;
            }
        }

        private static void VisitBucketForBuildGroupedTrusted(
            ref BuildDispatchContext context,
            long tick,
            byte[] buffer,
            ReadOnlySpan<ReplicationReplayWindow.DeltaEntry> deltas)
        {
            NetworkReplicationPipeline pipeline = context.Pipeline;
            for (int d = 0; d < deltas.Length; d++)
            {
                EncodedReplicationDelta delta = CreateDelta(tick, buffer, in deltas[d]);
                if (pipeline.TryDispatchSelectedRecipients(ref context, in delta))
                {
                    continue;
                }
                uint groupId = pipeline._groupSelector!(in delta);
                if (groupId < (uint)context.DenseConnectionGroupCount)
                {
                    List<ConnectionState>? denseGroupStates = context.DenseConnectionGroups[(int)groupId];
                    if (denseGroupStates != null)
                    {
                        bool hasDeferredSentKeyHash = false;
                        ulong deferredSentKeyHash = 0;
                        Span<ConnectionState> denseStates = CollectionsMarshal.AsSpan(denseGroupStates);
                        for (int i = 0; i < denseStates.Length; i++)
                        {
                            ProcessTrustedConnection(
                                ref context,
                                pipeline,
                                denseStates[i],
                                in delta,
                                ref hasDeferredSentKeyHash,
                                ref deferredSentKeyHash);
                            if (context.StopReplayVisit)
                                break;
                        }
                        if (context.StopReplayVisit)
                            return;
                        continue;
                    }
                }

                if (context.ConnectionGroups.TryGetValue(groupId, out List<ConnectionState>? groupStates))
                {
                    bool hasDeferredSentKeyHash = false;
                    ulong deferredSentKeyHash = 0;
                    Span<ConnectionState> sparseStates = CollectionsMarshal.AsSpan(groupStates);
                    for (int i = 0; i < sparseStates.Length; i++)
                    {
                        ProcessTrustedConnection(
                            ref context,
                            pipeline,
                            sparseStates[i],
                            in delta,
                            ref hasDeferredSentKeyHash,
                            ref deferredSentKeyHash);
                        if (context.StopReplayVisit)
                            break;
                    }
                    if (context.StopReplayVisit)
                        return;
                    continue;
                }

                if (pipeline._assumeGroupSelectorFiltersInterest || pipeline._assumeGroupSelectorDefinesInterestGroups)
                {
                    pipeline.ReportMissingGroup(groupId, context.DenseConnectionGroupCount);
                    continue;
                }

                bool hasDeferredSentKeyHashFallback = false;
                ulong deferredSentKeyHashFallback = 0;
                for (int i = 0, safeConnectionCount = GetSafeConnectionCount(in context); i < safeConnectionCount; i++)
                {
                    ProcessTrustedConnection(
                        ref context,
                        pipeline,
                        context.ConnectionStates[i],
                        in delta,
                        ref hasDeferredSentKeyHashFallback,
                        ref deferredSentKeyHashFallback);
                    if (context.StopReplayVisit)
                        break;
                }
                if (context.StopReplayVisit)
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static EncodedReplicationDelta CreateDelta(
            long tick,
            byte[] buffer,
            in ReplicationReplayWindow.DeltaEntry entry) =>
            new EncodedReplicationDelta(
                tick: tick,
                entityId: entry.EntityId,
                streamId: entry.StreamId,
                lane: entry.Lane,
                coalesceKey: entry.CoalesceKey,
                sentKeyHash: entry.SentKeyHash,
                payload: new ReadOnlyMemory<byte>(buffer, entry.Start, entry.Length));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ProcessConnection(
            ref BuildDispatchContext context,
            NetworkReplicationPipeline pipeline,
            ConnectionState state,
            in EncodedReplicationDelta delta,
            ref bool hasDeferredSentKeyHash,
            ref ulong deferredSentKeyHash)
        {
            if (delta.Tick < state.StartTick)
                return;

            int connectionId = state.ConnectionId;

            // M3: skip the interest/LOD filter for budget-exhausted connections when the overflow
            // action for this lane is Drop -- no deferred work is possible, so interest is irrelevant.
            if (state.IsBudgetExhausted)
            {
                if (pipeline._allOverflowActionsDrop)
                {
                    // No deferred deltas possible, skip WasDeferredSentThisBuild check.
                    pipeline._stats.RecordDropped(delta.Lane);
                    return;
                }
                if (pipeline._overflowPolicy.GetAction(delta.Lane) == ReplicationOverflowAction.Drop)
                {
                    if (!WasDeferredSentThisBuild(
                        state,
                        in delta,
                        ref hasDeferredSentKeyHash,
                        ref deferredSentKeyHash))
                        pipeline._stats.RecordDropped(delta.Lane);
                    return;
                }
                // Defer/Coalesce: still need the interest filter to know whether to buffer.
            }

            // M2: when assumeInterestFilterEntityScoped=true, interest is evaluated once per
            // (entity+stream, connection slot) per build and reused across replay deltas.

            if (!PassesInterestFilter(ref context, pipeline, state, connectionId, in delta))
            {
                return;
            }

            if (!pipelinePassesLod(pipeline._lodFilter, connectionId, in delta))
                return;

            context.InterestedSelected++;
            if (pipeline.TryHandleBudgetExhaustedPostInterest(
                connectionId,
                state,
                in delta,
                ref hasDeferredSentKeyHash,
                ref deferredSentKeyHash))
                return;
            bool wasBudgetExhausted = state.IsBudgetExhausted;
            if (pipeline.TrySendReplayDelta(
                connectionId,
                state,
                in delta,
                context.Sink,
                ref hasDeferredSentKeyHash,
                ref deferredSentKeyHash))
            {
                context.Sent++;
            }
            NoteBudgetExhaustionTransition(ref context, state, wasBudgetExhausted);
            return;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ProcessTrustedConnection(
            ref BuildDispatchContext context,
            NetworkReplicationPipeline pipeline,
            ConnectionState state,
            in EncodedReplicationDelta delta,
            ref bool hasDeferredSentKeyHash,
            ref ulong deferredSentKeyHash)
        {
            if (delta.Tick < state.StartTick)
                return;

            int connectionId = state.ConnectionId;

            // M3: skip LOD filter for budget-exhausted connections when Drop is the overflow action.
            if (state.IsBudgetExhausted)
            {
                if (pipeline._allOverflowActionsDrop)
                {
                    pipeline._stats.RecordDropped(delta.Lane);
                    return;
                }
                if (pipeline._overflowPolicy.GetAction(delta.Lane) == ReplicationOverflowAction.Drop)
                {
                    if (!WasDeferredSentThisBuild(
                        state,
                        in delta,
                        ref hasDeferredSentKeyHash,
                        ref deferredSentKeyHash))
                        pipeline._stats.RecordDropped(delta.Lane);
                    return;
                }
            }

            if (!pipelinePassesLod(pipeline._lodFilter, connectionId, in delta))
                return;

            context.InterestedSelected++;
            if (pipeline.TryHandleBudgetExhaustedPostInterest(
                connectionId,
                state,
                in delta,
                ref hasDeferredSentKeyHash,
                ref deferredSentKeyHash))
                return;
            bool wasBudgetExhausted = state.IsBudgetExhausted;
            if (pipeline.TrySendReplayDelta(
                connectionId,
                state,
                in delta,
                context.Sink,
                ref hasDeferredSentKeyHash,
                ref deferredSentKeyHash))
                context.Sent++;
            NoteBudgetExhaustionTransition(ref context, state, wasBudgetExhausted);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool pipelinePassesLod(
            ReplicationLodFilter? lodFilter,
            int connectionId,
            in EncodedReplicationDelta delta) =>
            lodFilter is null || lodFilter(connectionId, in delta);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetSafeConnectionCount(in BuildDispatchContext context)
        {
            int connectionCount = context.ConnectionCount;
            int stateCount = context.ConnectionStates.Length;
            return connectionCount <= stateCount ? connectionCount : stateCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void NoteBudgetExhaustionTransition(
            ref BuildDispatchContext context,
            ConnectionState state,
            bool wasBudgetExhausted)
        {
            if (wasBudgetExhausted || !state.IsBudgetExhausted)
                return;

            if (context.NonExhaustedConnections > 0)
                context.NonExhaustedConnections--;
            TrySetStopReplayVisit(ref context);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void NoteReplayDeltaVisited(
            ref BuildDispatchContext context,
            ReplicationLane lane)
        {
            if (!context.Pipeline._allOverflowActionsDrop &&
                context.Pipeline._overflowPolicy.GetAction(lane) != ReplicationOverflowAction.Drop)
            {
                context.VisitedNonDropOverflowDeltas++;
                if (context.RemainingNonDropOverflowDeltas > 0)
                    context.RemainingNonDropOverflowDeltas--;
            }
            TrySetStopReplayVisit(ref context);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void TrySetStopReplayVisit(ref BuildDispatchContext context)
        {
            if (context.NonExhaustedConnections != 0)
                return;

            if (context.Pipeline._allOverflowActionsDrop)
            {
                context.StopReplayVisit = true;
                return;
            }

            if (context.RemainingNonDropOverflowDeltas == 0)
                context.StopReplayVisit = true;
        }

        private static long GetStartTick(ConnectionState state)
        {
            long ackStart = state.LastAckedTick == long.MaxValue ? long.MaxValue : state.LastAckedTick + 1;
            long builtStart = state.LastBuiltTick == long.MaxValue ? long.MaxValue : state.LastBuiltTick + 1;
            return ackStart > builtStart ? ackStart : builtStart;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TrySendReplayDelta(
            int connectionId,
            ConnectionState state,
            in EncodedReplicationDelta delta,
            ReplicationPacketSink sink,
            ref bool hasDeferredSentKeyHash,
            ref ulong deferredSentKeyHash)
        {
            if (WasDeferredSentThisBuild(
                state,
                in delta,
                ref hasDeferredSentKeyHash,
                ref deferredSentKeyHash))
                return false;

            if (TrySend(connectionId, state, in delta, sink))
                return true;

            HandleOverflow(state, in delta);
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryHandleBudgetExhaustedPostInterest(
            int connectionId,
            ConnectionState state,
            in EncodedReplicationDelta delta,
            ref bool hasDeferredSentKeyHash,
            ref ulong deferredSentKeyHash)
        {
            if (!state.IsBudgetExhausted)
                return false;

            if (WasDeferredSentThisBuild(
                state,
                in delta,
                ref hasDeferredSentKeyHash,
                ref deferredSentKeyHash))
                return true;

            if (_overflowPolicy.GetAction(delta.Lane) == ReplicationOverflowAction.Drop)
            {
                _stats.RecordDropped(delta.Lane);
                return true;
            }

            // Defer/Coalesce still need the normal overflow path to preserve semantics.
            return false;
        }

        private int CountNonDropOverflowDeltasInRange(long minTickInclusive, long maxTickInclusive)
        {
            if (minTickInclusive > maxTickInclusive)
                return 0;

            _replayWindow.CountLanesInRange(
                minTickInclusive,
                maxTickInclusive,
                out int criticalCount,
                out int stateCount,
                out int cosmeticCount);

            int count = 0;
            if (_overflowPolicy.GetAction(ReplicationLane.Critical) != ReplicationOverflowAction.Drop)
                count += criticalCount;
            if (_overflowPolicy.GetAction(ReplicationLane.State) != ReplicationOverflowAction.Drop)
                count += stateCount;
            if (_overflowPolicy.GetAction(ReplicationLane.Cosmetic) != ReplicationOverflowAction.Drop)
                count += cosmeticCount;

            return count;
        }

        private void EnsureConnectionScratchCapacity(int required)
        {
            if (_connectionStateScratch.Length >= required)
                return;

            int newSize = _connectionStateScratch.Length == 0 ? 64 : _connectionStateScratch.Length;
            while (newSize < required)
            {
                if (newSize > int.MaxValue / 2)
                {
                    newSize = required;
                    break;
                }

                newSize <<= 1;
            }

            _connectionStateScratch = new ConnectionState[newSize];
        }

        private void EnsureRecipientConnectionScratchCapacity(int required)
        {
            if (_recipientConnectionIdScratch.Length >= required)
                return;

            int newSize = _recipientConnectionIdScratch.Length == 0 ? 64 : _recipientConnectionIdScratch.Length;
            while (newSize < required)
            {
                if (newSize > int.MaxValue / 2)
                {
                    newSize = required;
                    break;
                }

                newSize <<= 1;
            }

            _recipientConnectionIdScratch = new int[newSize];
        }

        private void EnsureRecipientSelectionEntryCapacity(int required)
        {
            if (_recipientSelectionEntries.Length >= required)
                return;

            int newSize = _recipientSelectionEntries.Length == 0 ? 64 : _recipientSelectionEntries.Length;
            while (newSize < required)
            {
                if (newSize > int.MaxValue / 2)
                {
                    newSize = required;
                    break;
                }

                newSize <<= 1;
            }

            RecipientSelectionCacheEntry[] next = new RecipientSelectionCacheEntry[newSize];
            if (_recipientSelectionEntries.Length != 0)
                Array.Copy(_recipientSelectionEntries, next, _recipientSelectionEntries.Length);
            for (int i = _recipientSelectionEntries.Length; i < next.Length; i++)
            {
                next[i] = new RecipientSelectionCacheEntry();
            }
            _recipientSelectionEntries = next;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ComputeRecipientSelectionCacheKey(in EncodedReplicationDelta delta, bool assumeRecipientSelectorEntityScoped)
        {
            // Recipient selection is commonly entity-scoped in AoI selector paths.
            // When explicitly configured, always use entity+stream so replay-window
            // depth reuses the same selector result cache entry.
            if (!assumeRecipientSelectorEntityScoped)
            {
                ulong coalesceKey = delta.CoalesceKey;
                if (coalesceKey != 0)
                    return coalesceKey;
            }

            return ComputeEntityStreamCacheKey(delta.EntityId.Value, delta.StreamId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordFanoutReuseSample(
            ulong recipientSignature,
            int recipientCount,
            in EncodedReplicationDelta delta)
        {
            // Signature is build-local and keyed to recipient-group identity, not payload identity.
            // The same recipient group appearing across many deltas in the tick should count as reuse.
            ulong signature = recipientSignature;
            signature = (signature * 1099511628211UL) ^ (uint)delta.Lane;
            signature = (signature * 1099511628211UL) ^ (uint)recipientCount;

            if (_fanoutReuseSignatureSeen.Add(signature))
            {
                _stats.RecordFanoutReuseMiss();
                return;
            }

            long bytesSaved = 0;
            if (recipientCount > 1)
                bytesSaved = (long)(recipientCount - 1) * delta.Payload.Length;
            _stats.RecordFanoutReuseHit(bytesSaved);
        }

        // M2: interest cache key is strictly entity+stream scoped.
        // CoalesceKey and lane are excluded because this cache is only safe for
        // entity-scoped interest filters (for example, AoI/chunk visibility).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ComputeInterestCacheKey(in EncodedReplicationDelta delta)
        {
            return ComputeEntityStreamCacheKey(delta.EntityId.Value, delta.StreamId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ComputeEntityStreamCacheKey(ulong entityId, ushort streamId)
        {
            // Fast-path for 48-bit entity IDs (common): preserve a collision-free pack.
            if ((entityId >> 48) == 0)
            {
                ulong packed = (entityId << 16) | streamId;
                return packed == 0 ? 1UL : packed;
            }

            // Fallback for wider IDs: mix full 64-bit entity ID with stream ID.
            // This avoids truncation collisions from previous uint-based packing.
            ulong key = entityId ^ ((ulong)streamId * 0x9E3779B97F4A7C15UL);
            key ^= key >> 33;
            key *= 0xff51afd7ed558ccdUL;
            key ^= key >> 33;
            return key == 0 ? 1UL : key;
        }

        private void EnsureInterestCacheEntryCapacity(int required)
        {
            if (_interestCacheEntries.Length >= required)
                return;

            int newSize = _interestCacheEntries.Length == 0 ? 64 : _interestCacheEntries.Length;
            while (newSize < required)
            {
                if (newSize > int.MaxValue / 2)
                {
                    newSize = required;
                    break;
                }
                newSize <<= 1;
            }

            InterestCacheEntry[] next = new InterestCacheEntry[newSize];
            if (_interestCacheEntries.Length != 0)
                Array.Copy(_interestCacheEntries, next, _interestCacheEntries.Length);
            for (int i = _interestCacheEntries.Length; i < next.Length; i++)
                next[i] = new InterestCacheEntry();
            _interestCacheEntries = next;
        }

        // Returns (or creates) the InterestCacheEntry for cacheKey.
        // Entry slot state is generation-stamped, so no per-build array clear is required.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private InterestCacheEntry GetOrCreateInterestCacheEntryCached(ulong cacheKey, int connectionCount)
        {
            if (_hasLastInterestCacheEntry && _lastInterestCacheKey == cacheKey && _lastInterestCacheEntry is not null)
                return _lastInterestCacheEntry;

            InterestCacheEntry entry = GetOrCreateInterestCacheEntry(cacheKey, connectionCount);
            _hasLastInterestCacheEntry = true;
            _lastInterestCacheKey = cacheKey;
            _lastInterestCacheEntry = entry;
            return entry;
        }

        private InterestCacheEntry GetOrCreateInterestCacheEntry(ulong cacheKey, int connectionCount)
        {
            if (_interestCacheByKey.TryGetValue(cacheKey, out int entryIndex))
            {
                InterestCacheEntry existing = _interestCacheEntries[entryIndex];
                existing.EnsureSlotCapacity(connectionCount);
                Debug.Assert(existing.TrueGenerationBySlot.Length >= connectionCount &&
                             existing.EvalGenerationBySlot.Length >= connectionCount);
                _interestCacheEntries[entryIndex] = existing;
                return existing;
            }

            entryIndex = _interestCacheEntryCount++;
            EnsureInterestCacheEntryCapacity(entryIndex + 1);

            InterestCacheEntry entry = _interestCacheEntries[entryIndex];
            entry.EnsureSlotCapacity(connectionCount);
            Debug.Assert(entry.TrueGenerationBySlot.Length >= connectionCount &&
                         entry.EvalGenerationBySlot.Length >= connectionCount);

            _interestCacheByKey[cacheKey] = entryIndex;
            return entry;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetCachedInterest(InterestCacheEntry entry, int slot, out bool interested)
        {
            if ((uint)slot >= (uint)entry.EvalGenerationBySlot.Length ||
                entry.EvalGenerationBySlot[slot] != _interestCacheBuildGeneration)
            {
                interested = false;
                return false;
            }

            interested = entry.TrueGenerationBySlot[slot] == _interestCacheBuildGeneration;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetCachedInterest(InterestCacheEntry entry, int slot, bool interested)
        {
            entry.EvalGenerationBySlot[slot] = _interestCacheBuildGeneration;
            if (interested)
                entry.TrueGenerationBySlot[slot] = _interestCacheBuildGeneration;
        }

        private void ResetInterestCacheForGenerationWrap()
        {
            _interestCacheBuildGeneration = 1;
            _interestCacheByKey.Clear();
            _interestCacheEntryCount = 0;
            _hasLastInterestCacheEntry = false;
            _lastInterestCacheKey = 0;
            _lastInterestCacheEntry = null;

            for (int i = 0; i < _interestCacheEntries.Length; i++)
            {
                InterestCacheEntry entry = _interestCacheEntries[i];
                if (entry.EvalGenerationBySlot.Length != 0)
                    Array.Clear(entry.EvalGenerationBySlot, 0, entry.EvalGenerationBySlot.Length);
                if (entry.TrueGenerationBySlot.Length != 0)
                    Array.Clear(entry.TrueGenerationBySlot, 0, entry.TrueGenerationBySlot.Length);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TrimInterestCacheIfNeeded()
        {
            if (_interestCacheEntries.Length <= MaxInterestCacheEntries &&
                _interestCacheEntryCount <= MaxInterestCacheEntries)
                return;

            for (int i = 0; i < _interestCacheEntries.Length; i++)
                _interestCacheEntries[i].ReturnPooledArrays();

            _interestCacheEntries = Array.Empty<InterestCacheEntry>();
            _interestCacheByKey.Clear();
            _interestCacheEntryCount = 0;
            _hasLastInterestCacheEntry = false;
            _lastInterestCacheKey = 0;
            _lastInterestCacheEntry = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TrimRecipientSelectionCacheIfNeeded()
        {
            if (_recipientSelectionEntries.Length <= MaxRecipientSelectionCacheEntries)
                return;

            for (int i = 0; i < _recipientSelectionEntries.Length; i++)
                _recipientSelectionEntries[i].ReturnPooledArrays();

            _recipientSelectionEntries = Array.Empty<RecipientSelectionCacheEntry>();
            if (_recipientSelectionEntryByKey.Count != 0)
                _recipientSelectionEntryByKey.Clear();
            _recipientSelectionEntryCount = 0;
            _hasLastRecipientSelectionEntry = false;
            _lastRecipientSelectionKey = 0;
            _lastRecipientSelectionEntryIndex = -1;
        }

        private int FlushDeferred(
            int connectionId,
            ConnectionState state,
            ReplicationPacketSink sink,
            int maxDeferredPackets,
            int maxDeferredBytes,
            ref int deferredPacketsUsedGlobal,
            ref int deferredBytesUsedGlobal)
        {
            if (state.DeferredQueue.Count == 0 && state.CoalescedDeferred.Count == 0)
                return 0;

            int sent = 0;

            while (state.DeferredQueue.TryPeek(out DeferredDelta delta))
            {
                if (WasDeferredSentKeyThisBuild(state, delta.SentKeyHash))
                {
                    state.DeferredQueue.TryDequeue(out _);
                    DecrementLaneCount(state.DeferredByLane, delta.Lane);
                    continue;
                }

                if (maxDeferredPackets > 0 && (deferredPacketsUsedGlobal + 1) > maxDeferredPackets)
                    return sent;
                if (maxDeferredBytes > 0 && (deferredBytesUsedGlobal + delta.PayloadLength) > maxDeferredBytes)
                    return sent;

                if (!TrySendDeferred(connectionId, state, in delta, sink))
                    return sent;

                MarkDeferredSentKeyThisBuild(state, delta.SentKeyHash);
                state.DeferredQueue.TryDequeue(out _);
                DecrementLaneCount(state.DeferredByLane, delta.Lane);
                deferredPacketsUsedGlobal++;
                deferredBytesUsedGlobal += delta.PayloadLength;
                sent++;
            }

            if (state.CoalescedDeferred.Count == 0)
                return sent;

            while (state.CoalescedDeferredOrder.TryPeek(out ulong coalesceKey, out _))
            {
                ref DeferredDelta delta = ref CollectionsMarshal.GetValueRefOrNullRef(state.CoalescedDeferred, coalesceKey);
                if (Unsafe.IsNullRef(ref delta))
                {
                    state.CoalescedDeferredOrder.Dequeue();
                    continue;
                }

                if (WasDeferredSentKeyThisBuild(state, delta.SentKeyHash))
                {
                    state.CoalescedDeferredOrder.Dequeue();
                    state.CoalescedDeferred.Remove(coalesceKey);
                    DecrementLaneCount(state.CoalescedByLane, delta.Lane);
                    continue;
                }

                if (maxDeferredPackets > 0 && (deferredPacketsUsedGlobal + 1) > maxDeferredPackets)
                    break;
                if (maxDeferredBytes > 0 && (deferredBytesUsedGlobal + delta.PayloadLength) > maxDeferredBytes)
                    break;

                if (!TrySendDeferred(connectionId, state, in delta, sink))
                    break;

                MarkDeferredSentKeyThisBuild(state, delta.SentKeyHash);
                state.CoalescedDeferredOrder.Dequeue();
                state.CoalescedDeferred.Remove(coalesceKey);
                DecrementLaneCount(state.CoalescedByLane, delta.Lane);
                deferredPacketsUsedGlobal++;
                deferredBytesUsedGlobal += delta.PayloadLength;
                sent++;
            }

            return sent;
        }

        private int FlushDeferredTrackedConnections(
            ReplicationPacketSink sink,
            int maxDeferredPackets,
            int maxDeferredBytes)
        {
            int sent = 0;
            int write = 0;
            int count = _connectionsWithDeferred.Count;
            int deferredPacketsUsedGlobal = 0;
            int deferredBytesUsedGlobal = 0;

            for (int read = 0; read < count; read++)
            {
                ConnectionState state = _connectionsWithDeferred[read];
                sent += FlushDeferred(
                    state.ConnectionId,
                    state,
                    sink,
                    maxDeferredPackets,
                    maxDeferredBytes,
                    ref deferredPacketsUsedGlobal,
                    ref deferredBytesUsedGlobal);

                if (state.DeferredQueue.Count != 0 || state.CoalescedDeferred.Count != 0)
                {
                    state.DeferredWorkListIndex = write;
                    _connectionsWithDeferred[write++] = state;
                }
                else
                {
                    state.HasDeferredWork = false;
                    state.DeferredWorkListIndex = -1;
                    state.DeferredArena.Reset();
                }
            }

            if (write < count)
                _connectionsWithDeferred.RemoveRange(write, count - write);

            return sent;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeDeferredFlushCap(int budgetPerTick)
        {
            if (budgetPerTick <= 1)
                return budgetPerTick;

            // Keep roughly half of each per-connection budget for current-tick replication.
            return Math.Max(1, budgetPerTick >> 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MarkConnectionHasDeferredWork(ConnectionState state)
        {
            if (state.HasDeferredWork)
                return;

            state.HasDeferredWork = true;
            state.DeferredWorkListIndex = _connectionsWithDeferred.Count;
            _connectionsWithDeferred.Add(state);
        }

        private void RemoveConnectionDeferredWork(ConnectionState state)
        {
            state.HasDeferredWork = false;
            int index = state.DeferredWorkListIndex;
            state.DeferredWorkListIndex = -1;
            if ((uint)index >= (uint)_connectionsWithDeferred.Count)
                return;

            int last = _connectionsWithDeferred.Count - 1;
            ConnectionState lastState = _connectionsWithDeferred[last];
            _connectionsWithDeferred[index] = lastState;
            _connectionsWithDeferred.RemoveAt(last);
            if (index < last)
                lastState.DeferredWorkListIndex = index;
        }

        private bool TrySend(
            int connectionId,
            ConnectionState state,
            in EncodedReplicationDelta delta,
            ReplicationPacketSink sink)
        {
            _stats.RecordSendAttempt();
            int payloadBytes = delta.Payload.Length;
            if (!TryReserveBudget(state, payloadBytes, out int nextUsedPackets, out int nextUsedBytes))
                return false;

            sink(new ReplicationPacket(
                connectionId: connectionId,
                tick: delta.Tick,
                entityId: delta.EntityId,
                streamId: delta.StreamId,
                lane: delta.Lane,
                coalesceKey: delta.CoalesceKey,
                payload: delta.Payload));

            CommitReservedBudget(state, nextUsedPackets, nextUsedBytes);
            _stats.RecordSent(delta.Lane, payloadBytes);
            if (state.HasDeferredWork)
            {
                ulong sentKeyHash = delta.SentKeyHash != 0
                    ? delta.SentKeyHash
                    : ComputeDeferredSentKeyHash(
                        delta.Tick,
                        delta.EntityId,
                        delta.StreamId,
                        delta.Lane,
                        delta.CoalesceKey,
                        delta.Payload.Span);
                MarkDeferredSentKeyThisBuild(state, sentKeyHash);
            }
            return true;
        }

        private void ForwardPacketToBatch(in ReplicationPacket packet)
        {
            _batchPacketBuffer[_batchPacketCount++] = packet;
            if (_batchPacketCount >= _batchPacketLimit)
                FlushPacketBatch();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushPacketBatch()
        {
            ReplicationPacketBatchSink? sink = _activeBatchSink;
            if (sink == null || _batchPacketCount == 0)
                return;

            sink(_batchPacketBuffer, _batchPacketCount);
            _batchPacketCount = 0;
        }

        private void EnsureBatchPacketCapacity(int required)
        {
            if (_batchPacketBuffer.Length >= required)
                return;

            int newSize = _batchPacketBuffer.Length == 0 ? 64 : _batchPacketBuffer.Length;
            while (newSize < required)
            {
                if (newSize > int.MaxValue / 2)
                {
                    newSize = required;
                    break;
                }

                newSize <<= 1;
            }

            _batchPacketBuffer = new ReplicationPacket[newSize];
        }

        private bool TrySendDeferred(
            int connectionId,
            ConnectionState state,
            in DeferredDelta delta,
            ReplicationPacketSink sink)
        {
            _stats.RecordSendAttempt();
            int payloadBytes = delta.PayloadLength;
            if (!TryReserveBudget(state, payloadBytes, out int nextUsedPackets, out int nextUsedBytes))
                return false;

            sink(new ReplicationPacket(
                connectionId: connectionId,
                tick: delta.Tick,
                entityId: delta.EntityId,
                streamId: delta.StreamId,
                lane: delta.Lane,
                coalesceKey: delta.CoalesceKey,
                payload: new ReadOnlyMemory<byte>(delta.PayloadBuffer, delta.PayloadOffset, payloadBytes)));

            CommitReservedBudget(state, nextUsedPackets, nextUsedBytes);
            _stats.RecordSent(delta.Lane, payloadBytes);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryReserveBudget(
            ConnectionState state,
            int payloadBytes,
            out int nextUsedPackets,
            out int nextUsedBytes)
        {
            nextUsedPackets = state.UsedPackets;
            nextUsedBytes = state.UsedBytes;
            if (state.IsBudgetExhausted)
                return false;

            nextUsedPackets = state.UsedPackets + 1;
            if (nextUsedPackets > _budget.MaxPacketsPerTick)
            {
                state.IsBudgetExhausted = true;
                return false;
            }

            if (payloadBytes > _budget.MaxBytesPerTick - state.UsedBytes)
            {
                state.IsBudgetExhausted = true;
                return false;
            }

            nextUsedBytes = state.UsedBytes + payloadBytes;
            if (nextUsedBytes > _budget.MaxBytesPerTick)
            {
                state.IsBudgetExhausted = true;
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CommitReservedBudget(ConnectionState state, int nextUsedPackets, int nextUsedBytes)
        {
            state.UsedPackets = nextUsedPackets;
            state.UsedBytes = nextUsedBytes;
        }

        private void HandleOverflow(ConnectionState state, in EncodedReplicationDelta delta)
        {
            switch (_overflowPolicy.GetAction(delta.Lane))
            {
                case ReplicationOverflowAction.Drop:
                    _stats.RecordDropped(delta.Lane);
                    return;
                case ReplicationOverflowAction.Defer:
                    if (state.DeferredQueue.Count >= _budget.MaxDeferredPackets ||
                        state.DeferredQueue.Count >= state.DeferredQueue.Capacity)
                    {
                        _stats.RecordDropped(delta.Lane);
                        return;
                    }

                    DeferredDelta deferredDelta = CloneDeltaForDeferred(state, in delta);
                    state.DeferredQueue.EnqueueAssumeCapacity(in deferredDelta);
                    MarkConnectionHasDeferredWork(state);
                    IncrementLaneCount(state.DeferredByLane, delta.Lane);
                    _stats.RecordDeferred(delta.Lane);
                    return;
                case ReplicationOverflowAction.Coalesce:
                    if (state.PendingCoalesced.Count >= _budget.MaxDeferredPackets &&
                        !state.PendingCoalesced.ContainsKey(delta.CoalesceKey))
                    {
                        _stats.RecordDropped(delta.Lane);
                        return;
                    }

                    ref PendingCoalescedDelta pending = ref CollectionsMarshal.GetValueRefOrAddDefault(
                        state.PendingCoalesced,
                        delta.CoalesceKey,
                        out _);
                    CopyPayloadToDeferredArena(
                        state,
                        delta.Payload.Span,
                        out byte[] payloadBuffer,
                        out int payloadOffset,
                        out int payloadLength);
                    pending = new PendingCoalescedDelta(
                        tick: delta.Tick,
                        entityId: delta.EntityId,
                        streamId: delta.StreamId,
                        lane: delta.Lane,
                        coalesceKey: delta.CoalesceKey,
                        payloadBuffer: payloadBuffer,
                        payloadOffset: payloadOffset,
                        payloadLength: payloadLength);
                    _stats.RecordCoalesced(delta.Lane);
                    return;
                default:
                    _stats.RecordDropped(delta.Lane);
                    return;
            }
        }

        private void CommitPendingCoalesced(ConnectionState state)
        {
            foreach ((ulong coalesceKey, PendingCoalescedDelta pending) in state.PendingCoalesced)
            {
                ref DeferredDelta slot = ref CollectionsMarshal.GetValueRefOrAddDefault(
                    state.CoalescedDeferred,
                    coalesceKey,
                    out bool hadExisting);

                ReadOnlySpan<byte> payload = pending.PayloadBuffer.AsSpan(
                    pending.PayloadOffset,
                    pending.PayloadLength);
                if (hadExisting && slot.PayloadCapacity >= payload.Length)
                {
                    if (slot.Lane != pending.Lane)
                    {
                        DecrementLaneCount(state.CoalescedByLane, slot.Lane);
                        IncrementLaneCount(state.CoalescedByLane, pending.Lane);
                    }
                    payload.CopyTo(slot.PayloadBuffer.AsSpan(slot.PayloadOffset, payload.Length));
                    slot.Tick = pending.Tick;
                    slot.EntityId = pending.EntityId;
                    slot.StreamId = pending.StreamId;
                    slot.Lane = pending.Lane;
                    slot.CoalesceKey = pending.CoalesceKey;
                    slot.PayloadLength = payload.Length;
                    slot.SentKeyHash = ComputeDeferredSentKeyHash(
                        pending.Tick,
                        pending.EntityId,
                        pending.StreamId,
                        pending.Lane,
                        pending.CoalesceKey,
                        slot.PayloadBuffer.AsSpan(slot.PayloadOffset, payload.Length));
                    continue;
                }

                if (hadExisting && slot.Lane != pending.Lane)
                {
                    DecrementLaneCount(state.CoalescedByLane, slot.Lane);
                    IncrementLaneCount(state.CoalescedByLane, pending.Lane);
                }
                else if (!hadExisting)
                {
                    IncrementLaneCount(state.CoalescedByLane, pending.Lane);
                }

                slot = ClonePendingForDeferred(state, in pending);
                if (!hadExisting)
                    state.CoalescedDeferredOrder.Enqueue(coalesceKey, state.NextCoalescedOrder++);
            }

            if (state.CoalescedDeferred.Count != 0)
                MarkConnectionHasDeferredWork(state);
            state.PendingCoalesced.Clear();
        }

        private DeferredDelta CloneDeltaForDeferred(ConnectionState state, in EncodedReplicationDelta delta)
        {
            CopyPayloadToDeferredArena(
                state,
                delta.Payload.Span,
                out byte[] payloadBuffer,
                out int payloadOffset,
                out int payloadLength);
            return new DeferredDelta(
                tick: delta.Tick,
                entityId: delta.EntityId,
                streamId: delta.StreamId,
                lane: delta.Lane,
                coalesceKey: delta.CoalesceKey,
                sentKeyHash: ComputeDeferredSentKeyHash(
                    delta.Tick,
                    delta.EntityId,
                    delta.StreamId,
                    delta.Lane,
                    delta.CoalesceKey,
                    delta.Payload.Span),
                payloadBuffer: payloadBuffer,
                payloadOffset: payloadOffset,
                payloadLength: payloadLength);
        }

        private DeferredDelta ClonePendingForDeferred(ConnectionState state, in PendingCoalescedDelta pending)
        {
            ReadOnlySpan<byte> payload = pending.PayloadBuffer.AsSpan(pending.PayloadOffset, pending.PayloadLength);
            CopyPayloadToDeferredArena(
                state,
                payload,
                out byte[] payloadBuffer,
                out int payloadOffset,
                out int payloadLength);
            return new DeferredDelta(
                tick: pending.Tick,
                entityId: pending.EntityId,
                streamId: pending.StreamId,
                lane: pending.Lane,
                coalesceKey: pending.CoalesceKey,
                sentKeyHash: ComputeDeferredSentKeyHash(
                    pending.Tick,
                    pending.EntityId,
                    pending.StreamId,
                    pending.Lane,
                    pending.CoalesceKey,
                    payload),
                payloadBuffer: payloadBuffer,
                payloadOffset: payloadOffset,
                payloadLength: payloadLength);
        }

        private static void CopyPayloadToDeferredArena(
            ConnectionState state,
            ReadOnlySpan<byte> payload,
            out byte[] payloadBuffer,
            out int payloadOffset,
            out int payloadLength)
        {
            payloadLength = payload.Length;
            if (payloadLength == 0)
            {
                payloadBuffer = Array.Empty<byte>();
                payloadOffset = 0;
                return;
            }

            state.DeferredArena.Allocate(payloadLength, out payloadBuffer, out payloadOffset);
            payload.CopyTo(payloadBuffer.AsSpan(payloadOffset, payloadLength));
        }

        private static void IncrementLaneCount(int[] laneCounts, ReplicationLane lane)
        {
            int index = (int)lane;
            if ((uint)index >= (uint)laneCounts.Length)
                return;

            laneCounts[index]++;
        }

        private static void DecrementLaneCount(int[] laneCounts, ReplicationLane lane)
        {
            int index = (int)lane;
            if ((uint)index >= (uint)laneCounts.Length)
                return;

            int value = laneCounts[index] - 1;
            laneCounts[index] = value < 0 ? 0 : value;
        }

        private static bool TryGetOldestDeferredTick(ConnectionState state, out long oldestTick)
        {
            long minTick = long.MaxValue;

            if (state.DeferredQueue.TryGetMinTick(out long queueMinTick))
                minTick = queueMinTick;

            foreach ((ulong _, DeferredDelta deferred) in state.CoalescedDeferred)
            {
                if (deferred.Tick < minTick)
                    minTick = deferred.Tick;
            }

            if (minTick == long.MaxValue)
            {
                oldestTick = 0;
                return false;
            }

            oldestTick = minTick;
            return true;
        }

        private static bool WasDeferredSentThisBuild(
            ConnectionState state,
            in EncodedReplicationDelta delta,
            ref bool hasDeferredSentKeyHash,
            ref ulong deferredSentKeyHash)
        {
            if (state.SentDeferredCountThisBuild == 0)
                return false;

            if (!hasDeferredSentKeyHash)
            {
                deferredSentKeyHash = delta.SentKeyHash != 0
                    ? delta.SentKeyHash
                    : ComputeDeferredSentKeyHash(
                        delta.Tick,
                        delta.EntityId,
                        delta.StreamId,
                        delta.Lane,
                        delta.CoalesceKey,
                        delta.Payload.Span);
                hasDeferredSentKeyHash = true;
            }

            return WasDeferredSentKeyThisBuild(state, deferredSentKeyHash);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool WasDeferredSentKeyThisBuild(ConnectionState state, ulong sentKeyHash)
        {
            if (state.SentDeferredCountThisBuild == 0)
                return false;

            return state.SentDeferredKeyGenerationByHash.TryGetValue(sentKeyHash, out int generation) &&
                generation == state.SentDeferredGeneration;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MarkDeferredSentKeyThisBuild(ConnectionState state, ulong sentKeyHash)
        {
            ref int generation = ref CollectionsMarshal.GetValueRefOrAddDefault(
                state.SentDeferredKeyGenerationByHash,
                sentKeyHash,
                out bool exists);
            if (exists && generation == state.SentDeferredGeneration)
                return;

            generation = state.SentDeferredGeneration;
            state.SentDeferredCountThisBuild++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BeginSentDeferredGeneration(ConnectionState state)
        {
            if (state.SentDeferredGeneration == int.MaxValue ||
                state.SentDeferredKeyGenerationByHash.Count > MaxSentDeferredKeyGenerationEntries)
            {
                if (state.SentDeferredKeyGenerationByHash.Count != 0)
                    state.SentDeferredKeyGenerationByHash.Clear();
                state.SentDeferredGeneration = 1;
            }
            else
            {
                state.SentDeferredGeneration++;
            }

            state.SentDeferredCountThisBuild = 0;
        }

        private static ulong ComputeDeferredSentKeyHash(
            long tick,
            NetworkEntityId entityId,
            ushort streamId,
            ReplicationLane lane,
            ulong coalesceKey,
            ReadOnlySpan<byte> payload) =>
            ReplicationDeltaSentKey.Compute(tick, entityId, streamId, lane, coalesceKey, payload);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReportMissingGroup(uint groupId, int denseConnectionGroupCount)
        {
            if (_reportedMissingGroupThisBuild)
                return;

            _reportedMissingGroupThisBuild = true;
            Debug.WriteLine(
                $"Group selector returned groupId={groupId} not found in any connection group (DenseCount={denseConnectionGroupCount}); delta silently dropped. Check group selector configuration.");
        }

        private ConnectionState RentConnectionState(int connectionId, uint groupId, long lastAckedTick)
        {
            if (!_connectionPool.TryPop(out ConnectionState? state))
                state = new ConnectionState(_budget.MaxDeferredPackets, _deferredArenaSlabBytes);

            state.Reset(connectionId, groupId, lastAckedTick);
            return state;
        }

        private void ReturnConnectionState(ConnectionState state)
        {
            state.Reset(connectionId: 0, groupId: 0, lastAckedTick: 0);
            _connectionPool.Push(state);
        }

        private void AddConnectionToGroup(ConnectionState state)
        {
            if (!_connectionsByGroup.TryGetValue(state.GroupId, out List<ConnectionState>? members))
            {
                members = new List<ConnectionState>(8);
                _connectionsByGroup.Add(state.GroupId, members);
                SetDenseGroupEntry(state.GroupId, members);
            }

            int insertAt = members.BinarySearch(state, GroupConnectionComparer);
            if (insertAt < 0)
                insertAt = ~insertAt;

            members.Insert(insertAt, state);
        }

        private void RemoveConnectionFromGroup(ConnectionState state)
        {
            if (!_connectionsByGroup.TryGetValue(state.GroupId, out List<ConnectionState>? members))
                return;

            int index = members.BinarySearch(state, GroupConnectionComparer);
            if (index >= 0 && ReferenceEquals(members[index], state))
                members.RemoveAt(index);
            else
                members.Remove(state);

            if (members.Count == 0)
            {
                _connectionsByGroup.Remove(state.GroupId);
                SetDenseGroupEntry(state.GroupId, null);
            }
        }

        private void SetDenseGroupEntry(uint groupId, List<ConnectionState>? members)
        {
            if (groupId > DenseGroupLookupMaxGroupId)
                return;

            EnsureDenseGroupCapacity((int)groupId + 1);
            _denseConnectionGroups[(int)groupId] = members;
        }

        private void EnsureDenseGroupCapacity(int required)
        {
            if (_denseConnectionGroups.Length >= required)
                return;

            int newSize = _denseConnectionGroups.Length == 0 ? 16 : _denseConnectionGroups.Length;
            while (newSize < required)
            {
                if (newSize > int.MaxValue / 2)
                {
                    newSize = required;
                    break;
                }

                newSize <<= 1;
            }

            Array.Resize(ref _denseConnectionGroups, newSize);
        }

        private sealed class ConnectionStateIdComparer : IComparer<ConnectionState>
        {
            public int Compare(ConnectionState? x, ConnectionState? y)
            {
                if (ReferenceEquals(x, y))
                    return 0;
                if (x is null)
                    return -1;
                if (y is null)
                    return 1;
                return x.ConnectionId.CompareTo(y.ConnectionId);
            }
        }

        private static int CompareCandidates(ReplicationCandidate left, ReplicationCandidate right)
        {
            int tickCompare = left.Tick.CompareTo(right.Tick);
            if (tickCompare != 0)
                return tickCompare;

            int laneCompare = left.Lane.CompareTo(right.Lane);
            if (laneCompare != 0)
                return laneCompare;

            int entityCompare = left.EntityId.CompareTo(right.EntityId);
            if (entityCompare != 0)
                return entityCompare;

            int streamCompare = left.StreamId.CompareTo(right.StreamId);
            if (streamCompare != 0)
                return streamCompare;

            return left.CoalesceKey.CompareTo(right.CoalesceKey);
        }

        public void Dispose()
        {
            for (int i = 0; i < _connectionStates.Count; i++)
            {
                ConnectionState state = _connectionStates[i];
                state.Reset(connectionId: 0, groupId: 0, lastAckedTick: 0);
            }

            _connections.Clear();
            _connectionStates.Clear();
            _connectionStateIndexById.Clear();
            _connectionsByGroup.Clear();
            _connectionsWithDeferred.Clear();
            for (int i = 0; i < _recipientSelectionEntries.Length; i++)
                _recipientSelectionEntries[i].ReturnPooledArrays();
            _recipientSelectionEntryByKey.Clear();
            _recipientSelectionEntryCount = 0;
            _hasLastRecipientSelectionEntry = false;
            _lastRecipientSelectionKey = 0;
            _lastRecipientSelectionEntryIndex = -1;
            for (int i = 0; i < _interestCacheEntries.Length; i++)
                _interestCacheEntries[i].ReturnPooledArrays();
            _interestCacheByKey.Clear();
            _interestCacheEntryCount = 0;
            _hasLastInterestCacheEntry = false;
            _lastInterestCacheKey = 0;
            _lastInterestCacheEntry = null;
            _activeBatchSink = null;
            _batchPacketCount = 0;
            _batchPacketLimit = 0;

            while (_connectionPool.Count > 0)
            {
                ConnectionState state = _connectionPool.Pop();
                state.Reset(connectionId: 0, groupId: 0, lastAckedTick: 0);
            }

            _replayWindow.Dispose();
        }

    }
}
