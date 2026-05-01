namespace UltraSim.Server.Scripting;

public sealed class ScriptPrefabCatalogBuildOptions
{
    public ScriptPrefabCatalogBuildOptions(Random? random = null)
    {
        Random = random ?? new Random();
    }

    public Random Random { get; }
}
