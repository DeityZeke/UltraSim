using System;
using System.Collections.Generic;
using UltraSim.Client.Runtime;
using Godot;

namespace Client.Godot
{
    public sealed class GodotContentResolver : IContentResolver
    {
        private readonly Dictionary<(ClientAssetKind Kind, string Id), string> _assetPaths =
            new();

        public void Register(ClientAssetKind kind, string logicalId, string resourcePath)
        {
            if (kind == ClientAssetKind.Unknown)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(logicalId))
                throw new ArgumentException("Logical asset id is required.", nameof(logicalId));
            if (string.IsNullOrWhiteSpace(resourcePath))
                throw new ArgumentException("Resource path is required.", nameof(resourcePath));

            _assetPaths[(kind, logicalId)] = resourcePath;
        }

        public bool TryResolve(ClientAssetKind kind, string logicalId, out ClientResolvedAssetRef asset)
        {
            if (kind != ClientAssetKind.Unknown &&
                !string.IsNullOrWhiteSpace(logicalId) &&
                _assetPaths.TryGetValue((kind, logicalId), out string? path))
            {
                asset = new ClientResolvedAssetRef(path);
                return true;
            }

            asset = default;
            return false;
        }

        public bool TryLoad<T>(ClientAssetKind kind, string logicalId, out T? resource) where T : Resource
        {
            resource = null;
            if (!TryResolve(kind, logicalId, out ClientResolvedAssetRef resolved) || resolved.IsEmpty)
                return false;

            resource = GD.Load<T>(resolved.Value);
            return resource != null;
        }
    }
}
