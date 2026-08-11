using UltraSim.ECS;

namespace UltraSim.Server.Scripting;

public sealed class ScriptPrefabCatalogBuildResult
{
    public ScriptPrefabCatalogBuildResult(
        PrefabCatalog catalog,
        IReadOnlyDictionary<string, ArchetypeSignature> archetypeSignatures)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        ArchetypeSignatures = archetypeSignatures ?? throw new ArgumentNullException(nameof(archetypeSignatures));
    }

    public PrefabCatalog Catalog { get; }
    public IReadOnlyDictionary<string, ArchetypeSignature> ArchetypeSignatures { get; }
}
