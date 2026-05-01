namespace UltraSim.Server.Scripting;

public sealed record PrefabDescriptor(
    string Key,
    string ArchetypeKey,
    Type ArchetypeShapeType,
    IReadOnlyList<PrefabComponentValue> ComponentValues)
{
    private readonly Dictionary<Type, PrefabComponentValue> _valuesByType =
        ComponentValues.ToDictionary(static value => value.ComponentType);

    public bool TryGetComponentValue<T>(out T value)
    {
        if (_valuesByType.TryGetValue(typeof(T), out PrefabComponentValue? stored) &&
            stored.TryGetValue(out value))
        {
            return true;
        }

        value = default!;
        return false;
    }

    public bool TryCreateComponentValue<T>(PrefabRollContext context, out T value)
    {
        if (_valuesByType.TryGetValue(typeof(T), out PrefabComponentValue? stored) &&
            stored.TryCreateValue(context, out value))
        {
            return true;
        }

        value = default!;
        return false;
    }
}
