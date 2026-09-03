using System.Buffers.Binary;
using System.Collections.ObjectModel;
using UltraSim.Game.World;

namespace UltraSim.Client.Content;

/// <summary>Immutable, bounded passive package manifest with canonical semantic identity.</summary>
public sealed class ContentPackageManifest
{
    private readonly ReadOnlyCollection<ContentFileDescriptor> _files;
    private readonly ReadOnlyCollection<ContentDefinitionDescriptor> _definitions;

    private ContentPackageManifest(
        int minimumPipelineFormat,
        int maximumPipelineFormat,
        ContentPackageId packageId,
        ContentNamespaceId contentNamespace,
        string name,
        string version,
        ContentTrustClass trustClass,
        ContentFileDescriptor[] files,
        ContentDefinitionDescriptor[] definitions)
    {
        ManifestVersion = ContentLimits.CurrentManifestVersion;
        MinimumPipelineFormat = minimumPipelineFormat;
        MaximumPipelineFormat = maximumPipelineFormat;
        PackageId = packageId;
        Namespace = contentNamespace;
        Name = name;
        Version = version;
        TrustClass = trustClass;
        _files = Array.AsReadOnly(files);
        _definitions = Array.AsReadOnly(definitions);
        ManifestHash = ContentCanonicalHash.ComputeManifest(this);
    }

    public int ManifestVersion { get; }
    public int MinimumPipelineFormat { get; }
    public int MaximumPipelineFormat { get; }
    public ContentPackageId PackageId { get; }
    public ContentNamespaceId Namespace { get; }
    public string Name { get; }
    public string Version { get; }
    public ContentTrustClass TrustClass { get; }
    public ReadOnlyCollection<ContentFileDescriptor> Files => _files;
    public ReadOnlyCollection<ContentDefinitionDescriptor> Definitions => _definitions;
    public ContentHash ManifestHash { get; }

    public static bool TryCreate(
        int minimumPipelineFormat,
        int maximumPipelineFormat,
        ContentPackageId packageId,
        ContentNamespaceId contentNamespace,
        string? name,
        string? version,
        ContentTrustClass trustClass,
        IEnumerable<ContentFileDescriptor>? files,
        IEnumerable<ContentDefinitionDescriptor>? definitions,
        out ContentPackageManifest? manifest,
        out string? error,
        ContentLimits limits = default)
    {
        manifest = null;
        error = null;
        limits = ContentLimits.Resolve(limits);
        if (minimumPipelineFormat <= 0 || maximumPipelineFormat < minimumPipelineFormat ||
            minimumPipelineFormat > ContentLimits.CurrentPipelineFormat ||
            maximumPipelineFormat < ContentLimits.CurrentPipelineFormat)
            return Fail("The package does not support the current content pipeline format.", out error);
        if (!packageId.IsValid || !contentNamespace.IsValid)
            return Fail("The package or namespace identity is invalid.", out error);
        if (!ValidDisplay(name, 128) || !ValidDisplay(version, 48))
            return Fail("The package name or version is invalid.", out error);
        if (trustClass is not (ContentTrustClass.BuiltIn or ContentTrustClass.ServerAuthorizedPassive))
            return Fail("Only built-in and server-authorized passive packages use this manifest.", out error);

        if (!ContentBoundedCopy.TryCopy(files, limits.MaximumFilesPerPackage,
                out ContentFileDescriptor[] orderedFiles) ||
            !ContentBoundedCopy.TryCopy(definitions, limits.MaximumDefinitionsPerPackage,
                out ContentDefinitionDescriptor[] orderedDefinitions) ||
            orderedDefinitions.Any(static value => value is null))
            return Fail("The package exceeds its bounded manifest capacity.", out error);
        Array.Sort(orderedFiles, static (left, right) => left.Path.CompareTo(right.Path));
        Array.Sort(orderedDefinitions, static (left, right) => left.Id.CompareTo(right.Id));

        long totalBytes = 0;
        for (int index = 0; index < orderedFiles.Length; index++)
        {
            ContentFileDescriptor file = orderedFiles[index];
            if (!file.IsValid || file.SizeBytes > limits.MaximumFileBytes ||
                index != 0 && orderedFiles[index - 1].Path == file.Path)
                return Fail("The package contains an invalid or duplicate file descriptor.", out error);
            try { totalBytes = checked(totalBytes + file.SizeBytes); }
            catch (OverflowException) { return Fail("The package byte total overflowed.", out error); }
            if (totalBytes > limits.MaximumPackageBytes)
                return Fail("The package exceeds its byte limit.", out error);
        }

        for (int index = 0; index < orderedDefinitions.Length; index++)
        {
            ContentDefinitionDescriptor? definition = orderedDefinitions[index];
            if (definition is null || !definition.Id.IsValid || !definition.DefinitionHash.IsValid ||
                index != 0 && orderedDefinitions[index - 1].Id == definition.Id)
                return Fail("The package contains an invalid or duplicate definition.", out error);
            if (definition.Operation == ContentDefinitionOperation.Add &&
                definition.Id.Namespace != contentNamespace)
                return Fail("An Add definition must use the package's authorized namespace.", out error);
            if (definition.PrimaryFile.IsValid)
            {
                if (!TryFindFile(orderedFiles, definition.PrimaryFile, out ContentFileDescriptor file))
                    return Fail("A definition references an undeclared package file.", out error);
                bool kindMatches = definition.Kind switch
                {
                    ContentDefinitionKind.Texture or ContentDefinitionKind.Icon =>
                        file.Kind is ContentFileKind.PngTexture or ContentFileKind.WebpTexture,
                    ContentDefinitionKind.WorldPack => file.Kind == ContentFileKind.WorldPack,
                    _ => false,
                };
                if (!kindMatches)
                    return Fail("A file-backed definition does not match its declared passive file kind.", out error);
            }
        }

        manifest = new ContentPackageManifest(
            minimumPipelineFormat, maximumPipelineFormat, packageId, contentNamespace,
            name!, version!, trustClass, orderedFiles, orderedDefinitions);
        return true;
    }

