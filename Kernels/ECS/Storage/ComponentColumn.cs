namespace UltraSim.ECS;

internal sealed class ComponentColumn<T> : IComponentColumn
    where T : unmanaged, IComponent<T>
{
    private T[] _data;

    public ComponentColumn(int capacity)
    {
        _data = capacity > 0 ? new T[capacity] : Array.Empty<T>();
    }

    public int Capacity => _data.Length;

    public void EnsureCapacity(int capacity)
    {
        if (capacity <= _data.Length)
            return;

        int newCapacity = _data.Length == 0
            ? Math.Max(64, capacity)
            : Math.Max(_data.Length * 2, capacity);

        Array.Resize(ref _data, newCapacity);
    }

    public void SetDefaultAt(int slot)
    {
        _data[slot] = default;
    }

    public void SwapLastIntoSlot(int slot, int last)
    {
        if (slot != last)
            _data[slot] = _data[last];
    }

    public void CopySlot(int source, int destination)
    {
        _data[destination] = _data[source];
    }

    public void CopySlotRange(int source, int destinationStart, int count)
    {
        _data.AsSpan(destinationStart, count).Fill(_data[source]);
    }

    public void CopyFrom(IComponentColumn source, int sourceSlot, int destinationSlot)
    {
        _data[destinationSlot] = ((ComponentColumn<T>)source)._data[sourceSlot];
    }

    public Span<T> AsSpan(int count) => _data.AsSpan(0, count);
}
