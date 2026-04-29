namespace UltraSim.Client.Content;

public interface IContentResolver
{
    bool TryResolve(string logicalId, out ContentResolution resolution);
}

public readonly record struct ContentResolution(
    string LogicalId,
    string ResolvedPath,
    ContentTrustClass TrustClass);

public enum ContentTrustClass
{
    BuiltIn = 0,
    UserResourcePack = 1,
    ServerPatch = 2,
    UserMod = 3
}
