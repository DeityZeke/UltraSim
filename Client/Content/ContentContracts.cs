using System.Collections.ObjectModel;

namespace UltraSim.Client.Content;

public readonly record struct ContentLimits
{
    public const int CurrentManifestVersion = 1;
    public const int CurrentPipelineFormat = 1;

    public ContentLimits(
        int maximumPackages,
        int maximumFilesPerPackage,
        int maximumDefinitionsPerPackage,
        int maximumSourcesPerPackage,
        long maximumFileBytes,
        long maximumPackageBytes)
    {
        if (maximumPackages is < 1 or > 256 || maximumFilesPerPackage is < 1 or > 16_384 ||
            maximumDefinitionsPerPackage is < 1 or > 65_536 || maximumSourcesPerPackage is < 1 or > 32 ||
            maximumFileBytes is < 1 or > 2L * 1024 * 1024 * 1024 ||
            maximumPackageBytes < maximumFileBytes || maximumPackageBytes > 8L * 1024 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumPackages));
        MaximumPackages = maximumPackages;
        MaximumFilesPerPackage = maximumFilesPerPackage;
        MaximumDefinitionsPerPackage = maximumDefinitionsPerPackage;
        MaximumSourcesPerPackage = maximumSourcesPerPackage;
        MaximumFileBytes = maximumFileBytes;
        MaximumPackageBytes = maximumPackageBytes;
    }

    public static ContentLimits Default { get; } = new(
        maximumPackages: 64,
        maximumFilesPerPackage: 2_048,
        maximumDefinitionsPerPackage: 4_096,
        maximumSourcesPerPackage: 8,
        maximumFileBytes: 512L * 1024 * 1024,
        maximumPackageBytes: 2L * 1024 * 1024 * 1024);

    public int MaximumPackages { get; }
    public int MaximumFilesPerPackage { get; }
    public int MaximumDefinitionsPerPackage { get; }
    public int MaximumSourcesPerPackage { get; }
    public long MaximumFileBytes { get; }
    public long MaximumPackageBytes { get; }

    internal static ContentLimits Resolve(ContentLimits limits) =>
        limits.MaximumPackages == 0 ? Default : limits;
}

public enum ContentTrustClass : byte
{
    BuiltIn = 1,
    ServerAuthorizedPassive = 2,
    UserResourcePack = 3,
    ExecutableExtension = 4,
}

public enum ContentFileKind : byte
{
    PngTexture = 1,
    WebpTexture = 2,
    WorldPack = 3,
}

public enum ContentDefinitionKind : byte
{
    Texture = 1,
    Icon = 2,
    Material = 3,
    WorldPack = 4,
}

public enum ContentDefinitionOperation : byte
{
    Add = 1,
    Replace = 2,
}

public enum ContentMaterialFamily : byte
{
    StandardLit = 1,
}

public enum ContentRequirementPolicy : byte
{
    Required = 1,
    Optional = 2,
}

public enum ContentSourceKind : byte
{
    Local = 1,
    ServerHostedStatic = 2,
    ExternalHttps = 3,
}

public enum ContentApplicationState : byte
{
    None = 0,
    Required = 1,
    PackagesVerified = 2,
    CatalogPublished = 3,
    Applied = 4,
    Rejected = 5,
}

public readonly record struct ContentFileDescriptor(
    ContentFilePath Path,
    ContentFileKind Kind,
    long SizeBytes,
    ContentHash Sha256)
{
    public bool IsValid => Path.IsValid && Kind is >= ContentFileKind.PngTexture and <= ContentFileKind.WorldPack &&
        SizeBytes >= 0 && Sha256.IsValid && ExtensionMatches(Path, Kind);

    public static bool TryCreate(
        ContentFilePath path,
        ContentFileKind kind,
        long sizeBytes,
        ContentHash sha256,
        ContentLimits limits,
        out ContentFileDescriptor descriptor)
    {
        limits = ContentLimits.Resolve(limits);
        descriptor = new ContentFileDescriptor(path, kind, sizeBytes, sha256);
        if (descriptor.IsValid && sizeBytes <= limits.MaximumFileBytes) return true;
        descriptor = default;
        return false;
    }

    private static bool ExtensionMatches(ContentFilePath path, ContentFileKind kind) => kind switch
    {
        ContentFileKind.PngTexture => path.Value!.EndsWith(".png", StringComparison.Ordinal),
        ContentFileKind.WebpTexture => path.Value!.EndsWith(".webp", StringComparison.Ordinal),
        ContentFileKind.WorldPack => path.Value!.EndsWith(".usworldpack", StringComparison.Ordinal),
        _ => false,
    };
}

public readonly record struct ContentMaterialDefinition(
    ContentMaterialFamily Family,
    LogicalContentId AlbedoTexture,
    float Roughness,
    float Metallic,
    float Emission)
{
    public bool IsValid => Family == ContentMaterialFamily.StandardLit &&
        float.IsFinite(Roughness) && Roughness is >= 0f and <= 1f &&
        float.IsFinite(Metallic) && Metallic is >= 0f and <= 1f &&
        float.IsFinite(Emission) && Emission is >= 0f and <= 64f;

    public static bool TryCreate(
        ContentMaterialFamily family,
        LogicalContentId albedoTexture,
        float roughness,
        float metallic,
        float emission,
        out ContentMaterialDefinition material)
    {
        if (roughness == 0f) roughness = 0f;
        if (metallic == 0f) metallic = 0f;
        if (emission == 0f) emission = 0f;
        material = new ContentMaterialDefinition(family, albedoTexture, roughness, metallic, emission);
        if (material.IsValid) return true;
        material = default;
        return false;
    }
}

