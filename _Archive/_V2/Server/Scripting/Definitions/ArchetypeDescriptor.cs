namespace UltraSim.Server.Scripting;

public sealed record ArchetypeDescriptor(
    string Key,
    IReadOnlyList<ArchetypeComponentDescriptor> Components)
{
    private readonly HashSet<Type> _componentTypes = Components.Select(static component => component.ComponentType).ToHashSet();

    public bool ContainsComponent(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        return _componentTypes.Contains(componentType);
    }

    public bool ContainsComponent<T>() => ContainsComponent(typeof(T));
}