    public bool TryGetFile(ContentFilePath path, out ContentFileDescriptor descriptor)
    {
        int low = 0, high = _files.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int comparison = _files[middle].Path.CompareTo(path);
            if (comparison == 0)
            {
                descriptor = _files[middle];
                return true;
            }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        descriptor = default;
        return false;
    }

    private static bool TryFindFile(
        ReadOnlySpan<ContentFileDescriptor> files,
        ContentFilePath path,
        out ContentFileDescriptor descriptor)
    {
        int low = 0, high = files.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int comparison = files[middle].Path.CompareTo(path);
            if (comparison == 0)
            {
                descriptor = files[middle];
                return true;
            }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        descriptor = default;
        return false;
    }

    private static bool ValidDisplay(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value != value.Trim()) return false;
        foreach (char character in value)
            if (char.IsControl(character)) return false;
        return true;
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}

public static class ContentPackageValidator
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool TryValidatePayloads(
        ContentPackageManifest manifest,
        IEnumerable<ContentFilePayload>? payloads,
        out VerifiedContentPackage? package,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        package = null;
        if (!ContentBoundedCopy.TryCopy(payloads, manifest.Files.Count, out ContentFilePayload[] ordered))
            return Fail("The payload set exceeds the manifest file count.", out error);
        Array.Sort(ordered, static (left, right) => left.Path.CompareTo(right.Path));
        if (ordered.Length != manifest.Files.Count)
            return Fail("The payload set does not exactly match the manifest file count.", out error);
        for (int index = 0; index < ordered.Length; index++)
        {
            ContentFilePayload payload = ordered[index];
            ContentFileDescriptor expected = manifest.Files[index];
            if (payload.Path != expected.Path || payload.Bytes.Length != expected.SizeBytes ||
                ContentHash.Compute(payload.Bytes.Span) != expected.Sha256)
                return Fail("A package payload does not match its exact path, size, or SHA-256 descriptor.", out error);
            if (!EnvelopeMatches(expected.Kind, payload.Bytes.Span))
                return Fail("A package payload does not match its declared passive format.", out error);
        }
        package = new VerifiedContentPackage(manifest);
        error = null;
        return true;
    }

    private static bool EnvelopeMatches(ContentFileKind kind, ReadOnlySpan<byte> bytes) => kind switch
    {
        ContentFileKind.PngTexture => bytes.Length >= 24 && bytes[..8].SequenceEqual(PngSignature) &&
            bytes[12..16].SequenceEqual("IHDR"u8) &&
            BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]) is > 0 and <= 16_384 &&
            BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]) is > 0 and <= 16_384,
        ContentFileKind.WebpTexture => bytes.Length >= 16 && bytes[..4].SequenceEqual("RIFF"u8) &&
            bytes[8..12].SequenceEqual("WEBP"u8) &&
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) + 8UL == (ulong)bytes.Length,
        ContentFileKind.WorldPack => ValidateCompleteWorldPack(bytes),
        _ => false,
    };

    private static bool ValidateCompleteWorldPack(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < WorldPackFormat.HeaderBytes) return false;
        ulong contentLow = BinaryPrimitives.ReadUInt64LittleEndian(bytes[48..]);
        ulong contentHigh = BinaryPrimitives.ReadUInt64LittleEndian(bytes[56..]);
        if (contentLow == 0 && contentHigh == 0) return false;
        var expectedContent = new WorldContentFingerprint(contentLow, contentHigh);
        return WorldPack.TryOpen(bytes, WorldPackLimits.Default, expectedContent, out _, out _);
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}

/// <summary>
/// Capability produced only after every manifest payload has matched its exact descriptor and
/// passive-format validator. Catalog publication accepts this type rather than a bare manifest.
/// </summary>
public sealed class VerifiedContentPackage
{
    internal VerifiedContentPackage(ContentPackageManifest manifest) => Manifest = manifest;
    public ContentPackageManifest Manifest { get; }
    public ContentPackageId PackageId => Manifest.PackageId;
    public ContentHash ManifestHash => Manifest.ManifestHash;
}
