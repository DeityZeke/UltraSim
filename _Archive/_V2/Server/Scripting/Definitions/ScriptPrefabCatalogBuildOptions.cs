namespace UltraSim.Server.Scripting;

public sealed class ScriptPrefabCatalogBuildOptions
{
    public ScriptPrefabCatalogBuildOptions(Random? random = null)
    {
        Random = random ?? new Xoroshiro256PlusPlusRandom();
    }

    public Random Random { get; }
}
