using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace UltraSim.Network
{
    public sealed class ClientInterpolationBuffer<TState>
    {
        private readonly bool _clearSamples = RuntimeHelpers.IsReferenceOrContainsReferences<InterpolationSample<TState>>();
        private InterpolationSample<TState>[] _samples;
        private int _start;
        private int _count;
        private int _lastBracketIndex;

        public ClientInterpolationBuffer(int initialCapacity = 32)
        {
            if (initialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            _samples = new InterpolationSample<TState>[initialCapacity];
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _count;
        }

        public void AddSample(long serverTick, in TState state)
        {
            var sample = new InterpolationSample<TState>(serverTick, in state);
            if (_count == 0)
            {
                _samples[0] = sample;
                _start = 0;
                _count = 1;
                return;
            }

            int lastIndex = _count - 1;
            ref InterpolationSample<TState> last = ref GetAt(lastIndex);
            if (serverTick > last.ServerTick)
            {
                EnsureCapacityForOneMore();
                _samples[PhysicalIndex(_count)] = sample;
                _count++;
                return;
            }

            if (serverTick == last.ServerTick)
            {
                last = sample;
                return;
            }

            int index = BinarySearch(serverTick);
            if (index >= 0)
            {
                GetAt(index) = sample;
                if (index <= _lastBracketIndex)
                    _lastBracketIndex = 0;
                return;
            }

            InsertAt(~index, in sample);
        }

        public int PruneBefore(long minServerTick)
        {
            if (_count <= 1)
                return 0;

            int removable = 0;
            int lastRemovableIndex = _count - 2;
            while (removable <= lastRemovableIndex && GetAt(removable + 1).ServerTick < minServerTick)
                removable++;

            if (removable == 0)
                return 0;

            if (_clearSamples)
            {
                for (int i = 0; i < removable; i++)
                    _samples[PhysicalIndex(i)] = default;
            }

            _start = PhysicalIndex(removable);
            _count -= removable;
            if (_count == 0)
                _start = 0;

            if (_lastBracketIndex >= removable)
                _lastBracketIndex -= removable;
            else
                _lastBracketIndex = 0;

            return removable;
        }

        public int TrimToMaxCount(int maxCount)
        {
            if (maxCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCount));
            if (_count <= maxCount)
                return 0;

            int removable = _count - maxCount;
            if (_clearSamples)
            {
                for (int i = 0; i < removable; i++)
                    _samples[PhysicalIndex(i)] = default;
            }

            _start = PhysicalIndex(removable);
            _count -= removable;
            if (_count == 0)
                _start = 0;

            if (_lastBracketIndex >= removable)
                _lastBracketIndex -= removable;
            else
                _lastBracketIndex = 0;

            return removable;
        }

        public bool TrySample(long targetServerTick, InterpolationLerp<TState> lerp, out TState value)
        {
            ArgumentNullException.ThrowIfNull(lerp);

            if (_count == 0)
            {
                value = default!;
                return false;
            }

            if (_count == 1 || targetServerTick <= GetAt(0).ServerTick)
            {
                value = GetAt(0).State;
                return true;
            }

            int lastIndex = _count - 1;
            if (targetServerTick >= GetAt(lastIndex).ServerTick)
            {
                value = GetAt(lastIndex).State;
                return true;
            }

            int cachedFrom = _lastBracketIndex;
            if ((uint)cachedFrom < (uint)lastIndex && cachedFrom + 1 <= lastIndex)
            {
                ref InterpolationSample<TState> cachedA = ref GetAt(cachedFrom);
                ref InterpolationSample<TState> cachedB = ref GetAt(cachedFrom + 1);
                if (targetServerTick >= cachedA.ServerTick && targetServerTick <= cachedB.ServerTick)
                {
                    value = Interpolate(in cachedA, in cachedB, targetServerTick, lerp);
                    return true;
                }
            }

            int upper = FindUpperBound(targetServerTick, lastIndex);
            int fromIndex = upper - 1;
            _lastBracketIndex = fromIndex;
            ref InterpolationSample<TState> from = ref GetAt(fromIndex);
            ref InterpolationSample<TState> to = ref GetAt(upper);
            value = Interpolate(in from, in to, targetServerTick, lerp);
            return true;
        }

        public bool TryGetTickBounds(out long minServerTick, out long maxServerTick)
        {
            if (_count == 0)
            {
                minServerTick = 0;
                maxServerTick = 0;
                return false;
            }

            minServerTick = GetAt(0).ServerTick;
            maxServerTick = GetAt(_count - 1).ServerTick;
            return true;
        }

        public void Clear()
        {
            if (_clearSamples && _count > 0)
            {
                for (int i = 0; i < _count; i++)
                    _samples[PhysicalIndex(i)] = default;
            }

            _start = 0;
            _count = 0;
            _lastBracketIndex = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref InterpolationSample<TState> GetAt(int logicalIndex) =>
            ref _samples[PhysicalIndex(logicalIndex)];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int PhysicalIndex(int logicalIndex)
        {
            int index = _start + logicalIndex;
            if (index >= _samples.Length)
                index -= _samples.Length;
            return index;
        }

        private int BinarySearch(long serverTick)
        {
            int low = 0;
            int high = _count - 1;
            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                long midTick = GetAt(mid).ServerTick;
                if (midTick < serverTick)
                {
                    low = mid + 1;
                    continue;
                }

                if (midTick > serverTick)
                {
                    high = mid - 1;
                    continue;
                }

                return mid;
            }

            return ~low;
        }

        private void InsertAt(int index, in InterpolationSample<TState> sample)
        {
            EnsureCapacityForOneMore();
            MakeContiguous();
            if (index < _count)
                Array.Copy(_samples, index, _samples, index + 1, _count - index);
            _samples[index] = sample;
            _count++;
            if (index <= _lastBracketIndex)
                _lastBracketIndex = 0;
        }

        private void EnsureCapacityForOneMore()
        {
            if (_count < _samples.Length)
                return;

            int newSize = _samples.Length * 2;
            if (newSize < 4)
                newSize = 4;

            var newBuffer = new InterpolationSample<TState>[newSize];
            for (int i = 0; i < _count; i++)
                newBuffer[i] = GetAt(i);
            _samples = newBuffer;
            _start = 0;
        }

        private void MakeContiguous()
        {
            if (_start == 0 || _count == 0)
                return;

            var newBuffer = new InterpolationSample<TState>[_samples.Length];
            for (int i = 0; i < _count; i++)
                newBuffer[i] = GetAt(i);
            _samples = newBuffer;
            _start = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindUpperBound(long targetServerTick, int lastIndex)
        {
            int low = 0;
            int high = lastIndex;
            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                if (GetAt(mid).ServerTick <= targetServerTick)
                    low = mid + 1;
                else
                    high = mid - 1;
            }

            return low;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TState Interpolate(
            in InterpolationSample<TState> from,
            in InterpolationSample<TState> to,
            long targetServerTick,
            InterpolationLerp<TState> lerp)
        {
            Debug.Assert(to.ServerTick >= from.ServerTick);
            if (to.ServerTick == from.ServerTick)
                return to.State;

            float alpha = (float)(targetServerTick - from.ServerTick) / (to.ServerTick - from.ServerTick);
            if (alpha < 0f)
                alpha = 0f;
            else if (alpha > 1f)
                alpha = 1f;

            return lerp(in from.State, in to.State, alpha);
        }
    }
}
