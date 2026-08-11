namespace UltraSim.ECS;

public delegate void PrefabApply(Archetype archetype, int slot);

public delegate void PrefabApplyResolved(Archetype archetype, int slot, ReadOnlySpan<int> columns);

/// <summary>
/// Base contract for typed prefab values that can stamp initial component state into a spawned entity.
/// </summary>
public interface IPrefab<TSelf>
    where TSelf : unmanaged, IPrefab<TSelf>
{
    ArchetypeSignature Signature { get; }
    void ApplyTo(Archetype archetype, int slot);
}

/// <summary>
/// Optional high-performance prefab contract that separates shared template application from per-instance writes.
/// </summary>
public interface ITemplatePrefab<TSelf> : IPrefab<TSelf>
    where TSelf : unmanaged, ITemplatePrefab<TSelf>
{
    bool HasPerInstanceData { get; }
    void ApplyTemplateTo(Archetype archetype, int slot);
    void ApplyInstanceTo(Archetype archetype, int slot);
}

/// <summary>
/// Optional prefab contract for pre-resolved component-column application.
/// </summary>
public interface IResolvedPrefab<TSelf> : IPrefab<TSelf>
    where TSelf : unmanaged, IResolvedPrefab<TSelf>
{
    static abstract ComponentId[] ResolvedComponentIds { get; }
    void ApplyResolvedTo(Archetype archetype, int slot, ReadOnlySpan<int> columns);
}
