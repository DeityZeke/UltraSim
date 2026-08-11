namespace UltraSim.Client.Content;

public static class ContentPackManifestValidator
{
    private static readonly HashSet<string> AllowedFileKinds = new(StringComparer.Ordinal)
    {
        "model",
        "texture",
        "material",
        "audio",
        "icon",
        "ui",
        "metadata"
    };

    private static readonly HashSet<string> AllowedDefinitionKinds = new(StringComparer.Ordinal)
    {
        ContentPackDefinitionKind.ItemVisual,
        ContentPackDefinitionKind.Mobile,
        ContentPackDefinitionKind.Material,
        ContentPackDefinitionKind.Texture,
        ContentPackDefinitionKind.Model,
        ContentPackDefinitionKind.Icon,
        ContentPackDefinitionKind.UiSurface,
        ContentPackDefinitionKind.SoundEvent
    };

    private static readonly HashSet<string> AllowedDefinitionModes = new(StringComparer.Ordinal)
    {
        ContentPackDefinitionMode.Append,
        ContentPackDefinitionMode.Replace,
        ContentPackDefinitionMode.Patch
    };

    private static readonly HashSet<string> AllowedHueChannels = new(StringComparer.Ordinal)
    {
        ContentPackHueChannel.Main,
        ContentPackHueChannel.Secondary,
        ContentPackHueChannel.Trim
    };

