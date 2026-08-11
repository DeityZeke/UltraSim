using System.Runtime.CompilerServices;
using System.Threading;

namespace UltraSim.ECS;

public sealed class EcsSchema
{
    private static class ComponentIdCache<T> where T : unmanaged, IComponent<T>
    {
        public static EcsSchema? Schema;
        public static ComponentId Id;
    }

    private readonly ComponentTypeInfo[] _components;
    private readonly Dictionary<Type, ComponentId> _typeToId;

    internal EcsSchema(ComponentTypeInfo[] components, Dictionary<Type, ComponentId> typeToId)
    {
        _components = components;
        _typeToId = typeToId;
    }

    public int ComponentCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _components.Length;
    }

    public IReadOnlyList<ComponentTypeInfo> Components => _components;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentId GetComponentId<T>() where T : unmanaged, IComponent<T>
    {
        EcsSchema? cachedSchema = Volatile.Read(ref ComponentIdCache<T>.Schema);
        if (ReferenceEquals(cachedSchema, this))
            return ComponentIdCache<T>.Id;

        ComponentId id = GetComponentId(typeof(T));
        ComponentIdCache<T>.Id = id;
        Volatile.Write(ref ComponentIdCache<T>.Schema, this);
        return id;
    }

    public ComponentId GetComponentId(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);

        if (_typeToId.TryGetValue(componentType, out var id))
            return id;

        throw new InvalidOperationException($"Component type {componentType.FullName} is not registered in this schema.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetComponentId(Type componentType, out ComponentId id)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        return _typeToId.TryGetValue(componentType, out id);
    }

    public Type GetComponentType(ComponentId id) => GetComponentInfo(id).ClrType;

    public ComponentTypeInfo GetComponentInfo(ComponentId id)
    {
        int index = id.Value;
        if ((uint)index >= (uint)_components.Length)
            throw new ArgumentOutOfRangeException(nameof(id), $"Invalid component id: {id.Value}");

        return _components[index];
    }

    internal IComponentColumn CreateColumn(ComponentId id, int capacity)
        => GetComponentInfo(id).ColumnFactory(capacity);
}
