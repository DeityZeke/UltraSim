namespace UltraSim.Server.Scripting;

public static class UltraSimRuntimeTemplate
{
    public static void Ensure(UltraSimRuntimeRootLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        Directory.CreateDirectory(layout.RootPath);
        Directory.CreateDirectory(layout.ScriptsPath);

        Directory.CreateDirectory(layout.DataPath);
        Directory.CreateDirectory(layout.DataPrefabsPath);
        Directory.CreateDirectory(Path.Combine(layout.DataPrefabsPath, "items"));
        Directory.CreateDirectory(Path.Combine(layout.DataPrefabsPath, "mobiles"));
        Directory.CreateDirectory(Path.Combine(layout.DataPrefabsPath, "containers"));
        Directory.CreateDirectory(Path.Combine(layout.DataPrefabsPath, "crafting"));

        Directory.CreateDirectory(layout.CachePath);
        Directory.CreateDirectory(layout.ScriptCachePath);
        Directory.CreateDirectory(layout.ScriptAssemblyCachePath);
        Directory.CreateDirectory(layout.ScriptManifestCachePath);
        Directory.CreateDirectory(layout.ScriptDiagnosticsCachePath);

        Directory.CreateDirectory(layout.SavesPath);
        Directory.CreateDirectory(layout.BackupsPath);
        Directory.CreateDirectory(layout.DocsPath);
    }
}
