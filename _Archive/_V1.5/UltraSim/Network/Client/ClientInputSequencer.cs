using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace UltraSim.Network
{
    public sealed class ClientInputSequencer<TInput>
    {
        private ClientInputSample<TInput>[] _pending;
        private int _head;
        private int _count;
        private ushort _nextSequence;
        private bool _hasLastAck;
        private ushort _lastAckSequence;
        private int _backwardAckDrops;

        public ClientInputSequencer(ushort initialSequence = 0, int initialCapacity = 16)
        {
            if (initialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            _pending = new ClientInputSample<TInput>[initialCapacity];
            _nextSequence = initialSequence;
        }

        /// <summary>Number of acks silently dropped because the acknowledged sequence was not newer than the last ack.
        /// Non-zero under normal packet reordering; persistently high values indicate a sequencing bug.</summary>
        public int BackwardAckDrops
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _backwardAckDrops;
        }

        public int PendingCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _count;
        }

        public ushort NextSequence
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _nextSequence;
        }

        public bool HasLastAcknowledgedSequence
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _hasLastAck;
        }

        public ushort LastAcknowledgedSequence
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _lastAckSequence;
        }

        public ClientInputSample<TInput> EnqueueInput(in TInput input, long clientTick)
        {
            ushort sequence = _nextSequence++;
            var sample = new ClientInputSample<TInput>(sequence, clientTick, in input);
            EnsureCapacityForOneMore();
            _pending[PhysicalIndex(_count)] = sample;
            _count++;
            return sample;
        }

        public int AcknowledgeUpTo(ushort acknowledgedSequence)
        {
            if (_count == 0)
            {
                if (_hasLastAck &&
                    acknowledgedSequence != _lastAckSequence &&
                    !SequenceMath.IsNewer(acknowledgedSequence, _lastAckSequence))
                {
                    _backwardAckDrops++;
                    Debug.WriteLine($"[ClientInputSequencer] Backward ack dropped: ack={acknowledgedSequence} last={_lastAckSequence}");
                    return 0;
                }

                _hasLastAck = true;
                _lastAckSequence = acknowledgedSequence;
                return 0;
            }

            if (_hasLastAck && !SequenceMath.IsNewer(acknowledgedSequence, _lastAckSequence) && acknowledgedSequence != _lastAckSequence)
            {
                _backwardAckDrops++;
                Debug.WriteLine($"[ClientInputSequencer] Backward ack dropped: ack={acknowledgedSequence} last={_lastAckSequence} pending={_count}");
                return 0;
            }

            int removed = 0;
            while (_count > 0)
            {
                ClientInputSample<TInput> head = _pending[_head];
                if (!IsAcknowledged(head.Sequence, acknowledgedSequence))
                    break;

                RemoveFront();
                removed++;
            }

            _hasLastAck = true;
            _lastAckSequence = acknowledgedSequence;
            return removed;
        }

        public int CopyPendingTo(Span<ClientInputSample<TInput>> destination)
        {
            int count = 0;
            int limit = Math.Min(_count, destination.Length);
            while (count < limit)
            {
                destination[count] = _pending[PhysicalIndex(count)];
                count++;
            }

            return count;
        }

        public void ClearPending()
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<ClientInputSample<TInput>>() && _count > 0)
            {
                for (int i = 0; i < _count; i++)
                    _pending[PhysicalIndex(i)] = default;
            }

            _head = 0;
            _count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int PhysicalIndex(int logicalIndex)
        {
            int index = _head + logicalIndex;
            if (index >= _pending.Length)
                index -= _pending.Length;
            return index;
        }

        private void EnsureCapacityForOneMore()
        {
            if (_count < _pending.Length)
                return;

            int newSize = _pending.Length * 2;
            if (newSize < 4)
                newSize = 4;

            var newBuffer = new ClientInputSample<TInput>[newSize];
            for (int i = 0; i < _count; i++)
                newBuffer[i] = _pending[PhysicalIndex(i)];
            _pending = newBuffer;
            _head = 0;
        }

        private void RemoveFront()
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<ClientInputSample<TInput>>())
                _pending[_head] = default;

            _head++;
            if (_head == _pending.Length)
                _head = 0;
            _count--;

            if (_count == 0)
                _head = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAcknowledged(ushort sequence, ushort acknowledgedSequence) =>
            sequence == acknowledgedSequence || SequenceMath.IsNewer(acknowledgedSequence, sequence);
    }
}
