namespace UltraSim.ECS;

internal sealed class DirtyTracker
{
    private readonly object _sync = new();
    private readonly HashSet<Entity> _all = new();
    private readonly Dictionary<int, HashSet<Entity>> _byComponent = new();

    public void Mark(Entity entity)
    {
        lock (_sync)
            _all.Add(entity);
    }

    public void Mark(ComponentId componentId, Entity entity)
    {
        int index = componentId.Value;
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(componentId), "ComponentId.Invalid is not allowed.");

        lock (_sync)
        {
            _all.Add(entity);
            if (!_byComponent.TryGetValue(index, out HashSet<Entity>? set))
            {
                set = new HashSet<Entity>();
                _byComponent[index] = set;
            }

            set.Add(entity);
        }
    }

    public int CopyAllTo(Span<Entity> destination)
    {
        lock (_sync)
            return CopyTo(destination, _all);
    }

    public int CopyAndClearAllTo(Span<Entity> destination)
    {
        lock (_sync)
        {
            int written = CopyTo(destination, _all);
            _all.Clear();
            return written;
        }
    }

    public int CopyComponentTo(ComponentId componentId, Span<Entity> destination)
    {
        lock (_sync)
        {
            return _byComponent.TryGetValue(componentId.Value, out HashSet<Entity>? set)
                ? CopyTo(destination, set)
                : 0;
        }
    }

    public int CopyAndClearComponentTo(ComponentId componentId, Span<Entity> destination)
    {
        lock (_sync)
        {
            if (!_byComponent.TryGetValue(componentId.Value, out HashSet<Entity>? set))
                return 0;

            int written = CopyTo(destination, set);
            set.Clear();
            return written;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _all.Clear();
            foreach (HashSet<Entity> set in _byComponent.Values)
                set.Clear();
        }
    }

    private static int CopyTo(Span<Entity> destination, IEnumerable<Entity> source)
    {
        int written = 0;
        foreach (Entity entity in source)
        {
            if (written >= destination.Length)
                break;

            destination[written++] = entity;
        }

        return written;
    }
}
