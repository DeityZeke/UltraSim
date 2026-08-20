using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UltraSim.Network
{
    public sealed class ClientReconciliationQueue<TCorrection>
    {
        private static readonly bool s_clearEntries =
            RuntimeHelpers.IsReferenceOrContainsReferences<ReconciliationEntry<TCorrection>>();

        private readonly bool _threadSafe;
        private readonly object _sync = new object();
        private ReconciliationEntry<TCorrection>[] _writeBuffer;
        private int _writeCount;
        private ReconciliationEntry<TCorrection>[] _drainBuffer;
        private int _drainCount;
        private int _drainIndex;
        private int _drainInProgress;
        private Dictionary<ulong, int>? _writeIndexByMergeKey;
        private Dictionary<ulong, int>? _drainIndexByMergeKey;

        public ClientReconciliationQueue(int initialCapacity = 64, bool threadSafe = true)
        {
            if (initialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            _threadSafe = threadSafe;
            _writeBuffer = new ReconciliationEntry<TCorrection>[initialCapacity];
            _drainBuffer = new ReconciliationEntry<TCorrection>[initialCapacity];
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (!_threadSafe)
                    return _writeCount + (_drainCount - _drainIndex);

                lock (_sync)
                    return _writeCount + (_drainCount - _drainIndex);
            }
        }

        public void Enqueue(ushort authoritativeSequence, long serverTick, long queuedClientTick, in TCorrection correction)
        {
            if (!_threadSafe)
            {
                EnqueueNoLock(authoritativeSequence, serverTick, queuedClientTick, in correction);
                return;
            }

            lock (_sync)
            {
                EnqueueNoLock(authoritativeSequence, serverTick, queuedClientTick, in correction);
            }
        }

        public int Drain(ReconciliationHandler<TCorrection> handler)
        {
            return Drain(handler, int.MaxValue);
        }

        public int Drain(ReconciliationHandler<TCorrection> handler, int maxCount)
        {
            ArgumentNullException.ThrowIfNull(handler);
            if (maxCount <= 0)
                return 0;

            if (!_threadSafe)
                return DrainNoLock(handler, maxCount);

            if (Interlocked.Exchange(ref _drainInProgress, 1) != 0)
            {
                Debug.Assert(false, "ClientReconciliationQueue.Drain() called concurrently; queued corrections will be deferred.");
                return 0;
            }

            try
            {
                int count;
                lock (_sync)
                {
                    if (_drainIndex >= _drainCount && _writeCount == 0)
                        return 0;

                    if (_drainIndex >= _drainCount)
                        Swap_NoLock();

                    count = _drainCount - _drainIndex;
                    if (count > maxCount)
                        count = maxCount;
                }

                return DrainCore(handler, count);
            }
            finally
            {
                Volatile.Write(ref _drainInProgress, 0);
            }
        }

        public bool EnqueueOrReplace(
            ulong mergeKey,
            ushort authoritativeSequence,
            long serverTick,
            long queuedClientTick,
            in TCorrection correction)
        {
            if (!_threadSafe)
                return EnqueueOrReplaceNoLock(mergeKey, authoritativeSequence, serverTick, queuedClientTick, in correction);

            lock (_sync)
            {
                return EnqueueOrReplaceNoLock(mergeKey, authoritativeSequence, serverTick, queuedClientTick, in correction);
            }
        }

        public void Clear()
        {
            if (!_threadSafe)
            {
                ClearNoLock();
                return;
            }

            while (Volatile.Read(ref _drainInProgress) != 0)
                Thread.Yield();

            lock (_sync)
            {
                ClearNoLock();
            }
        }

        private void EnqueueNoLock(ushort authoritativeSequence, long serverTick, long queuedClientTick, in TCorrection correction)
        {
            if (_writeCount == _writeBuffer.Length)
                GrowWrite_NoLock();

            _writeBuffer[_writeCount++] = new ReconciliationEntry<TCorrection>(
                authoritativeSequence,
                serverTick,
                queuedClientTick,
                in correction);
        }

        private bool EnqueueOrReplaceNoLock(
            ulong mergeKey,
            ushort authoritativeSequence,
            long serverTick,
            long queuedClientTick,
            in TCorrection correction)
        {
            Dictionary<ulong, int> writeIndexByMergeKey = _writeIndexByMergeKey ??= new Dictionary<ulong, int>();
            if (writeIndexByMergeKey.TryGetValue(mergeKey, out int existingIndex))
            {
                _writeBuffer[existingIndex] = new ReconciliationEntry<TCorrection>(
                    authoritativeSequence,
                    serverTick,
                    queuedClientTick,
                    mergeKey,
                    hasMergeKey: true,
                    in correction);
                return true;
            }

            if (Volatile.Read(ref _drainInProgress) == 0 &&
                _drainIndexByMergeKey != null &&
                _drainIndexByMergeKey.TryGetValue(mergeKey, out existingIndex) &&
                (uint)existingIndex < (uint)_drainCount &&
                existingIndex >= _drainIndex)
            {
                _drainBuffer[existingIndex] = new ReconciliationEntry<TCorrection>(
                    authoritativeSequence,
                    serverTick,
                    queuedClientTick,
                    mergeKey,
                    hasMergeKey: true,
                    in correction);
                return true;
            }

            if (_writeCount == _writeBuffer.Length)
                GrowWrite_NoLock();

            int index = _writeCount++;
            _writeBuffer[index] = new ReconciliationEntry<TCorrection>(
                authoritativeSequence,
                serverTick,
                queuedClientTick,
                mergeKey,
                hasMergeKey: true,
                in correction);
            writeIndexByMergeKey[mergeKey] = index;
            return false;
        }

        private int DrainNoLock(ReconciliationHandler<TCorrection> handler, int maxCount)
        {
            if (_drainIndex >= _drainCount && _writeCount == 0)
                return 0;

            if (_drainIndex >= _drainCount)
                Swap_NoLock();

            int count = _drainCount - _drainIndex;
            if (count > maxCount)
                count = maxCount;
            return DrainCore(handler, count);
        }

        private int DrainCore(ReconciliationHandler<TCorrection> handler, int count)
        {
            int drained = 0;
            int clearStart = _drainIndex;
            while (drained < count)
            {
                int index = _drainIndex;
                if (_drainIndexByMergeKey != null)
                    RemoveDrainMergeKeyAtIndex_NoLock(index);
                try
                {
                    handler(in _drainBuffer[index]);
                }
                catch
                {
                    if (s_clearEntries && drained > 0)
                        Array.Clear(_drainBuffer, clearStart, drained);
                    throw;
                }
                _drainIndex++;
                drained++;
            }

            if (s_clearEntries && drained > 0)
                Array.Clear(_drainBuffer, clearStart, drained);

            if (_drainIndex >= _drainCount)
            {
                _drainCount = 0;
                _drainIndex = 0;
                _drainIndexByMergeKey?.Clear();
            }

            return drained;
        }

        private void ClearNoLock()
        {
            if (s_clearEntries && _writeCount > 0)
                Array.Clear(_writeBuffer, 0, _writeCount);
            if (s_clearEntries && _drainCount > 0)
                Array.Clear(_drainBuffer, 0, _drainCount);
            _writeIndexByMergeKey?.Clear();
            _drainIndexByMergeKey?.Clear();
            _writeCount = 0;
            _drainCount = 0;
            _drainIndex = 0;
        }

        private void GrowWrite_NoLock()
        {
            int newSize = _writeBuffer.Length * 2;
            if (newSize < 4)
                newSize = 4;

            var newBuffer = new ReconciliationEntry<TCorrection>[newSize];
            Array.Copy(_writeBuffer, 0, newBuffer, 0, _writeCount);
            _writeBuffer = newBuffer;
        }

        private void Swap_NoLock()
        {
            var temp = _drainBuffer;
            _drainBuffer = _writeBuffer;
            _drainCount = _writeCount;
            _drainIndex = 0;
            var tempIndex = _drainIndexByMergeKey;
            _drainIndexByMergeKey = _writeIndexByMergeKey;
            _writeBuffer = temp;
            _writeIndexByMergeKey = tempIndex;
            _writeCount = 0;
            _writeIndexByMergeKey?.Clear();
        }

        private void RemoveDrainMergeKeyAtIndex_NoLock(int index)
        {
            if (_drainIndexByMergeKey == null || _drainIndexByMergeKey.Count == 0)
                return;

            ref readonly ReconciliationEntry<TCorrection> entry = ref _drainBuffer[index];
            if (entry.HasMergeKey)
                _drainIndexByMergeKey.Remove(entry.MergeKey);
        }
    }
}
