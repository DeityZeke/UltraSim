using System.Collections.ObjectModel;
using UltraSim.Client.Content;
using UltraSim.Game.Application;

namespace UltraSim.Client.BuiltInTestWorlds;

/// <summary>Stable logical presentation identities used by the built-in Cabin Test World.</summary>
public static class CabinContentIds
{
    public static LogicalContentId WallTexture { get; } = Logical("cabin:texture/wall");
    public static LogicalContentId WoodTexture { get; } = Logical("cabin:texture/wood");
    public static LogicalContentId WallMaterial { get; } = Logical("cabin:material/wall");
    public static LogicalContentId WoodMaterial { get; } = Logical("cabin:material/wood");
    public static LogicalContentId TestWorldIcon { get; } = Logical("cabin:icon/test-world");
    public static LogicalContentId ChestIcon { get; } = Logical("cabin:icon/chest");

    public static LogicalContentId BlueGlowTexture { get; } = Logical("test-cabin:texture/blue-glow");
    public static LogicalContentId BlueGlowMaterial { get; } = Logical("test-cabin:material/blue-glow");
    public static LogicalContentId StreamedBadgeIcon { get; } = Logical("test-cabin:icon/streamed-badge");

    private static LogicalContentId Logical(string value)
    {
        if (!LogicalContentId.TryCreate(value, out LogicalContentId id))
            throw new InvalidOperationException($"The frozen Cabin logical id '{value}' is invalid.");
        return id;
    }
}

public enum CabinContentVariant : byte
{
    Default = 1,
    Replace = 2,
    Add = 3,
}

/// <summary>
/// One exact locally available Cabin package. Payload bytes remain separate from the verified
/// publishable capability so later cache/source work can replace their physical origin.
/// </summary>
public sealed class CabinLocalContentPackage
{
    private readonly ReadOnlyCollection<ContentFilePayload> _payloads;

    internal CabinLocalContentPackage(
        VerifiedContentPackage verified,
        ContentFilePayload[] payloads,
        ContentPackageAcquisition acquisition)
    {
        Verified = verified;
        _payloads = Array.AsReadOnly(payloads);
        Acquisition = acquisition;
    }

    public VerifiedContentPackage Verified { get; }
    public ContentPackageManifest Manifest => Verified.Manifest;
    public ReadOnlyCollection<ContentFilePayload> Payloads => _payloads;
    public ContentPackageAcquisition Acquisition { get; }

    public bool TryGetPayload(ContentFilePath path, out ReadOnlyMemory<byte> payload)
    {
        int low = 0;
        int high = _payloads.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int comparison = _payloads[middle].Path.CompareTo(path);
            if (comparison == 0)
            {
                payload = _payloads[middle].Bytes;
                return true;
            }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        payload = default;
        return false;
    }
}

/// <summary>One immutable resolved Cabin catalog and the exact activation that produced it.</summary>
public sealed class CabinResolvedContent
{
    internal CabinResolvedContent(
        CabinContentVariant variant,
        ContentActivationPlan plan,
        ResolvedContentCatalog catalog)
    {
        Variant = variant;
        Plan = plan;
        Catalog = catalog;
    }

    public CabinContentVariant Variant { get; }
    public ContentActivationPlan Plan { get; }
    public ResolvedContentCatalog Catalog { get; }
}

/// <summary>
/// Exact scope-authorized presentation-only form of the Cabin texture/material fixture. This is
/// consumed through the persistent Local store so Godot no longer reaches around the common
/// presentation generation to read oracle bytes directly.
/// </summary>
public sealed class CabinLocalPresentationContent
{
    private readonly ReadOnlyCollection<CabinLocalContentPackage> _packages;

    internal CabinLocalPresentationContent(
        CabinContentVariant variant,
        ContentActivationPlan plan,
        CabinLocalContentPackage[] packages)
    {
        Variant = variant;
        Plan = plan;
        _packages = Array.AsReadOnly(packages);
    }

