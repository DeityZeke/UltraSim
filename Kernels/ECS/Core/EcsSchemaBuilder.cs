using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

public sealed class EcsSchemaBuilder
{
    private readonly Dictionary<Type, ComponentId> _typeToId = new();
    private readonly List<ComponentTypeInfo> _components = new();
    private bool _built;

    public int ComponentCount => _components.Count;

    public ComponentId RegisterComponent<T>() where T : unmanaged, IComponent<T>
        => RegisterComponent(typeof(T), static capacity => new ComponentColumn<T>(capacity));

    public ComponentId RegisterComponent(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);

        return RegisterComponent(componentType, CreateFactory(componentType));
    }

    public bool TryGetComponentId(Type componentType, out ComponentId id)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        return _typeToId.TryGetValue(componentType, out id);
    }

    public EcsSchema Build()
    {
        EnsureMutable();

        var components = _components.ToArray();
        var typeToId = new Dictionary<Type, ComponentId>(_typeToId);
        _built = true;
        return new EcsSchema(components, typeToId);
    }

    private ComponentId RegisterComponent(Type componentType, Func<int, IComponentColumn> columnFactory)
    {
        EnsureMutable();
        ValidateComponentType(componentType);

        if (_typeToId.TryGetValue(componentType, out var existing))
            return existing;

        var id = new ComponentId(_components.Count);
        _typeToId.Add(componentType, id);
        _components.Add(new ComponentTypeInfo(id, componentType, columnFactory));
        return id;
    }

    private void EnsureMutable()
    {
        if (_built)
            throw new InvalidOperationException("Schema builder has already been built.");
    }

    private static Func<int, IComponentColumn> CreateFactory(Type componentType)
    {
        ValidateComponentType(componentType);

        Type columnType = typeof(ComponentColumn<>).MakeGenericType(componentType);
        var ctor = columnType.GetConstructor(new[] { typeof(int) })
            ?? throw new InvalidOperationException($"Missing component column constructor for {componentType.FullName}.");

        return capacity => (IComponentColumn)ctor.Invoke(new object[] { capacity });
    }

    private static void ValidateComponentType(Type componentType)
    {
        if (!componentType.IsValueType)
            throw new ArgumentException($"Component type {componentType.FullName} must be a struct.", nameof(componentType));

        if (!ImplementsIComponentOfSelf(componentType))
        {
            throw new ArgumentException(
                $"Component type {componentType.FullName} must implement IComponent<{componentType.Name}>.",
                nameof(componentType));
        }
    }

    private static bool ImplementsIComponentOfSelf(Type componentType)
    {
        Type interfaceType = typeof(IComponent<>);
        foreach (Type candidate in componentType.GetInterfaces())
        {
            if (!candidate.IsGenericType)
                continue;

            if (candidate.GetGenericTypeDefinition() != interfaceType)
                continue;

            Type[] args = candidate.GetGenericArguments();
            if (args.Length == 1 && args[0] == componentType)
                return true;
        }

        return false;
    }
}
