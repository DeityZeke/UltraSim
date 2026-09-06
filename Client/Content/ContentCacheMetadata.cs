using System.Buffers.Binary;
using System.Text;
using UltraSim.Game.Application;

namespace UltraSim.Client.Content;

internal readonly record struct ContentCacheActivationRecord(
    ContentActivationPlan Plan,
    ContentHash ResolvedCatalogFingerprint,
    bool PersistentlyPinned,
    PackageManifestIdentity[] SelectedPackages);

/// <summary>Canonical private cache metadata. These bytes carry no source URLs or executable data.</summary>
internal static class ContentCacheMetadata
{
    private const uint FormatVersion = 1;
    private const ulong ManifestMagic = 0x314E414D435355UL; // USCMAN1
    private const ulong ActivationMagic = 0x31544341435355UL; // USCACT1
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static byte[] EncodeManifest(ContentPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true);
        writer.Write(ManifestMagic);
        writer.Write(FormatVersion);
        WriteHash(writer, manifest.ManifestHash);
        writer.Write(manifest.ManifestVersion);
        writer.Write(manifest.MinimumPipelineFormat);
        writer.Write(manifest.MaximumPipelineFormat);
        WriteString(writer, manifest.PackageId.Value!);
        WriteString(writer, manifest.Namespace.Value!);
        WriteString(writer, manifest.Name);
        WriteString(writer, manifest.Version);
        writer.Write((byte)manifest.TrustClass);
        writer.Write(manifest.Files.Count);
        foreach (ContentFileDescriptor file in manifest.Files)
        {
            WriteString(writer, file.Path.Value!);
            writer.Write((byte)file.Kind);
            writer.Write(file.SizeBytes);
            WriteHash(writer, file.Sha256);
        }
        writer.Write(manifest.Definitions.Count);
        foreach (ContentDefinitionDescriptor definition in manifest.Definitions)
        {
            WriteString(writer, definition.Id.Value!);
            writer.Write((byte)definition.Kind);
            writer.Write((byte)definition.Operation);
            if (definition.Kind == ContentDefinitionKind.Material)
            {
                writer.Write((byte)definition.Material.Family);
                writer.Write(definition.Material.AlbedoTexture.IsValid ? (byte)1 : (byte)0);
                if (definition.Material.AlbedoTexture.IsValid)
                    WriteString(writer, definition.Material.AlbedoTexture.Value!);
                writer.Write(BitConverter.SingleToInt32Bits(definition.Material.Roughness));
                writer.Write(BitConverter.SingleToInt32Bits(definition.Material.Metallic));
                writer.Write(BitConverter.SingleToInt32Bits(definition.Material.Emission));
            }
            else
            {
                WriteString(writer, definition.PrimaryFile.Value!);
            }
        }
        writer.Flush();
        return stream.ToArray();
    }

    internal static bool TryDecodeManifest(
        ReadOnlySpan<byte> bytes,
        ContentHash expectedHash,
        ContentLimits limits,
        out ContentPackageManifest? manifest)
    {
        manifest = null;
        limits = ContentLimits.Resolve(limits);
        var reader = new CacheMetadataReader(bytes);
        if (!reader.TryReadUInt64(out ulong magic) || magic != ManifestMagic ||
            !reader.TryReadUInt32(out uint version) || version != FormatVersion ||
            !reader.TryReadHash(out ContentHash storedHash) || storedHash != expectedHash ||
            !reader.TryReadInt32(out int manifestVersion) ||
            manifestVersion != ContentLimits.CurrentManifestVersion ||
            !reader.TryReadInt32(out int minimumPipeline) ||
            !reader.TryReadInt32(out int maximumPipeline) ||
            !reader.TryReadString(ContentPackageId.MaximumCharacters, out string packageText) ||
            !ContentPackageId.TryCreate(packageText, out ContentPackageId packageId) ||
            !reader.TryReadString(ContentNamespaceId.MaximumCharacters, out string namespaceText) ||
            !ContentNamespaceId.TryCreate(namespaceText, out ContentNamespaceId contentNamespace) ||
            !reader.TryReadString(128, out string name) ||
            !reader.TryReadString(48, out string packageVersion) ||
            !reader.TryReadByte(out byte trustValue) ||
            !reader.TryReadInt32(out int fileCount) ||
            fileCount < 0 || fileCount > limits.MaximumFilesPerPackage)
            return false;

        var files = new ContentFileDescriptor[fileCount];
        for (int index = 0; index < files.Length; index++)
        {
            if (!reader.TryReadString(ContentFilePath.MaximumCharacters, out string pathText) ||
                !ContentFilePath.TryCreate(pathText, out ContentFilePath path) ||
                !reader.TryReadByte(out byte kindValue) ||
                !reader.TryReadInt64(out long size) ||
                !reader.TryReadHash(out ContentHash hash) ||
                !ContentFileDescriptor.TryCreate(path, (ContentFileKind)kindValue, size, hash,
                    limits, out files[index]))
                return false;
        }

        if (!reader.TryReadInt32(out int definitionCount) || definitionCount < 0 ||
            definitionCount > limits.MaximumDefinitionsPerPackage)
            return false;
        var definitions = new ContentDefinitionDescriptor[definitionCount];
        for (int index = 0; index < definitions.Length; index++)
        {
            if (!reader.TryReadString(LogicalContentId.MaximumCharacters, out string idText) ||
                !LogicalContentId.TryCreate(idText, out LogicalContentId id) ||
                !reader.TryReadByte(out byte kindValue) ||
                !reader.TryReadByte(out byte operationValue))
                return false;
            var kind = (ContentDefinitionKind)kindValue;
            var operation = (ContentDefinitionOperation)operationValue;
            if (kind == ContentDefinitionKind.Material)
            {
                if (!reader.TryReadByte(out byte familyValue) ||
                    !reader.TryReadByte(out byte hasAlbedo) || hasAlbedo > 1)
                    return false;
                LogicalContentId albedo = default;
                if (hasAlbedo != 0 &&
                    (!reader.TryReadString(LogicalContentId.MaximumCharacters, out string albedoText) ||
                     !LogicalContentId.TryCreate(albedoText, out albedo)))
                    return false;
                if (!reader.TryReadInt32(out int roughnessBits) ||
                    !reader.TryReadInt32(out int metallicBits) ||
                    !reader.TryReadInt32(out int emissionBits) ||
                    !ContentMaterialDefinition.TryCreate(
                        (ContentMaterialFamily)familyValue,
                        albedo,
                        BitConverter.Int32BitsToSingle(roughnessBits),
                        BitConverter.Int32BitsToSingle(metallicBits),
                        BitConverter.Int32BitsToSingle(emissionBits),
                        out ContentMaterialDefinition material) ||
                    !ContentDefinitionDescriptor.TryCreateMaterial(
                        id, operation, material, out ContentDefinitionDescriptor? definition) ||
                    definition is null)
                    return false;
                definitions[index] = definition;
            }
            else
            {
                if (!reader.TryReadString(ContentFilePath.MaximumCharacters, out string pathText) ||
                    !ContentFilePath.TryCreate(pathText, out ContentFilePath path) ||
                    !ContentDefinitionDescriptor.TryCreateFileDefinition(
                        id, kind, operation, path, out ContentDefinitionDescriptor? definition) ||
                    definition is null)
                    return false;
                definitions[index] = definition;
            }
        }

        if (!reader.IsComplete ||
            !ContentPackageManifest.TryCreate(
                minimumPipeline, maximumPipeline, packageId, contentNamespace, name,
                packageVersion, (ContentTrustClass)trustValue, files, definitions,
                out manifest, out _, limits) ||
            manifest!.ManifestHash != expectedHash ||
            !bytes.SequenceEqual(EncodeManifest(manifest)))
        {
            manifest = null;
            return false;
        }
        return true;
    }

    internal static byte[] EncodeActivation(in ContentCacheActivationRecord record)
    {
        ContentActivationPlan plan = record.Plan;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true);
        writer.Write(ActivationMagic);
        writer.Write(FormatVersion);
        writer.Write(record.PersistentlyPinned ? (byte)1 : (byte)0);
        writer.Write(plan.Realm.High);
        writer.Write(plan.Realm.Low);
        writer.Write(plan.Activation.High);
        writer.Write(plan.Activation.Low);
        WriteHash(writer, plan.RequiredBaseCatalogFingerprint);
        WriteHash(writer, plan.PlanHash);
        WriteHash(writer, record.ResolvedCatalogFingerprint);
        writer.Write(plan.Packages.Count);
        foreach (ContentActivationPackage package in plan.Packages)
        {
            WriteString(writer, package.PackageId.Value!);
            WriteHash(writer, package.ManifestHash);
            writer.Write((byte)package.Requirement);
        }
        writer.Write(record.SelectedPackages.Length);
        foreach (PackageManifestIdentity package in record.SelectedPackages)
        {
            WriteString(writer, package.PackageId.Value!);
            WriteHash(writer, package.ManifestHash);
        }
        writer.Flush();
        return stream.ToArray();
    }

    internal static bool TryDecodeActivation(
        ReadOnlySpan<byte> bytes,
        ContentRealmActivationId expectedIdentity,
        ContentHash expectedPlanHash,
        ContentLimits limits,
        out ContentCacheActivationRecord record)
    {
        record = default;
        limits = ContentLimits.Resolve(limits);
        var reader = new CacheMetadataReader(bytes);
        if (!reader.TryReadUInt64(out ulong magic) || magic != ActivationMagic ||
            !reader.TryReadUInt32(out uint version) || version != FormatVersion ||
            !reader.TryReadByte(out byte pinned) || pinned > 1 ||
            !reader.TryReadUInt64(out ulong realmHigh) ||
            !reader.TryReadUInt64(out ulong realmLow) ||
            !reader.TryReadUInt64(out ulong activationHigh) ||
            !reader.TryReadUInt64(out ulong activationLow) ||
            !reader.TryReadHash(out ContentHash baseFingerprint) ||
            !reader.TryReadHash(out ContentHash storedPlanHash) ||
            !reader.TryReadHash(out ContentHash resolvedFingerprint) ||
            expectedPlanHash.IsValid && storedPlanHash != expectedPlanHash ||
            !reader.TryReadInt32(out int packageCount) || packageCount < 0 ||
            packageCount > limits.MaximumPackages)
            return false;
        ApplicationRealmId realm;
        ContentActivationId activation;
        try
        {
            realm = new ApplicationRealmId(realmHigh, realmLow);
            activation = new ContentActivationId(activationHigh, activationLow);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        if (new ContentRealmActivationId(realm, activation) != expectedIdentity)
            return false;
        var packages = new ContentActivationPackage[packageCount];
        for (int index = 0; index < packages.Length; index++)
        {
            if (!reader.TryReadString(ContentPackageId.MaximumCharacters, out string packageText) ||
                !ContentPackageId.TryCreate(packageText, out ContentPackageId packageId) ||
                !reader.TryReadHash(out ContentHash manifestHash) ||
                !reader.TryReadByte(out byte requirementValue))
                return false;
            packages[index] = new ContentActivationPackage(
                packageId, manifestHash, (ContentRequirementPolicy)requirementValue);
        }
        if (!reader.TryReadInt32(out int selectedCount) || selectedCount < 0 ||
            selectedCount > packageCount)
            return false;
        var selected = new PackageManifestIdentity[selectedCount];
        int planCursor = 0;
        for (int index = 0; index < selected.Length; index++)
        {
            if (!reader.TryReadString(ContentPackageId.MaximumCharacters, out string packageText) ||
                !ContentPackageId.TryCreate(packageText, out ContentPackageId packageId) ||
                !reader.TryReadHash(out ContentHash manifestHash))
                return false;
            while (planCursor < packages.Length &&
                   (packages[planCursor].PackageId != packageId ||
                    packages[planCursor].ManifestHash != manifestHash))
                planCursor++;
            if (planCursor == packages.Length) return false;
            selected[index] = new PackageManifestIdentity(packageId, manifestHash);
            planCursor++;
        }
        foreach (ContentActivationPackage package in packages)
            if (package.Requirement == ContentRequirementPolicy.Required &&
                !selected.Contains(new PackageManifestIdentity(package.PackageId, package.ManifestHash)))
                return false;
        if (!reader.IsComplete ||
            !ContentActivationPlan.TryCreate(
                realm, activation, baseFingerprint, packages, out ContentActivationPlan? plan,
                out _, limits) || plan!.PlanHash != storedPlanHash)
            return false;
        record = new ContentCacheActivationRecord(plan, resolvedFingerprint, pinned != 0, selected);
        if (!bytes.SequenceEqual(EncodeActivation(record)))
        {
            record = default;
            return false;
        }
        return true;
    }

    private static void WriteHash(BinaryWriter writer, ContentHash hash)
    {
        Span<byte> digest = stackalloc byte[ContentHash.ByteLength];
        hash.WriteDigest(digest);
        writer.Write(digest);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = StrictUtf8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private ref struct CacheMetadataReader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _offset;

        internal CacheMetadataReader(ReadOnlySpan<byte> bytes) => _bytes = bytes;
        internal bool IsComplete => _offset == _bytes.Length;

        internal bool TryReadByte(out byte value)
        {
            value = 0;
            if (_offset >= _bytes.Length) return false;
            value = _bytes[_offset++];
            return true;
        }

        internal bool TryReadInt32(out int value)
        {
            value = 0;
            if (!TryTake(sizeof(int), out ReadOnlySpan<byte> bytes)) return false;
            value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
            return true;
        }

        internal bool TryReadUInt32(out uint value)
        {
            value = 0;
            if (!TryTake(sizeof(uint), out ReadOnlySpan<byte> bytes)) return false;
            value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            return true;
        }

        internal bool TryReadInt64(out long value)
        {
            value = 0;
            if (!TryTake(sizeof(long), out ReadOnlySpan<byte> bytes)) return false;
            value = BinaryPrimitives.ReadInt64LittleEndian(bytes);
            return true;
        }

        internal bool TryReadUInt64(out ulong value)
        {
            value = 0;
            if (!TryTake(sizeof(ulong), out ReadOnlySpan<byte> bytes)) return false;
            value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            return true;
        }

        internal bool TryReadHash(out ContentHash hash)
        {
            hash = default;
            if (!TryTake(ContentHash.ByteLength, out ReadOnlySpan<byte> digest)) return false;
            try { hash = ContentHash.FromDigest(digest); }
            catch (ArgumentException) { return false; }
            return true;
        }

        internal bool TryReadString(int maximumCharacters, out string value)
        {
            value = string.Empty;
            if (!TryReadInt32(out int byteCount) || byteCount < 0 ||
                byteCount > checked(maximumCharacters * 4) ||
                !TryTake(byteCount, out ReadOnlySpan<byte> bytes))
                return false;
            try { value = StrictUtf8.GetString(bytes); }
            catch (DecoderFallbackException) { return false; }
            return value.Length <= maximumCharacters;
        }

        private bool TryTake(int length, out ReadOnlySpan<byte> result)
        {
            result = default;
            if (length < 0 || length > _bytes.Length - _offset) return false;
            result = _bytes.Slice(_offset, length);
            _offset += length;
            return true;
        }
    }
}