    public CabinContentVariant Variant { get; }
    public ContentActivationPlan Plan { get; }
    public ReadOnlyCollection<CabinLocalContentPackage> Packages => _packages;
    public ContentScopeActivationKey ActivationKey =>
        new(new ContentScopeActivationId(ContentScopeKey.Local, Plan.Activation), Plan.PlanHash);
}

/// <summary>Frozen A0.1 oracle hashes. Changes require an explicit content-oracle update.</summary>
public static class CabinContentFingerprints
{
    public const string BaseCatalog = "573b0486d7914f7640b109fcae10e0712c033844ca2325db889e0443adcd7a31";
    public const string DefaultCatalog = "633eed18a9856474e43c4a6fb61db0bd80fbade8a40f6d884728b29024afb503";
    public const string ReplaceCatalog = "f1f223d3c1ccf94560b8fbbb4d50bf036a98b043cb117ced63257ac4ecdd7f44";
    public const string AddCatalog = "919d13d6572bf0df807891783d3ea4e25dea16010717df8a2223081ab1df8564";
}

/// <summary>
/// Builds the local, engine-neutral Default/Replace/Add content oracle for Cabin in the Woods.
/// It performs no filesystem access, cache installation, Network work, or engine realization.
/// </summary>
public sealed class CabinContentOracle
{
    private const string WallPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mOYmOnxHwAFEwJCUcD00AAAAABJRU5ErkJggg==";
    private const string WoodPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mOIsVL5DwADagG6BZniIwAAAABJRU5ErkJggg==";
    private const string TestWorldIconPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNwafL4DwAEKgIOr4uweAAAAABJRU5ErkJggg==";
    private const string ReplacementWallPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mMwjF3xHwAELwI2inq7YQAAAABJRU5ErkJggg==";
    private const string BlueGlowPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mPQXvH/PwAFpgLS3CcyDAAAAABJRU5ErkJggg==";
    private const string StreamedBadgePng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNwu/T/PwAGkAMXjXjDrwAAAABJRU5ErkJggg==";

    private CabinContentOracle(
        CabinLocalContentPackage basePackage,
        CabinLocalContentPackage replacePackage,
        CabinLocalContentPackage addPackage,
        ContentBaseCatalog baseCatalog,
        CabinResolvedContent defaultContent,
        CabinResolvedContent replaceContent,
        CabinResolvedContent addContent)
    {
        BasePackage = basePackage;
        ReplacePackage = replacePackage;
        AddPackage = addPackage;
        BaseCatalog = baseCatalog;
        Default = defaultContent;
        Replace = replaceContent;
        Add = addContent;
    }

    /// <summary>
    /// Stable authenticated realm used by historical remote-protocol oracle fixtures. Persistent
    /// Local acceptance creates a scope-owned activation directly and never impersonates it.
    /// </summary>
    public static ApplicationRealmId FixtureRealm { get; } =
        new(0x554C_5452_4153_494DUL, 0x4341_4249_4E00_0001UL);

    public CabinLocalContentPackage BasePackage { get; }
    public CabinLocalContentPackage ReplacePackage { get; }
    public CabinLocalContentPackage AddPackage { get; }
    public ContentBaseCatalog BaseCatalog { get; }
    public CabinResolvedContent Default { get; }
    public CabinResolvedContent Replace { get; }
    public CabinResolvedContent Add { get; }

    /// <summary>
    /// Resolves one verified local fixture payload by content identity. This is the narrow
    /// A0.1-to-F0.2 realization seam; later cache/source implementations can satisfy the same
    /// hash without exposing package-relative paths to presentation code.
    /// </summary>
    public bool TryGetPayload(ContentHash hash, out ReadOnlyMemory<byte> payload)
    {
        if (!hash.IsValid)
        {
            payload = default;
            return false;
        }
        return TryGetPayload(BasePackage, hash, out payload) ||
            TryGetPayload(ReplacePackage, hash, out payload) ||
            TryGetPayload(AddPackage, hash, out payload);
    }

