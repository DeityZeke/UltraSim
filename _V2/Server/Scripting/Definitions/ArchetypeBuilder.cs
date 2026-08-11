using UltraSim.ECS;

namespace UltraSim.Server.Scripting;

public sealed class ArchetypeBuilder
{
    private readonly string _key;
    private readonly Dictionary<Type, ArchetypeComponentDescriptor> _components = new();

    public ArchetypeBuilder(string key)
    {
        ScriptDefinitionValidation.ValidateStableKey(key, nameof(key));
        _key = ScriptDefinitionValidation.NormalizeKey(key);
    }

    public ArchetypeBuilder AddComponent<T>()
        where T : unmanaged, IComponent<T>
    {
        return AddComponent(typeof(T));
    }

    public ArchetypeBuilder AddComponent(Type componentType)
    {
        ScriptDefinitionValidation.ValidateComponentType(componentType);
        _components.TryAdd(componentType, new ArchetypeComponentDescriptor(componentType));
        return this;
    }

    public ArchetypeDescriptor Build()
    {
        return new ArchetypeDescriptor(
            _key,
            _components.Values
                .OrderBy(static component => component.ComponentName, StringComparer.Ordinal)
                .ToArray());
    }
}