public sealed class ContentDefinitionDescriptor
{
    private ContentDefinitionDescriptor(
        LogicalContentId id,
        ContentDefinitionKind kind,
        ContentDefinitionOperation operation,
        ContentFilePath primaryFile,
        ContentMaterialDefinition material)
    {
        Id = id;
        Kind = kind;
        Operation = operation;
        PrimaryFile = primaryFile;
        Material = material;
        DefinitionHash = ContentCanonicalHash.ComputeDefinition(this);
    }

    public LogicalContentId Id { get; }
    public ContentDefinitionKind Kind { get; }
    public ContentDefinitionOperation Operation { get; }
    public ContentFilePath PrimaryFile { get; }
    public ContentMaterialDefinition Material { get; }
    public ContentHash DefinitionHash { get; }

    public static bool TryCreateFileDefinition(
        LogicalContentId id,
        ContentDefinitionKind kind,
        ContentDefinitionOperation operation,
        ContentFilePath primaryFile,
        out ContentDefinitionDescriptor? definition)
    {
        definition = null;
        if (!id.IsValid || kind is not (ContentDefinitionKind.Texture or ContentDefinitionKind.Icon or
                ContentDefinitionKind.WorldPack) ||
            operation is not (ContentDefinitionOperation.Add or ContentDefinitionOperation.Replace) ||
            !primaryFile.IsValid) return false;
        definition = new ContentDefinitionDescriptor(id, kind, operation, primaryFile, default);
        return true;
    }

    public static bool TryCreateMaterial(
        LogicalContentId id,
        ContentDefinitionOperation operation,
        ContentMaterialDefinition material,
        out ContentDefinitionDescriptor? definition)
    {
        definition = null;
        if (!id.IsValid || operation is not (ContentDefinitionOperation.Add or ContentDefinitionOperation.Replace) ||
            !material.IsValid) return false;
        definition = new ContentDefinitionDescriptor(
            id, ContentDefinitionKind.Material, operation, default, material);
        return true;
    }
}

public readonly record struct ContentFilePayload(ContentFilePath Path, ReadOnlyMemory<byte> Bytes);

public sealed class ContentPackageAcquisition
{
    private readonly ReadOnlyCollection<ContentSourceDescriptor> _sources;

    public ContentPackageAcquisition(
        ContentPackageId package,
        ContentHash manifestHash,
        IEnumerable<ContentSourceDescriptor> sources,
        ContentLimits limits = default)
    {
        limits = ContentLimits.Resolve(limits);
        if (!package.IsValid || !manifestHash.IsValid) throw new ArgumentException("The package acquisition identity is invalid.");
        ArgumentNullException.ThrowIfNull(sources);
        if (!ContentBoundedCopy.TryCopy(sources, limits.MaximumSourcesPerPackage,
                out ContentSourceDescriptor[] copy) || copy.Length == 0 || copy.Any(value => !value.IsValid))
            throw new ArgumentException("The package source list is invalid.", nameof(sources));
        if (copy.Distinct().Count() != copy.Length)
            throw new ArgumentException("The package source list contains a duplicate.", nameof(sources));
        Package = package;
        ManifestHash = manifestHash;
        _sources = Array.AsReadOnly(copy);
    }

    public ContentPackageId Package { get; }
    public ContentHash ManifestHash { get; }
    public ReadOnlyCollection<ContentSourceDescriptor> Sources => _sources;
}

public readonly record struct ContentSourceDescriptor(ContentSourceKind Kind, string Locator)
{
    public const int MaximumLocatorCharacters = 2_048;

    public bool IsValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Locator) || Locator.Length > MaximumLocatorCharacters || Locator != Locator.Trim())
                return false;
            return Kind switch
            {
                ContentSourceKind.Local => ContentFilePath.TryCreate(Locator, out _),
                ContentSourceKind.ServerHostedStatic or ContentSourceKind.ExternalHttps =>
                    Uri.TryCreate(Locator, UriKind.Absolute, out Uri? uri) &&
                    uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) &&
                    string.IsNullOrEmpty(uri.Fragment),
                _ => false,
            };
        }
    }
}

public readonly record struct ContentBlobKey(ContentHash Sha256)
{
    public bool IsValid => Sha256.IsValid;
}

internal static class ContentBoundedCopy
{
    internal static bool TryCopy<T>(IEnumerable<T>? source, int maximum, out T[] values)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        if (source is null)
        {
            values = [];
            return true;
        }
        if (source is ICollection<T> collection && collection.Count > maximum ||
            source is IReadOnlyCollection<T> readOnly && readOnly.Count > maximum)
        {
            values = [];
            return false;
        }
        var bounded = new List<T>(Math.Min(maximum, 16));
        foreach (T value in source)
        {
            if (bounded.Count == maximum)
            {
                values = [];
                return false;
            }
            bounded.Add(value);
        }
        values = bounded.ToArray();
        return true;
    }
}