    public static CabinContentOracle Create()
    {
        CabinLocalContentPackage basePackage = CreateBasePackage();
        CabinLocalContentPackage replacePackage = CreateReplacePackage();
        CabinLocalContentPackage addPackage = CreateAddPackage();

        if (!ContentBaseCatalog.TryCreate([basePackage.Verified], out ContentBaseCatalog? baseCatalog,
                out ContentCatalogResolutionError baseError))
            throw new InvalidOperationException($"The frozen Cabin base catalog is invalid: {baseError}.");

        CabinResolvedContent defaultContent = Resolve(
            CabinContentVariant.Default,
            new ContentActivationId(0x4341_4249_4E2D_4445UL, 0x4641_554C_5400_0001UL),
            baseCatalog!,
            [],
            []);
        CabinResolvedContent replaceContent = Resolve(
            CabinContentVariant.Replace,
            new ContentActivationId(0x4341_4249_4E2D_5245UL, 0x504C_4143_4500_0001UL),
            baseCatalog!,
            [replacePackage.Verified],
            [replacePackage.Manifest]);
        CabinResolvedContent addContent = Resolve(
            CabinContentVariant.Add,
            new ContentActivationId(0x4341_4249_4E2D_4144UL, 0x4400_0000_0000_0001UL),
            baseCatalog!,
            [addPackage.Verified],
            [addPackage.Manifest]);

        return new CabinContentOracle(
            basePackage, replacePackage, addPackage, baseCatalog!, defaultContent, replaceContent, addContent);
    }

