using System.Reflection;

namespace UltraSim.Server.Scripting;

public static class UltraSimRuntimeRootResolver
{
    public static UltraSimRuntimeRootLayout Resolve(string? runtimeRootOverride = null, Assembly? entryAssembly = null)
    {
        string root = !string.IsNullOrWhiteSpace(runtimeRootOverride)
            ? runtimeRootOverride
            : ResolveDefaultRoot(entryAssembly);

        return FromRoot(root);
    }

    public static UltraSimRuntimeRootLayout FromRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Runtime root path cannot be empty.", nameof(rootPath));

        string root = Path.GetFullPath(rootPath);
        string scripts = Path.Combine(root, "scripts");
        string data = Path.Combine(root, "data");
        string cache = Path.Combine(root, "cache");
        string scriptCache = Path.Combine(cache, "scripts");

        return new UltraSimRuntimeRootLayout(
            root,
            scripts,
            data,
            cache,
            scriptCache,
            Path.Combine(root, "saves"),
            Path.Combine(root, "backups"),
            Path.Combine(root, "docs"));
    }

    private static string ResolveDefaultRoot(Assembly? entryAssembly)
    {
        string? environmentRoot = Environment.GetEnvironmentVariable("ULTRASIM_RUNTIME_ROOT");
        if (!string.IsNullOrWhiteSpace(environmentRoot))
            return environmentRoot;

        string? baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory))
            return baseDirectory;

        string? assemblyLocation = (entryAssembly ?? Assembly.GetEntryAssembly())?.Location;
        if (!string.IsNullOrWhiteSpace(assemblyLocation))
            return Path.GetDirectoryName(assemblyLocation) ?? Directory.GetCurrentDirectory();

        return Directory.GetCurrentDirectory();
    }
}
