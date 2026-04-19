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
}
