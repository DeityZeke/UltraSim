namespace UltraSim.Server.Scripting;

public sealed record UltraSimRuntimeRootLayout(
    string RootPath,
    string ScriptsPath,
    string DataPath,
    string CachePath,
    string ScriptCachePath,
    string SavesPath,
    string BackupsPath,
    string DocsPath)
{
    public string ScriptAssemblyCachePath => Path.Combine(ScriptCachePath, "assemblies");
    public string ScriptManifestCachePath => Path.Combine(ScriptCachePath, "manifests");
    public string ScriptDiagnosticsCachePath => Path.Combine(ScriptCachePath, "diagnostics");
    public string DataPrefabsPath => Path.Combine(DataPath, "prefabs");
}
