namespace UltraSim.Client.Content;

public sealed class LayeredContentResolver : IContentResolver
{
    private readonly ContentResolverLayer[] _layers;

    public LayeredContentResolver(IEnumerable<ContentResolverLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        _layers = layers
            .OrderByDescending(static layer => layer.Priority)
            .ThenBy(static layer => layer.LayerId, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<ContentResolverLayer> Layers => _layers;

    public bool TryResolve(string logicalId, out ContentResolution resolution)
    {
        foreach (ContentResolverLayer layer in _layers)
        {
            if (layer.TryResolve(logicalId, out resolution))
            {
                return true;
            }
        }

        resolution = default;
        return false;
    }
}
