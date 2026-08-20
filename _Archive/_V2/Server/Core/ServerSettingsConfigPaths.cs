namespace UltraSim.Server;

public static class ServerSettingsConfigPaths
{
    public const string RelativePath = "config/server_settings.json";

    public static string GetPath(string runtimeRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRootPath);
        return Path.Combine(runtimeRootPath, RelativePath);
    }
}