    /// <summary>
    /// Builds the ordinary world.local package set for the current logical Cabin presentation.
    /// The historical built-in oracle remains the semantic comparison; these exact bytes enter
    /// the same CAS/catalog path used by Realm and User presentation layers.
    /// </summary>
    public CabinLocalPresentationContent CreateLocalPresentation(CabinContentVariant variant)
    {
        CabinLocalContentPackage localBase = ReauthorizeForLocal(BasePackage);
        CabinLocalContentPackage[] packages = variant switch
        {
            CabinContentVariant.Default => [localBase],
            CabinContentVariant.Replace => [localBase, ReauthorizeForLocal(ReplacePackage)],
            CabinContentVariant.Add => [localBase, ReauthorizeForLocal(AddPackage)],
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        ContentActivationId activation = variant switch
        {
            CabinContentVariant.Default => new(0x4631_362D_4341_4249UL, 0x4E2D_4445_4641_554CUL),
            CabinContentVariant.Replace => new(0x4631_362D_4341_4249UL, 0x4E2D_5245_504C_4143UL),
            CabinContentVariant.Add => new(0x4631_362D_4341_4249UL, 0x4E2D_4144_4400_0001UL),
            _ => default,
        };
        ContentActivationPackage[] selected = packages.Select(static package =>
            new ContentActivationPackage(
                package.Manifest.PackageId,
                package.Manifest.ManifestHash,
                ContentRequirementPolicy.Required)).ToArray();
        if (!ContentActivationPlan.TryCreate(
                ContentScopeKey.Local, activation, ContentBaseCatalog.Empty.Fingerprint,
                selected, out ContentActivationPlan? plan, out string? error))
            throw new InvalidOperationException(
                $"The Cabin Local presentation plan is invalid: {error}");
        return new CabinLocalPresentationContent(variant, plan!, packages);
    }

    /// <summary>Deterministic F1.6 user presentation leaf for the real Cabin wall consumer.</summary>
    public CabinLocalContentPackage CreateUserWallTexturePack() =>
        CreateUserTexturePack(
            "user.cabin.wall-blue", "Cabin Blue Wall", CabinContentIds.WallTexture,
            "textures/user-wall.png", BlueGlowPng);

    /// <summary>Valid selected pack that intentionally targets no active Cabin logical leaf.</summary>
    public CabinLocalContentPackage CreateIneffectiveUserTexturePack()
    {
        if (!LogicalContentId.TryCreate("other:texture/wall", out LogicalContentId other))
            throw new InvalidOperationException("The F1.6 no-op texture identity is invalid.");
        return CreateUserTexturePack(
            "user.other.wall-blue", "Other World Blue Wall", other,
            "textures/other-wall.png", BlueGlowPng);
    }

    private static CabinLocalContentPackage CreateUserTexturePack(
        string packageValue,
        string name,
        LogicalContentId target,
        string path,
        string base64)
    {
        ContentFilePayload[] payloads = [Payload(path, base64)];
        ContentDefinitionDescriptor[] definitions =
        [
            FileDefinition(target, ContentDefinitionKind.Texture,
                ContentDefinitionOperation.Replace, path),
        ];
        Array.Sort(payloads, static (left, right) => left.Path.CompareTo(right.Path));
        ContentFileDescriptor[] files = payloads.Select(static payload =>
            File(payload.Path, ContentFileKind.PngTexture, payload.Bytes.Span)).ToArray();
        if (!ContentPackageId.TryCreate(packageValue, out ContentPackageId packageId) ||
            !ContentNamespaceId.TryCreate("userpack", out ContentNamespaceId contentNamespace))
            throw new InvalidOperationException("The F1.6 Cabin user identity is invalid.");
        if (!ContentPackageManifest.TryCreateUserResourcePack(
                ContentLimits.CurrentPipelineFormat,
                ContentLimits.CurrentPipelineFormat,
                packageId,
                contentNamespace,
                name,
                "1.0.0",
                files,
                definitions,
                out ContentPackageManifest? manifest,
                out string? manifestError))
            throw new InvalidOperationException(
                $"The F1.6 Cabin user texture pack is invalid: {manifestError}");
        if (!ContentPackageValidator.TryValidatePayloads(
                manifest!, payloads, out VerifiedContentPackage? verified,
                out string? validationError))
            throw new InvalidOperationException(
                $"The F1.6 Cabin user texture payload is invalid: {validationError}");
        return new CabinLocalContentPackage(
            verified!, payloads,
            new ContentPackageAcquisition(
                packageId, manifest!.ManifestHash,
                [new ContentSourceDescriptor(
                    ContentSourceKind.Local, "fixtures/user/cabin-wall-blue.uspkg")]));
    }

    private static CabinResolvedContent Resolve(
        CabinContentVariant variant,
        ContentActivationId activation,
        ContentBaseCatalog baseCatalog,
        VerifiedContentPackage[] available,
        ContentPackageManifest[] required)
    {
        ContentActivationPackage[] packages = required
            .Select(static package => new ContentActivationPackage(
                package.PackageId, package.ManifestHash, ContentRequirementPolicy.Required))
            .ToArray();
        if (!ContentActivationPlan.TryCreate(
                FixtureRealm, activation, baseCatalog.Fingerprint, packages,
                out ContentActivationPlan? plan, out string? planError))
            throw new InvalidOperationException($"The frozen Cabin {variant} activation is invalid: {planError}");
        if (!ContentCatalogResolver.TryResolve(
                baseCatalog, plan!, available, out ResolvedContentCatalog? catalog,
                out ContentCatalogResolutionError resolutionError))
            throw new InvalidOperationException($"The frozen Cabin {variant} catalog is invalid: {resolutionError}.");
        return new CabinResolvedContent(variant, plan!, catalog!);
    }

    private static bool TryGetPayload(
        CabinLocalContentPackage package,
        ContentHash hash,
        out ReadOnlyMemory<byte> payload)
    {
        foreach (ContentFileDescriptor file in package.Manifest.Files)
        {
            if (file.Sha256 != hash) continue;
            if (!package.TryGetPayload(file.Path, out payload))
                throw new InvalidOperationException(
                    $"Verified Cabin file '{file.Path}' lost its local payload.");
            return true;
        }
        payload = default;
        return false;
    }

    private static CabinLocalContentPackage CreateBasePackage()
    {
        ContentFilePayload[] payloads =
        [
            Payload("icons/chest.png", WoodPng),
            Payload("icons/test-world.png", TestWorldIconPng),
            Payload("textures/wall.png", WallPng),
            Payload("textures/wood.png", WoodPng),
        ];
        ContentDefinitionDescriptor[] definitions =
        [
            FileDefinition(CabinContentIds.ChestIcon, ContentDefinitionKind.Icon,
                ContentDefinitionOperation.Add, "icons/chest.png"),
            FileDefinition(CabinContentIds.TestWorldIcon, ContentDefinitionKind.Icon,
                ContentDefinitionOperation.Add, "icons/test-world.png"),
            MaterialDefinition(CabinContentIds.WallMaterial, ContentDefinitionOperation.Add,
                CabinContentIds.WallTexture, roughness: 0.82f, metallic: 0f, emission: 0f),
            MaterialDefinition(CabinContentIds.WoodMaterial, ContentDefinitionOperation.Add,
                CabinContentIds.WoodTexture, roughness: 0.68f, metallic: 0f, emission: 0f),
            FileDefinition(CabinContentIds.WallTexture, ContentDefinitionKind.Texture,
                ContentDefinitionOperation.Add, "textures/wall.png"),
            FileDefinition(CabinContentIds.WoodTexture, ContentDefinitionKind.Texture,
                ContentDefinitionOperation.Add, "textures/wood.png"),
        ];
        return Package(
            "ultrasim.cabin.default", "cabin", "Cabin in the Woods Defaults",
            ContentTrustClass.BuiltIn, "builtin/cabin/default.uspkg", payloads, definitions);
    }

    private static CabinLocalContentPackage ReauthorizeForLocal(CabinLocalContentPackage source)
    {
        ContentPackageManifest existing = source.Manifest;
        if (!ContentPackageManifest.TryCreate(
                existing.MinimumPipelineFormat,
                existing.MaximumPipelineFormat,
                existing.PackageId,
                existing.Namespace,
                existing.Name,
                existing.Version,
                ContentTrustClass.ScopeAuthorizedPassive,
                existing.Files,
                existing.Definitions,
                out ContentPackageManifest? manifest,
                out string? manifestError))
            throw new InvalidOperationException(
                $"The Cabin world.local manifest is invalid: {manifestError}");
        if (!ContentPackageValidator.TryValidatePayloads(
                manifest!, source.Payloads,
                out VerifiedContentPackage? verified, out string? validationError))
            throw new InvalidOperationException(
                $"The Cabin world.local payloads are invalid: {validationError}");
        return new CabinLocalContentPackage(verified!, source.Payloads.ToArray(), source.Acquisition);
    }

    private static CabinLocalContentPackage CreateReplacePackage()
    {
        ContentFilePayload[] payloads = [Payload("textures/replacement-wall.png", ReplacementWallPng)];
        ContentDefinitionDescriptor[] definitions =
        [
            FileDefinition(CabinContentIds.WallTexture, ContentDefinitionKind.Texture,
                ContentDefinitionOperation.Replace, "textures/replacement-wall.png"),
        ];
        return Package(
            "test-cabin.replace-wall", "test-cabin", "Cabin Wall Replacement",
            ContentTrustClass.ServerAuthorizedPassive, "fixtures/cabin/replace.uspkg", payloads, definitions);
    }

    private static CabinLocalContentPackage CreateAddPackage()
    {
        ContentFilePayload[] payloads =
        [
            Payload("icons/streamed-badge.png", StreamedBadgePng),
            Payload("textures/blue-glow.png", BlueGlowPng),
        ];
        ContentDefinitionDescriptor[] definitions =
        [
            MaterialDefinition(CabinContentIds.BlueGlowMaterial, ContentDefinitionOperation.Add,
                CabinContentIds.BlueGlowTexture, roughness: 0.25f, metallic: 0.1f, emission: 3f),
            FileDefinition(CabinContentIds.BlueGlowTexture, ContentDefinitionKind.Texture,
                ContentDefinitionOperation.Add, "textures/blue-glow.png"),
            FileDefinition(CabinContentIds.StreamedBadgeIcon, ContentDefinitionKind.Icon,
                ContentDefinitionOperation.Add, "icons/streamed-badge.png"),
        ];
        return Package(
            "test-cabin.add", "test-cabin", "Cabin Added Presentation",
            ContentTrustClass.ServerAuthorizedPassive, "fixtures/cabin/add.uspkg", payloads, definitions);
    }

    private static CabinLocalContentPackage Package(
        string packageValue,
        string namespaceValue,
        string name,
        ContentTrustClass trust,
        string localSource,
        ContentFilePayload[] payloads,
        ContentDefinitionDescriptor[] definitions)
    {
        Array.Sort(payloads, static (left, right) => left.Path.CompareTo(right.Path));
        ContentFileDescriptor[] files = payloads
            .Select(static payload => File(payload.Path, ContentFileKind.PngTexture, payload.Bytes.Span))
            .ToArray();
        if (!ContentPackageId.TryCreate(packageValue, out ContentPackageId packageId) ||
            !ContentNamespaceId.TryCreate(namespaceValue, out ContentNamespaceId contentNamespace))
            throw new InvalidOperationException($"The frozen Cabin package identity '{packageValue}' is invalid.");
        if (!ContentPackageManifest.TryCreate(
                ContentLimits.CurrentPipelineFormat,
                ContentLimits.CurrentPipelineFormat,
                packageId,
                contentNamespace,
                name,
                "1.0.0",
                trust,
                files,
                definitions,
                out ContentPackageManifest? manifest,
                out string? manifestError))
            throw new InvalidOperationException($"The frozen Cabin package is invalid: {manifestError}");
        if (!ContentPackageValidator.TryValidatePayloads(
                manifest!, payloads, out VerifiedContentPackage? verified, out string? validationError))
            throw new InvalidOperationException($"The frozen Cabin payloads are invalid: {validationError}");
        var acquisition = new ContentPackageAcquisition(
            packageId,
            manifest!.ManifestHash,
            [new ContentSourceDescriptor(ContentSourceKind.Local, localSource)]);
        return new CabinLocalContentPackage(verified!, payloads, acquisition);
    }

    private static ContentFilePayload Payload(string pathValue, string base64)
    {
        if (!ContentFilePath.TryCreate(pathValue, out ContentFilePath path))
            throw new InvalidOperationException($"The frozen Cabin file path '{pathValue}' is invalid.");
        return new ContentFilePayload(path, Convert.FromBase64String(base64));
    }

    private static ContentFileDescriptor File(
        ContentFilePath path,
        ContentFileKind kind,
        ReadOnlySpan<byte> bytes)
    {
        if (!ContentFileDescriptor.TryCreate(
                path, kind, bytes.Length, ContentHash.Compute(bytes), default, out ContentFileDescriptor descriptor))
            throw new InvalidOperationException($"The frozen Cabin file '{path}' is invalid.");
        return descriptor;
    }

    private static ContentDefinitionDescriptor FileDefinition(
        LogicalContentId id,
        ContentDefinitionKind kind,
        ContentDefinitionOperation operation,
        string pathValue)
    {
        if (!ContentFilePath.TryCreate(pathValue, out ContentFilePath path) ||
            !ContentDefinitionDescriptor.TryCreateFileDefinition(id, kind, operation, path, out var definition))
            throw new InvalidOperationException($"The frozen Cabin definition '{id}' is invalid.");
        return definition!;
    }

    private static ContentDefinitionDescriptor MaterialDefinition(
        LogicalContentId id,
        ContentDefinitionOperation operation,
        LogicalContentId albedo,
        float roughness,
        float metallic,
        float emission)
    {
        if (!ContentMaterialDefinition.TryCreate(
                ContentMaterialFamily.StandardLit, albedo, roughness, metallic, emission, out var material) ||
            !ContentDefinitionDescriptor.TryCreateMaterial(id, operation, material, out var definition))
            throw new InvalidOperationException($"The frozen Cabin material '{id}' is invalid.");
        return definition!;
    }
}
