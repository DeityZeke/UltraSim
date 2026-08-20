using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UltraSim.Network
{
    public sealed class ClientPredictionStore<TState>
    {
        private PredictedStateSample<TState>[] _buffer;
        private PredictedStateSample<TState>[]? _copyNewerThanScratch;
        private int _head;
        private int _count;
        private readonly Dictionary<ushort, int> _indexBySequence;

        public ClientPredictionStore(int initialCapacity = 16)
        {
            if (initialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            _buffer = new PredictedStateSample<TState>[initialCapacity];
            _indexBySequence = new Dictionary<ushort, int>(initialCapacity);
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _count;
        }

        public void Record(ushort sequence, long clientTick, in TState state)
        {
            if (_indexBySequence.ContainsKey(sequence))
                RemoveSequenceFromFront(sequence);

            EnsureCapacityForOneMore();
            var sample = new PredictedStateSample<TState>(sequence, clientTick, in state);
            int tailIndex = GetPhysicalIndex(_count);
            _buffer[tailIndex] = sample;
            _indexBySequence[sequence] = tailIndex;
            _count++;
        }

        public bool TryGet(ushort sequence, out PredictedStateSample<TState> sample) =>
            TryGetBySequence(sequence, out sample);

        public int RemoveAcknowledgedUpTo(ushort acknowledgedSequence)
        {
            int removed = 0;
            while (_count > 0)
            {
                PredictedStateSample<TState> sample = _buffer[_head];
                bool acknowledged = sample.Sequence == acknowledgedSequence ||
                    SequenceMath.IsNewer(acknowledgedSequence, sample.Sequence);
                if (!acknowledged)
                    break;

                RemoveFront();
                removed++;
            }

            return removed;
        }

        public int CopyRemainingTo(Span<PredictedStateSample<TState>> destination)
        {
            int count = 0;
            int limit = Math.Min(_count, destination.Length);
            while (count < limit)
            {
                destination[count] = _buffer[GetPhysicalIndex(count)];
                count++;
            }

            return count;
        }

        public int CopyNewerThan(ushort sequence, Span<PredictedStateSample<TState>> destination)
        {
            if (destination.Length == 0)
                return 0;

            int copied = 0;
            int writeIndex = 0;
            bool overwrote = false;
            for (int i = 0; i < _count; i++)
            {
                PredictedStateSample<TState> sample = _buffer[GetPhysicalIndex(i)];
                if (!SequenceMath.IsNewer(sample.Sequence, sequence))
                    continue;

                if (copied < destination.Length)
                {
                    destination[copied++] = sample;
                    continue;
                }

                overwrote = true;
                destination[writeIndex++] = sample;
                if (writeIndex == destination.Length)
                    writeIndex = 0;
            }

            if (!overwrote)
                return copied;

            PredictedStateSample<TState>[] scratchArray = _copyNewerThanScratch ?? Array.Empty<PredictedStateSample<TState>>();
            if (scratchArray.Length < destination.Length)
            {
                scratchArray = new PredictedStateSample<TState>[destination.Length];
                _copyNewerThanScratch = scratchArray;
            }

            Span<PredictedStateSample<TState>> scratch = scratchArray.AsSpan(0, destination.Length);
            destination.CopyTo(scratch);
            scratch[writeIndex..].CopyTo(destination);
            scratch[..writeIndex].CopyTo(destination[(destination.Length - writeIndex)..]);
            return destination.Length;
        }

        public void Clear()
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<PredictedStateSample<TState>>() && _count > 0)
            {
                for (int i = 0; i < _count; i++)
                    _buffer[GetPhysicalIndex(i)] = default;
            }

            _head = 0;
            _count = 0;
            _indexBySequence.Clear();
        }

        private void RemoveSequenceFromFront(ushort sequence)
        {
            if (!_indexBySequence.TryGetValue(sequence, out int physicalIndex))
                return;

            int logicalIndex = physicalIndex >= _head
                ? physicalIndex - _head
                : physicalIndex + _buffer.Length - _head;
            if ((uint)logicalIndex >= (uint)_count)
                return;

            for (int i = logicalIndex; i < _count - 1; i++)
            {
                int sourceIndex = GetPhysicalIndex(i + 1);
                int destinationIndex = GetPhysicalIndex(i);
                PredictedStateSample<TState> moved = _buffer[sourceIndex];
                _buffer[destinationIndex] = moved;
                _indexBySequence[moved.Sequence] = destinationIndex;
            }

            _indexBySequence.Remove(sequence);
            int tailIndex = GetPhysicalIndex(_count - 1);
            if (RuntimeHelpers.IsReferenceOrContainsReferences<PredictedStateSample<TState>>())
                _buffer[tailIndex] = default;
            _count--;
            if (_count == 0)
                _head = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetBySequence(ushort sequence, out PredictedStateSample<TState> sample)
        {
            if (_indexBySequence.TryGetValue(sequence, out int index))
            {
                sample = _buffer[index];
                return true;
            }

            sample = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetPhysicalIndex(int logicalIndex)
        {
            int index = _head + logicalIndex;
            if (index >= _buffer.Length)
                index -= _buffer.Length;
            return index;
        }

        private void EnsureCapacityForOneMore()
        {
            if (_count < _buffer.Length)
                return;

            int newSize = _buffer.Length * 2;
            if (newSize < 4)
                newSize = 4;

            var newBuffer = new PredictedStateSample<TState>[newSize];
            for (int i = 0; i < _count; i++)
                newBuffer[i] = _buffer[GetPhysicalIndex(i)];

            _buffer = newBuffer;
            _head = 0;

            _indexBySequence.Clear();
            for (int i = 0; i < _count; i++)
                _indexBySequence[_buffer[i].Sequence] = i;
        }

        private void RemoveFront()
        {
            PredictedStateSample<TState> sample = _buffer[_head];
            _indexBySequence.Remove(sample.Sequence);
            if (RuntimeHelpers.IsReferenceOrContainsReferences<PredictedStateSample<TState>>())
                _buffer[_head] = default;

            _head++;
            if (_head == _buffer.Length)
                _head = 0;
            _count--;

            if (_count == 0)
                _head = 0;
        }
    }
}
