using Godot;
using UltraSim.Client.Content;

namespace UltraSim.Client.Godot.Content;

public sealed class GodotClientContentServices
{
    private GodotClientContentServices(
        ClientContentCachePathPolicy pathPolicy,
        ContentCacheIndex index,
        ContentCacheIndexStore indexStore,
        UiSchemaCacheStore schemaCache,
        ServerAssetCacheStore assetCache,
        LayeredContentResolver resolver)
    {
        PathPolicy = pathPolicy;
        Index = index;
        IndexStore = indexStore;
        SchemaCache = schemaCache;
        AssetCache = assetCache;
        Resolver = resolver;
    }

    public ClientContentCachePathPolicy PathPolicy { get; }
    public ContentCacheIndex Index { get; }
    public ContentCacheIndexStore IndexStore { get; }
    public UiSchemaCacheStore SchemaCache { get; }
    public ServerAssetCacheStore AssetCache { get; }
    public LayeredContentResolver Resolver { get; }

    public static GodotClientContentServices Load(string cacheRootPath, Action<string>? warningSink = null)
    {
        var pathPolicy = new ClientContentCachePathPolicy(cacheRootPath);
        var indexStore = new ContentCacheIndexStore(pathPolicy.CacheIndexPath);
        ContentCacheIndex index;
        try
        {
            index = indexStore.LoadOrCreate();
        }
        catch (InvalidDataException ex)
        {
            warningSink?.Invoke($"Ignoring invalid content cache index: {ex.Message}");
            index = new ContentCacheIndex();
        }

        var resolver = new LayeredContentResolver([CreateBuiltInLayer()]);
        return new GodotClientContentServices(
            pathPolicy,
            index,
            indexStore,
            new UiSchemaCacheStore(pathPolicy, index),
            new ServerAssetCacheStore(pathPolicy, index),
            resolver);
    }

    public void SaveIndex()
    {
        IndexStore.Save(Index);
    }

    public bool TryResolve(string logicalId, out ContentResolution resolution)
    {
        return Resolver.TryResolve(logicalId, out resolution);
    }

    private static ContentResolverLayer CreateBuiltInLayer()
    {
        var mappings = new Dictionary<string, string>
        {
            ["mobile/default/root-scene"] = "res://Scenes/CharacterRoot.tscn",
            ["ui/icon/app"] = "res://icon.svg"
        };

        if (!ContentResolverLayer.TryCreate(
            "builtin.godot",
            ContentTrustClass.BuiltIn,
            ContentResolverPriority.BuiltIn,
            mappings,
            out ContentResolverLayer layer,
            out string error))
        {
            throw new InvalidOperationException(error);
        }

        return layer;
    }
}
