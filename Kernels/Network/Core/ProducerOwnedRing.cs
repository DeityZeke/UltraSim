using System.Runtime.CompilerServices;

namespace UltraSim.Network;

/// <summary>Fixed single-producer/single-consumer ring with explicit thread ownership.</summary>
public sealed class ProducerOwnedRing<T> where T : unmanaged
{
    private readonly T[] _items;
    private readonly int _producerThreadId;
    private readonly int _consumerThreadId;
    private long _writeSequence;
    private long _readSequence;

    public ProducerOwnedRing(int capacity, int producerThreadId, int consumerThreadId)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (producerThreadId <= 0) throw new ArgumentOutOfRangeException(nameof(producerThreadId));
        if (consumerThreadId <= 0) throw new ArgumentOutOfRangeException(nameof(consumerThreadId));
        _items = new T[capacity];
        _producerThreadId = producerThreadId;
        _consumerThreadId = consumerThreadId;
    }

    public int Capacity => _items.Length;
    public int Count => checked((int)(Volatile.Read(ref _writeSequence) - Volatile.Read(ref _readSequence)));
    public long ReservedBytes => checked((long)_items.Length * Unsafe.SizeOf<T>());

    public bool TryEnqueue(in T item)
    {
        EnsureThread(_producerThreadId, "producer");
        long write = _writeSequence;
        long read = Volatile.Read(ref _readSequence);
        if (write - read == _items.Length) return false;
        if (write == long.MaxValue)
            throw new InvalidOperationException("The producer ring sequence space is exhausted.");
        int index = (int)(write % _items.Length);
        _items[index] = item;
        Volatile.Write(ref _writeSequence, write + 1);
        return true;
    }

    public bool TryDequeue(out T item)
    {
        EnsureThread(_consumerThreadId, "consumer");
        long read = _readSequence;
        long write = Volatile.Read(ref _writeSequence);
        if (read == write) { item = default; return false; }
        int index = (int)(read % _items.Length);
        item = _items[index];
        _items[index] = default;
        Volatile.Write(ref _readSequence, read + 1);
        return true;
    }

    private static void EnsureThread(int expected, string role)
    {
        if (Environment.CurrentManagedThreadId != expected)
            throw new InvalidOperationException($"This ring operation belongs to its configured {role} thread.");
    }
}
