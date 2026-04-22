using System.Reflection;

namespace UltraSim.ECS.Persistence;

internal readonly struct PersistenceComponentAccess
{
    public PersistenceComponentAccess(
        Func<PersistenceArchetypeView, ComponentId, Array> capture,
        Action<Archetype, ComponentId, int, Array> applyRange)
    {
        Capture = capture;
        ApplyRange = applyRange;
    }

    public Func<PersistenceArchetypeView, ComponentId, Array> Capture { get; }
    public Action<Archetype, ComponentId, int, Array> ApplyRange { get; }
}

internal static class PersistenceComponentAccessCache
{
    private static readonly object s_sync = new();
    private static readonly Dictionary<Type, PersistenceComponentAccess> s_byType = new();

    public static PersistenceComponentAccess Get(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);

        lock (s_sync)
        {
            if (s_byType.TryGetValue(componentType, out PersistenceComponentAccess existing))
                return existing;

            PersistenceComponentAccess created = Create(componentType);
            s_byType.Add(componentType, created);
            return created;
        }
    }

    private static PersistenceComponentAccess Create(Type componentType)
    {
        MethodInfo captureMethod = typeof(PersistenceComponentAccessCache)
            .GetMethod(nameof(CaptureTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(componentType);
        MethodInfo applyMethod = typeof(PersistenceComponentAccessCache)
            .GetMethod(nameof(ApplyRangeTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(componentType);

        return new PersistenceComponentAccess(
            (Func<PersistenceArchetypeView, ComponentId, Array>)Delegate.CreateDelegate(
                typeof(Func<PersistenceArchetypeView, ComponentId, Array>),
                captureMethod),
            (Action<Archetype, ComponentId, int, Array>)Delegate.CreateDelegate(
                typeof(Action<Archetype, ComponentId, int, Array>),
                applyMethod));
    }

    private static Array CaptureTyped<T>(PersistenceArchetypeView archetypeView, ComponentId componentId)
        where T : unmanaged, IComponent<T>
        => archetypeView.GetComponentSpan<T>(componentId).ToArray();

    private static void ApplyRangeTyped<T>(Archetype archetype, ComponentId componentId, int startSlot, Array values)
        where T : unmanaged, IComponent<T>
    {
        if (values is not T[] typedValues)
            throw new InvalidCastException($"Expected {typeof(T).Name}[], got {values.GetType().Name}.");

        typedValues.AsSpan().CopyTo(archetype.GetComponentSpan<T>(componentId)[startSlot..(startSlot + typedValues.Length)]);
    }
}
