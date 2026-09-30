using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using UltraSim.Game.Application;

namespace UltraSim.Client.Content;

internal static class ContentCanonicalHash
{
    internal static ContentHash ComputeDefinition(ContentDefinitionDescriptor definition)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "ultrasim.content-definition.v1");
        AppendByte(hash, (byte)definition.Kind);
        switch (definition.Kind)
        {
            case ContentDefinitionKind.Texture:
            case ContentDefinitionKind.Icon:
            case ContentDefinitionKind.WorldPack:
            case ContentDefinitionKind.ActorPresentation:
            case ContentDefinitionKind.WorldStructuralManifest:
            case ContentDefinitionKind.AudioClip:
                AppendString(hash, definition.PrimaryFile.Value!);
                break;
            case ContentDefinitionKind.Material:
                AppendByte(hash, (byte)definition.Material.Family);
                AppendBool(hash, definition.Material.AlbedoTexture.IsValid);
                if (definition.Material.AlbedoTexture.IsValid)
                    AppendString(hash, definition.Material.AlbedoTexture.Value!);
                AppendInt32(hash, BitConverter.SingleToInt32Bits(definition.Material.Roughness));
                AppendInt32(hash, BitConverter.SingleToInt32Bits(definition.Material.Metallic));
                AppendInt32(hash, BitConverter.SingleToInt32Bits(definition.Material.Emission));
                break;
            default:
                throw new InvalidOperationException("The content definition kind is not canonical.");
        }
        return Finish(hash);
    }

    internal static ContentHash ComputeManifest(ContentPackageManifest manifest)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "ultrasim.content-manifest.v1");
        AppendInt32(hash, manifest.ManifestVersion);
        AppendInt32(hash, manifest.MinimumPipelineFormat);
        AppendInt32(hash, manifest.MaximumPipelineFormat);
        AppendString(hash, manifest.PackageId.Value!);
        AppendString(hash, manifest.Namespace.Value!);
        AppendString(hash, manifest.Name);
        AppendString(hash, manifest.Version);
        AppendByte(hash, (byte)manifest.TrustClass);
        AppendInt32(hash, manifest.Files.Count);
        foreach (ContentFileDescriptor file in manifest.Files)
        {
            AppendString(hash, file.Path.Value!);
            AppendByte(hash, (byte)file.Kind);
            AppendInt64(hash, file.SizeBytes);
            AppendHash(hash, file.Sha256);
        }
        AppendInt32(hash, manifest.Definitions.Count);
        foreach (ContentDefinitionDescriptor definition in manifest.Definitions)
        {
            AppendString(hash, definition.Id.Value!);
            AppendByte(hash, (byte)definition.Kind);
            AppendByte(hash, (byte)definition.Operation);
            AppendHash(hash, definition.DefinitionHash);
        }
        return Finish(hash);
    }

    internal static ContentHash ComputeBaseCatalog(IEnumerable<ResolvedContentEntry> entries)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "ultrasim.base-content-catalog.v1");
        AppendEntries(hash, entries);
        return Finish(hash);
    }

    internal static ContentHash ComputePlan(ContentActivationPlan plan)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "ultrasim.content-activation-plan.v1");
        AppendScope(hash, plan.Scope);
        AppendUInt64(hash, plan.Activation.High);
        AppendUInt64(hash, plan.Activation.Low);
        AppendHash(hash, plan.RequiredBaseCatalogFingerprint);
        AppendInt32(hash, plan.Packages.Count);
        foreach (ContentActivationPackage package in plan.Packages)
        {
            AppendString(hash, package.PackageId.Value!);
            AppendHash(hash, package.ManifestHash);
            AppendByte(hash, (byte)package.Requirement);
        }
        return Finish(hash);
    }

    internal static ContentHash ComputeResolvedCatalog(
        ContentActivationPlan plan,
        IEnumerable<ResolvedContentEntry> entries)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "ultrasim.resolved-content-catalog.v1");
        AppendScope(hash, plan.Scope);
        AppendUInt64(hash, plan.Activation.High);
        AppendUInt64(hash, plan.Activation.Low);
        AppendHash(hash, plan.PlanHash);
        AppendHash(hash, plan.RequiredBaseCatalogFingerprint);
        AppendEntries(hash, entries);
        return Finish(hash);
    }

    private static void AppendEntries(IncrementalHash hash, IEnumerable<ResolvedContentEntry> source)
    {
        ResolvedContentEntry[] entries = source.OrderBy(value => value.Id).ToArray();
        AppendInt32(hash, entries.Length);
        foreach (ResolvedContentEntry entry in entries)
        {
            AppendString(hash, entry.Id.Value!);
            AppendByte(hash, (byte)entry.Kind);
            AppendString(hash, entry.OwnerPackageId.Value!);
            AppendHash(hash, entry.OwnerManifestHash);
            AppendHash(hash, entry.Definition.DefinitionHash);
            AppendInt32(hash, entry.ReferencedFiles.Count);
            foreach (ContentHash file in entry.ReferencedFiles.Order()) AppendHash(hash, file);
        }
    }

    private static void AppendRealm(IncrementalHash hash, ApplicationRealmId realm)
    {
        AppendUInt64(hash, realm.High);
        AppendUInt64(hash, realm.Low);
    }

    private static void AppendScope(IncrementalHash hash, ContentScopeKey scope)
    {
        if (!scope.IsValid) throw new InvalidOperationException("The content scope is not canonical.");
        // Realm bytes remain exactly v1-compatible. The otherwise-invalid all-zero realm encoding
        // is the private canonical marker for the reserved Local scope.
        if (scope.IsLocal)
        {
            AppendUInt64(hash, 0);
            AppendUInt64(hash, 0);
            return;
        }
        AppendRealm(hash, scope.Realm);
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, utf8.Length);
        hash.AppendData(utf8);
    }

    private static void AppendHash(IncrementalHash hash, ContentHash value)
    {
        Span<byte> bytes = stackalloc byte[ContentHash.ByteLength];
        value.WriteDigest(bytes);
        hash.AppendData(bytes);
    }

    private static void AppendBool(IncrementalHash hash, bool value) => AppendByte(hash, value ? (byte)1 : (byte)0);
    private static void AppendByte(IncrementalHash hash, byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        bytes[0] = value;
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendUInt64(IncrementalHash hash, ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static ContentHash Finish(IncrementalHash hash)
    {
        Span<byte> digest = stackalloc byte[ContentHash.ByteLength];
        if (!hash.TryGetHashAndReset(digest, out int written) || written != digest.Length)
            throw new InvalidOperationException("SHA-256 did not produce a complete digest.");
        return ContentHash.FromDigest(digest);
    }
}
