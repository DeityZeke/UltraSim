namespace UltraSim.ECS;

public sealed partial class EcsWorld
{
    /// <summary>Gets the immutable content catalog bound before this world was sealed.</summary>
    public CompiledPrefabCatalog? ContentCatalog
    {
        get
        {
            EnsureOwnerThread();
            return _core.ContentCatalog;
        }
    }

    /// <summary>Compiles and binds class-shaped/direct prefab definitions before Seal.</summary>
    internal CompiledPrefabCatalog CompileContent(
        ContentCatalogBuilder builder,
        ContentCompilationOptions options)
    {
        EnsureOwnerThread();
        if (_core.State != EcsWorldState.Created)
            throw new InvalidOperationException("Content compilation must complete before world Seal.");
        return builder.CompileCore(_core, options);
    }

    /// <summary>Immediately instantiates one fully compiled prefab as an ordinary ECS entity.</summary>
    /// <remarks>
    /// Runtime execution performs direct handle lookup, ordinary row allocation,
    /// compiled default copies, and one Created publication. It performs no
    /// reflection, authoring inheritance traversal, or managed callback.
    /// </remarks>
    public bool TryInstantiate(PrefabHandle prefab, out Entity entity)
    {
        EnsureRuntimeAccess();
        ValidatePrefab(prefab, nameof(prefab));
        return _core.TryInstantiatePrefab(prefab.PrefabId, prefab.ArchetypeId, out entity);
    }

    /// <summary>Immediately instantiates a compiled prefab or throws on bounded capacity exhaustion.</summary>
    public Entity Instantiate(PrefabHandle prefab)
    {
        if (!TryInstantiate(prefab, out Entity entity))
        {
            throw new InvalidOperationException(
                "Prefab instantiation exhausted entity or target-archetype row capacity.");
        }
        return entity;
    }

    private void ValidatePrefab(PrefabHandle prefab, string parameterName)
    {
        if (!prefab.IsValid || prefab.World != Id)
            throw new ArgumentException("The prefab handle belongs to a different world.", parameterName);
        CompiledPrefabCatalog catalog = _core.ContentCatalog ??
            throw new InvalidOperationException("This world has no compiled content catalog.");
        catalog.Validate(prefab);
    }
}
