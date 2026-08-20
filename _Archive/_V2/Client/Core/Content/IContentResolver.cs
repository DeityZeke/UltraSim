namespace UltraSim.Client.Content;

public interface IContentResolver
{
    bool TryResolve(string logicalId, out ContentResolution resolution);
}

public readonly record struct ContentResolution(
    string LogicalId,
    string ResolvedPath,
    ContentTrustClass TrustClass,
    string LayerId = "");

public static class ContentResolverPriority
{
    public const int BuiltIn = 0;
    public const int UserResourcePack = 100;
    public const int ServerPatch = 200;
    public const int UserMod = 300;
}

public enum ContentTrustClass
{
    BuiltIn = 0,
    UserResourcePack = 1,
    ServerPatch = 2,
    UserMod = 3
}
