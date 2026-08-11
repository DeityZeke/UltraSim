using UltraSim.ECS;

namespace UltraSim.Server.Scripting;

public sealed class PrefabBuilder
{
    private readonly string _key;
    private readonly string _sourceName;
    private readonly ArchetypeDescriptor _archetype;
    private readonly Type _archetypeShapeType;
    private readonly Dictionary<Type, PrefabComponentValue> _values = new();

    public PrefabBuilder(string key, string sourceName, ArchetypeDescriptor archetype, Type archetypeShapeType)
    {
        ScriptDefinitionValidation.ValidateStableKey(key, nameof(key));
        if (string.IsNullOrWhiteSpace(sourceName))
            throw new ArgumentException("sourceName cannot be empty.", nameof(sourceName));
        ArgumentNullException.ThrowIfNull(archetype);
        ArgumentNullException.ThrowIfNull(archetypeShapeType);
        if (!typeof(ArchetypeShape).IsAssignableFrom(archetypeShapeType))
            throw new ArgumentException("Archetype shape type must derive from ArchetypeShape.", nameof(archetypeShapeType));

        _key = ScriptDefinitionValidation.NormalizeKey(key);
        _sourceName = sourceName.Trim();
        _archetype = archetype;
        _archetypeShapeType = archetypeShapeType;
    }

    public PrefabBuilder Set<T>(T value)
        where T : unmanaged, IComponent<T>
    {
        SetValue(PrefabComponentValue.Constant(value));
        return this;
    }

    public PrefabBuilder SetPerSpawn<T>(Func<PrefabRollContext, T> factory)
        where T : unmanaged, IComponent<T>
    {
        SetValue(PrefabComponentValue.PerSpawn(factory));
        return this;
    }

    public PrefabBuilder SetRandom<T>(Func<Random, T> factory)
        where T : unmanaged, IComponent<T>
    {
        ArgumentNullException.ThrowIfNull(factory);
        SetPerSpawn(context => factory(context.Random));
        return this;
    }

    public PrefabDescriptor Build()
    {
        return new PrefabDescriptor(
            _key,
            _sourceName,
            _archetype.Key,
            _archetypeShapeType,
            _values.Values
                .OrderBy(static value => value.ComponentName, StringComparer.Ordinal)
                .ToArray());
    }

    private void SetValue(PrefabComponentValue value)
    {
        Type componentType = value.ComponentType;
        if (!_archetype.ContainsComponent(componentType))
        {
            throw new InvalidOperationException(
                $"Prefab '{_key}' cannot set component '{componentType.FullName}' because archetype '{_archetype.Key}' does not include it.");
        }

        _values[componentType] = value;
    }
}
