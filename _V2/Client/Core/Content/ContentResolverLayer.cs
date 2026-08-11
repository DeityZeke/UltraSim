namespace UltraSim.Client.Content;

public sealed class ContentResolverLayer
{
    private readonly Dictionary<string, string> _mappings;

    private ContentResolverLayer(
        string layerId,
        ContentTrustClass trustClass,
        int priority,
        Dictionary<string, string> mappings)
    {
        LayerId = layerId;
        TrustClass = trustClass;
        Priority = priority;
        _mappings = mappings;
    }

    public string LayerId { get; }
    public ContentTrustClass TrustClass { get; }
    public int Priority { get; }
    public IReadOnlyDictionary<string, string> Mappings => _mappings;

    public static bool TryCreate(
        string? layerId,
        ContentTrustClass trustClass,
        int priority,
        IReadOnlyDictionary<string, string> mappings,
        out ContentResolverLayer layer,
        out string error)
    {
        layer = default!;
        string normalizedLayerId = (layerId ?? string.Empty).Trim();
        if (!IsSafeIdentifier(normalizedLayerId, maxLength: 96))
        {
            error = "Content resolver layer id must be 1-96 characters and contain only letters, digits, '.', '_', or '-'.";
            return false;
        }

        var normalizedMappings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> mapping in mappings)
        {
            if (!TryNormalizeLogicalId(mapping.Key, out string logicalId, out error))
            {
                return false;
            }

            string resolvedPath = (mapping.Value ?? string.Empty).Trim();
            if (resolvedPath.Length == 0)
            {
                error = $"Content mapping '{logicalId}' must have a resolved path.";
                return false;
            }

            if (resolvedPath.Any(char.IsControl))
            {
                error = $"Content mapping '{logicalId}' resolved path cannot contain control characters.";
                return false;
            }

            if (normalizedMappings.ContainsKey(logicalId))
            {
                error = $"Content resolver layer '{normalizedLayerId}' contains duplicate logical id '{logicalId}'.";
                return false;
            }

            normalizedMappings.Add(logicalId, resolvedPath);
        }

        layer = new ContentResolverLayer(normalizedLayerId, trustClass, priority, normalizedMappings);
        error = string.Empty;
        return true;
    }

    public bool TryResolve(string logicalId, out ContentResolution resolution)
    {
        resolution = default;
        if (!TryNormalizeLogicalId(logicalId, out string normalizedLogicalId, out _))
        {
            return false;
        }

        if (!_mappings.TryGetValue(normalizedLogicalId, out string? resolvedPath))
        {
            return false;
        }

        resolution = new ContentResolution(normalizedLogicalId, resolvedPath, TrustClass, LayerId);
        return true;
    }

    public static bool TryNormalizeLogicalId(string? logicalId, out string normalizedLogicalId, out string error)
    {
        normalizedLogicalId = (logicalId ?? string.Empty).Trim();
        if (normalizedLogicalId.Length == 0)
        {
            error = "Logical content id is required.";
            return false;
        }

        if (normalizedLogicalId.Length > 160)
        {
            error = "Logical content id cannot exceed 160 characters.";
            return false;
        }

        if (normalizedLogicalId.Any(char.IsControl))
        {
            error = "Logical content id cannot contain control characters.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsSafeIdentifier(string value, int maxLength)
    {
        if (value.Length == 0 || value.Length > maxLength)
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