    private static readonly HashSet<string> BlockedFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat",
        ".cmd",
        ".com",
        ".dll",
        ".dylib",
        ".exe",
        ".gd",
        ".pdb",
        ".ps1",
        ".py",
        ".sh",
        ".so"
    };

    public static bool TryValidate(ContentPackManifest manifest, out string error)
    {
        if (manifest is null)
        {
            error = "Content pack manifest is required.";
            return false;
        }

        if (!string.Equals(manifest.Schema, ContentPackManifest.CurrentSchema, StringComparison.Ordinal))
        {
            error = $"Unsupported content pack schema '{manifest.Schema}'.";
            return false;
        }

        if (!TryValidatePackInfo(manifest.Pack, out error))
        {
            return false;
        }

        if (manifest.Pack.MinPipelineFormat > ContentPackManifest.CurrentPipelineFormat ||
            manifest.Pack.MaxPipelineFormat < ContentPackManifest.CurrentPipelineFormat)
        {
            error = $"Content pack '{manifest.Pack.Id}' does not support pipeline format {ContentPackManifest.CurrentPipelineFormat}.";
            return false;
        }

        var filePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ContentPackFileEntry file in manifest.Files ?? Array.Empty<ContentPackFileEntry>())
        {
            if (!TryValidateFileEntry(file, filePaths, out error))
            {
                return false;
            }
        }

        var definitionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ContentPackDefinitionEntry definition in manifest.Definitions ?? Array.Empty<ContentPackDefinitionEntry>())
        {
            if (!TryValidateDefinitionEntry(definition, filePaths, definitionIds, out error))
            {
                return false;
            }
        }

        foreach (ContentPackDependency dependency in manifest.Dependencies ?? Array.Empty<ContentPackDependency>())
        {
            if (!TryValidatePackId(dependency.Id, out error))
            {
                error = $"Invalid dependency id: {error}";
                return false;
            }
        }

        foreach (ContentPackFilter filter in manifest.Filters ?? Array.Empty<ContentPackFilter>())
        {
            if (!TryValidateFilter(filter, out error))
            {
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateDeclaredFiles(IContentPackSource source, ContentPackManifest manifest, out string error)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!TryValidate(manifest, out error))
        {
            return false;
        }

        foreach (ContentPackFileEntry file in manifest.Files)
        {
            if (!source.TryReadBytes(file.Path, out byte[] bytes, out error))
            {
                return false;
            }

            if (!ContentPathValidation.ValidateSizeAndSha256(bytes, file.SizeBytes, file.Sha256, out error))
            {
                error = $"Content pack file '{file.Path}' failed validation: {error}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidatePackInfo(ContentPackInfo pack, out string error)
    {
        if (pack is null)
        {
            error = "Content pack info is required.";
            return false;
        }

        if (!TryValidatePackId(pack.Id, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(pack.Name))
        {
            error = "Content pack name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(pack.Version))
        {
            error = "Content pack version is required.";
            return false;
        }

        if (pack.MinPipelineFormat <= 0 || pack.MaxPipelineFormat <= 0)
        {
            error = "Content pack pipeline formats must be positive.";
            return false;
        }

        if (pack.MinPipelineFormat > pack.MaxPipelineFormat)
        {
            error = "Content pack minimum pipeline format cannot exceed maximum pipeline format.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pack.ContentHash))
        {
            string hash = pack.ContentHash.Trim();
            if (!hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
                !ContentPathValidation.TryNormalizeSha256Hex(hash["sha256:".Length..], out _, out error))
            {
                error = "Content pack contentHash must be a sha256:<64 hex> value.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateFileEntry(ContentPackFileEntry file, HashSet<string> filePaths, out string error)
    {
        if (file is null)
        {
            error = "Content pack file entry is required.";
            return false;
        }

        if (!ContentPathValidation.TryNormalizeRelativePath(file.Path, out string normalizedPath, out error))
        {
            return false;
        }

        string extension = Path.GetExtension(normalizedPath);
        if (BlockedFileExtensions.Contains(extension))
        {
            error = $"Content pack file '{normalizedPath}' uses blocked extension '{extension}'.";
            return false;
        }

        if (!filePaths.Add(normalizedPath))
        {
            error = $"Content pack contains duplicate file path '{normalizedPath}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(file.Kind) || !AllowedFileKinds.Contains(file.Kind.Trim()))
        {
            error = $"Content pack file '{normalizedPath}' has unsupported kind '{file.Kind}'.";
            return false;
        }

        if (file.SizeBytes < 0)
        {
            error = $"Content pack file '{normalizedPath}' has a negative size.";
            return false;
        }

        if (!ContentPathValidation.TryNormalizeSha256Hex(file.Sha256, out _, out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateDefinitionEntry(
        ContentPackDefinitionEntry definition,
        HashSet<string> filePaths,
        HashSet<string> definitionIds,
        out string error)
    {
        if (definition is null)
        {
            error = "Content pack definition entry is required.";
            return false;
        }

        if (!ContentResolverLayer.TryNormalizeLogicalId(definition.Id, out string normalizedId, out error))
        {
            error = $"Invalid content pack definition id: {error}";
            return false;
        }

        if (!definitionIds.Add(normalizedId))
        {
            error = $"Content pack contains duplicate definition id '{normalizedId}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(definition.Kind) || !AllowedDefinitionKinds.Contains(definition.Kind.Trim()))
        {
            error = $"Content pack definition '{normalizedId}' has unsupported kind '{definition.Kind}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(definition.Mode) || !AllowedDefinitionModes.Contains(definition.Mode.Trim()))
        {
            error = $"Content pack definition '{normalizedId}' has unsupported mode '{definition.Mode}'.";
            return false;
        }

        if (!TryValidateReferencedFile(definition.Model, filePaths, normalizedId, "model", required: IsMeshVisualDefinition(definition), out error) ||
            !TryValidateReferencedFile(definition.Texture, filePaths, normalizedId, "texture", required: false, out error) ||
            !TryValidateReferencedFile(definition.Material, filePaths, normalizedId, "material", required: false, out error))
        {
            return false;
        }

        if (definition.VisualScale is not null &&
            (definition.VisualScale.X == 0.0f || definition.VisualScale.Y == 0.0f || definition.VisualScale.Z == 0.0f))
        {
            error = $"Content pack definition '{normalizedId}' visual scale components cannot be zero.";
            return false;
        }

        if (!TryValidateMaterialSlots(definition, filePaths, normalizedId, out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateMaterialSlots(
        ContentPackDefinitionEntry definition,
        HashSet<string> filePaths,
        string definitionId,
        out string error)
    {
        IReadOnlyList<ContentPackMaterialSlotEntry>? materialSlots = definition.MaterialSlots;
        if (materialSlots is null || materialSlots.Count == 0)
        {
            error = string.Empty;
            return true;
        }

        if (materialSlots.Count > 32)
        {
            error = $"Content pack definition '{definitionId}' cannot declare more than 32 material slots.";
            return false;
        }

        var slotIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < materialSlots.Count; i++)
        {
            ContentPackMaterialSlotEntry slot = materialSlots[i];
            if (slot is null)
            {
                error = $"Content pack definition '{definitionId}' material slot entry is required.";
                return false;
            }

            if (!TryValidateIdentifier(slot.SlotId, $"Content pack definition '{definitionId}' material slot id", out error))
            {
                return false;
            }

            string slotId = slot.SlotId.Trim();
            if (!slotIds.Add(slotId))
            {
                error = $"Content pack definition '{definitionId}' contains duplicate material slot '{slotId}'.";
                return false;
            }

            if (!ContentResolverLayer.TryNormalizeLogicalId(slot.Resource, out _, out error))
            {
                error = $"Content pack definition '{definitionId}' material slot '{slotId}' has invalid resource id: {error}";
                return false;
            }

            if (!TryValidateReferencedFile(slot.Texture, filePaths, definitionId, $"material slot '{slotId}' texture", required: false, out error) ||
                !TryValidateReferencedFile(slot.Material, filePaths, definitionId, $"material slot '{slotId}' material", required: false, out error))
            {
                return false;
            }

            string hueChannel = (slot.HueChannel ?? string.Empty).Trim();
            if (slot.DefaultHue.HasValue && hueChannel.Length == 0)
            {
                error = $"Content pack definition '{definitionId}' material slot '{slotId}' default hue requires a hue channel.";
                return false;
            }

            if (hueChannel.Length > 0 && !AllowedHueChannels.Contains(hueChannel))
            {
                error = $"Content pack definition '{definitionId}' material slot '{slotId}' has unsupported hue channel '{slot.HueChannel}'.";
                return false;
            }

            if (slot.DefaultHue is < 0 or > 65535)
            {
                error = $"Content pack definition '{definitionId}' material slot '{slotId}' default hue must be between 0 and 65535.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateReferencedFile(
        string? path,
        HashSet<string> filePaths,
        string definitionId,
        string fieldName,
        bool required,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (required)
            {
                error = $"Content pack definition '{definitionId}' requires a {fieldName} file reference.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        if (!ContentPathValidation.TryNormalizeRelativePath(path, out string normalizedPath, out error))
        {
            error = $"Content pack definition '{definitionId}' has invalid {fieldName} path: {error}";
            return false;
        }

        if (!filePaths.Contains(normalizedPath))
        {
            error = $"Content pack definition '{definitionId}' references undeclared {fieldName} file '{normalizedPath}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsMeshVisualDefinition(ContentPackDefinitionEntry definition)
    {
        string kind = definition.Kind?.Trim() ?? string.Empty;
        return string.Equals(kind, ContentPackDefinitionKind.Mobile, StringComparison.Ordinal) ||
            string.Equals(kind, ContentPackDefinitionKind.ItemVisual, StringComparison.Ordinal);
    }

    private static bool TryValidatePackId(string? packId, out string error)
    {
        string value = (packId ?? string.Empty).Trim();
        if (value.Length == 0 || value.Length > 96)
        {
            error = "Content pack id must be 1-96 characters.";
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            {
                continue;
            }

            error = "Content pack id can contain only letters, digits, '.', '_', or '-'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateFilter(ContentPackFilter filter, out string error)
    {
        if (filter is null)
        {
            error = "Content pack filter entry is required.";
            return false;
        }

        if (!TryValidateIdentifier(filter.Namespace, "Content pack filter namespace", out error))
        {
            return false;
        }

        string path = (filter.Path ?? string.Empty).Trim();
        if (path.Length == 0 || path.Length > 160)
        {
            error = "Content pack filter path must be 1-160 characters.";
            return false;
        }

        if (path.Any(char.IsControl))
        {
            error = "Content pack filter path cannot contain control characters.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateIdentifier(string? value, string label, out string error)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > 96)
        {
            error = $"{label} must be 1-96 characters.";
            return false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            {
                continue;
            }

            error = $"{label} can contain only letters, digits, '.', '_', or '-'.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
