namespace UltraSim.Client.Local;

/// <summary>
/// Small fixed-storage cross-thread mailbox used only inside the concrete local
/// application adapters. The lock is the publication fence; enqueue/dequeue
/// never allocate after construction.
/// </summary>
internal sealed class BoundedSpscQueue<T>
{
    private readonly object _gate = new();
    private readonly T[] _items;
    private int _head;
    private int _count;
    private int _highWater;

    internal BoundedSpscQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _items = new T[capacity];
    }

    internal int Capacity => _items.Length;
    internal int Count { get { lock (_gate) return _count; } }
    internal int HighWater { get { lock (_gate) return _highWater; } }

    internal bool TryEnqueue(in T value)
    {
        lock (_gate)
        {
            if (_count == _items.Length) return false;
            int tail = (_head + _count) % _items.Length;
            _items[tail] = value;
            _count++;
            _highWater = Math.Max(_highWater, _count);
            return true;
        }
    }

    internal bool TryPeek(out T value)
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                value = default!;
                return false;
            }
            value = _items[_head];
            return true;
        }
    }

    internal bool TryDequeue(out T value)
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                value = default!;
                return false;
            }
            value = _items[_head];
            _items[_head] = default!;
            _head = (_head + 1) % _items.Length;
            _count--;
            return true;
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_items);
            _head = 0;
            _count = 0;
        }
    }
}
