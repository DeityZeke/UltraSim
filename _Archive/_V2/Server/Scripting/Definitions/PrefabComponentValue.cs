namespace UltraSim.Server.Scripting;

public sealed class PrefabComponentValue
{
    private readonly object? _constantValue;
    private readonly Func<PrefabRollContext, object>? _factory;

    private PrefabComponentValue(
        Type componentType,
        PrefabComponentValueKind kind,
        object? constantValue,
        Func<PrefabRollContext, object>? factory)
    {
        ComponentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
        Kind = kind;
        _constantValue = constantValue;
        _factory = factory;
    }

    public Type ComponentType { get; }
    public PrefabComponentValueKind Kind { get; }
    public string ComponentName => ComponentType.FullName ?? ComponentType.Name;
    public bool IsPerSpawnFactory => Kind == PrefabComponentValueKind.PerSpawnFactory;

    public static PrefabComponentValue Constant<T>(T value)
    {
        return new PrefabComponentValue(typeof(T), PrefabComponentValueKind.Constant, value!, factory: null);
    }

    public static PrefabComponentValue PerSpawn<T>(Func<PrefabRollContext, T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new PrefabComponentValue(
            typeof(T),
            PrefabComponentValueKind.PerSpawnFactory,
            constantValue: null,
            context => factory(context)!);
    }

    public bool TryGetValue<T>(out T value)
    {
        if (Kind == PrefabComponentValueKind.Constant && _constantValue is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    public bool TryCreateValue<T>(PrefabRollContext context, out T value)
    {
        object? raw = CreateValue(context);
        if (raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    public object CreateValue(PrefabRollContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Kind switch
        {
            PrefabComponentValueKind.Constant => _constantValue!,
            PrefabComponentValueKind.PerSpawnFactory => _factory!(context),
            _ => throw new InvalidOperationException($"Unknown prefab component value kind '{Kind}'."),
        };
    }
}
