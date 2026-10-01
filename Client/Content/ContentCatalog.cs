using System.Collections.ObjectModel;

namespace UltraSim.Client.Content;

public enum ContentCatalogResolutionError : byte
{
    None = 0,
    InvalidBaseCatalog,
    BaseCatalogMismatch,
    InvalidAvailablePackage,
    DuplicateAvailablePackage,
    MissingRequiredPackage,
    UnauthorizedPackageTrust,
    AddConflictsWithExistingDefinition,
    ReplaceTargetsMissingDefinition,
    InvalidDefinitionReference,
    CatalogCapacityExceeded,
}

/// <summary>One final logical definition and the exact package/files that currently own it.</summary>
public sealed class ResolvedContentEntry
{
    private readonly ReadOnlyCollection<ContentHash> _referencedFiles;

    internal ResolvedContentEntry(
        LogicalContentId id,
        ContentPackageId ownerPackageId,
        ContentHash ownerManifestHash,
        ContentDefinitionDescriptor definition,
        ContentHash[] referencedFiles)
    {
        Id = id;
        OwnerPackageId = ownerPackageId;
        OwnerManifestHash = ownerManifestHash;
        Definition = definition;
        _referencedFiles = Array.AsReadOnly(referencedFiles);
    }

    public LogicalContentId Id { get; }
    public ContentDefinitionKind Kind => Definition.Kind;
    public ContentPackageId OwnerPackageId { get; }
    public ContentHash OwnerManifestHash { get; }
    public ContentDefinitionDescriptor Definition { get; }
    public ReadOnlyCollection<ContentHash> ReferencedFiles => _referencedFiles;
}

/// <summary>Deterministic verified local/default catalog required by an activation plan.</summary>
public sealed class ContentBaseCatalog
{
    private readonly ReadOnlyCollection<ResolvedContentEntry> _entries;

    private ContentBaseCatalog(ResolvedContentEntry[] entries)
    {
        _entries = Array.AsReadOnly(entries);
        Fingerprint = ContentCanonicalHash.ComputeBaseCatalog(entries);
    }

    public ReadOnlyCollection<ResolvedContentEntry> Entries => _entries;
    public ContentHash Fingerprint { get; }

    /// <summary>
    /// Canonical absence of an optional local/default content pack. Primitive emergency rendering
    /// lives below Content, so a contentless client still has a valid empty base-catalog identity.
    /// </summary>
    public static ContentBaseCatalog Empty { get; } = new([]);

    public static bool TryCreate(
        IEnumerable<VerifiedContentPackage>? builtInPackages,
        out ContentBaseCatalog? catalog,
        out ContentCatalogResolutionError error,
        ContentLimits limits = default)
    {
        catalog = null;
        error = ContentCatalogResolutionError.None;
        limits = ContentLimits.Resolve(limits);
        if (!ContentBoundedCopy.TryCopy(builtInPackages, limits.MaximumPackages,
                out VerifiedContentPackage[] packages) || packages.Length == 0 ||
            packages.Any(static value => value is null))
            return Fail(ContentCatalogResolutionError.InvalidBaseCatalog, out error);
        Array.Sort(packages, static (left, right) =>
        {
            int id = left.PackageId.CompareTo(right.PackageId);
            return id != 0 ? id : left.ManifestHash.CompareTo(right.ManifestHash);
        });

        var packageIds = new HashSet<ContentPackageId>();
        var pending = new Dictionary<LogicalContentId, PendingContentEntry>();
        foreach (VerifiedContentPackage verified in packages)
        {
            ContentPackageManifest package = verified.Manifest;
            if (package.TrustClass != ContentTrustClass.BuiltIn ||
                !packageIds.Add(package.PackageId) || !ManifestWithinLimits(package, limits))
                return Fail(ContentCatalogResolutionError.InvalidBaseCatalog, out error);
            foreach (ContentDefinitionDescriptor definition in package.Definitions)
            {
                if (definition.Operation != ContentDefinitionOperation.Add ||
                    !pending.TryAdd(definition.Id, new PendingContentEntry(package, definition)))
                    return Fail(ContentCatalogResolutionError.InvalidBaseCatalog, out error);
            }
        }

        if (!WithinCatalogCapacity(pending.Count, limits))
            return Fail(ContentCatalogResolutionError.CatalogCapacityExceeded, out error);
        if (!TryMaterialize(pending, out ResolvedContentEntry[] entries))
            return Fail(ContentCatalogResolutionError.InvalidDefinitionReference, out error);
        catalog = new ContentBaseCatalog(entries);
        return true;
    }

    public bool TryGet(LogicalContentId id, out ResolvedContentEntry? entry) =>
        ContentCatalogLookup.TryGet(_entries, id, out entry);

    internal Dictionary<LogicalContentId, PendingContentEntry> CopyPending()
    {
        var result = new Dictionary<LogicalContentId, PendingContentEntry>(_entries.Count);
        foreach (ResolvedContentEntry entry in _entries)
            result.Add(entry.Id, new PendingContentEntry(entry.OwnerPackageId, entry.OwnerManifestHash,
                entry.Definition, entry.ReferencedFiles.ToArray()));
        return result;
    }

    internal static bool TryMaterialize(
        Dictionary<LogicalContentId, PendingContentEntry> pending,
        out ResolvedContentEntry[] entries)
    {
        PendingContentEntry[] ordered = pending.Values.OrderBy(value => value.Definition.Id).ToArray();
        entries = new ResolvedContentEntry[ordered.Length];
        for (int index = 0; index < ordered.Length; index++)
        {
            PendingContentEntry source = ordered[index];
            ContentHash[] references;
            if (source.Definition.Kind is ContentDefinitionKind.Texture or ContentDefinitionKind.Icon or
                ContentDefinitionKind.WorldPack or ContentDefinitionKind.ActorPresentation or
                ContentDefinitionKind.WorldStructuralManifest or ContentDefinitionKind.AudioClip or
                ContentDefinitionKind.WorldPresentation)
            {
                if (source.ResolvedReferences is not null)
                {
                    references = source.ResolvedReferences.ToArray();
                }
                else
                {
                    if (source.OwningManifest is null || !source.OwningManifest.TryGetFile(
                            source.Definition.PrimaryFile, out ContentFileDescriptor file))
                        return Invalid(out entries);
                    references = [file.Sha256];
                }
            }
            else if (source.Definition.Kind == ContentDefinitionKind.Material)
            {
                LogicalContentId albedo = source.Definition.Material.AlbedoTexture;
                if (!albedo.IsValid)
                {
                    references = [];
                }
                else
                {
                    if (!pending.TryGetValue(albedo, out PendingContentEntry texture) ||
                        texture.Definition.Kind != ContentDefinitionKind.Texture)
                        return Invalid(out entries);
                    if (texture.ResolvedReferences is not null)
                    {
                        references = texture.ResolvedReferences.ToArray();
                    }
                    else if (texture.OwningManifest is not null && texture.OwningManifest.TryGetFile(
                                 texture.Definition.PrimaryFile, out ContentFileDescriptor textureFile))
                    {
                        references = [textureFile.Sha256];
                    }
                    else
                    {
                        return Invalid(out entries);
                    }
                }
            }
            else
            {
                return Invalid(out entries);
            }

            Array.Sort(references);
            entries[index] = new ResolvedContentEntry(source.Definition.Id, source.OwnerPackageId,
                source.OwnerManifestHash, source.Definition, references);
        }
        return true;
    }

    internal static bool WithinCatalogCapacity(int count, ContentLimits limits)
    {
        long maximum = (long)limits.MaximumPackages * limits.MaximumDefinitionsPerPackage;
        return count >= 0 && count <= maximum;
    }

    internal static bool ManifestWithinLimits(ContentPackageManifest package, ContentLimits limits)
    {
        if (package.Files.Count > limits.MaximumFilesPerPackage ||
            package.Definitions.Count > limits.MaximumDefinitionsPerPackage) return false;
        long total = 0;
        foreach (ContentFileDescriptor file in package.Files)
        {
            if (file.SizeBytes > limits.MaximumFileBytes) return false;
            try { total = checked(total + file.SizeBytes); }
            catch (OverflowException) { return false; }
            if (total > limits.MaximumPackageBytes) return false;
        }
        return true;
    }

    private static bool Invalid(out ResolvedContentEntry[] entries)
    {
        entries = [];
        return false;
    }

    private static bool Fail(ContentCatalogResolutionError value, out ContentCatalogResolutionError error)
    {
        error = value;
        return false;
    }
}

/// <summary>Immutable, engine-neutral result of resolving one authorized activation.</summary>
public sealed class ResolvedContentCatalog
{
    private readonly ReadOnlyCollection<ResolvedContentEntry> _entries;

    internal ResolvedContentCatalog(ContentActivationPlan plan, ResolvedContentEntry[] entries)
    {
        Scope = plan.Scope;
        Activation = plan.Activation;
        PlanHash = plan.PlanHash;
        RequiredBaseCatalogFingerprint = plan.RequiredBaseCatalogFingerprint;
        _entries = Array.AsReadOnly(entries);
        Fingerprint = ContentCanonicalHash.ComputeResolvedCatalog(plan, entries);
        Generation = ContentCatalogGenerationId.Next();
    }

    public ContentScopeKey Scope { get; }
    /// <summary>Compatibility projection for remote protocol adapters. Invalid for Local.</summary>
    public UltraSim.Game.Application.ApplicationRealmId Realm => Scope.Realm;
    public ContentActivationId Activation { get; }
    public ContentHash PlanHash { get; }
    public ContentHash RequiredBaseCatalogFingerprint { get; }
    public ContentHash Fingerprint { get; }
    public ContentCatalogGenerationId Generation { get; }
    public ReadOnlyCollection<ResolvedContentEntry> Entries => _entries;

    public bool TryGet(LogicalContentId id, out ResolvedContentEntry? entry) =>
        ContentCatalogLookup.TryGet(_entries, id, out entry);
}

public static class ContentCatalogResolver
{
    public static bool TryResolve(
        ContentBaseCatalog baseCatalog,
        ContentActivationPlan plan,
        IEnumerable<VerifiedContentPackage>? availablePackages,
        out ResolvedContentCatalog? catalog,
        out ContentCatalogResolutionError error,
        ContentLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(baseCatalog);
        ArgumentNullException.ThrowIfNull(plan);
        catalog = null;
        error = ContentCatalogResolutionError.None;
        limits = ContentLimits.Resolve(limits);
        if (plan.Packages.Count > limits.MaximumPackages)
            return Fail(ContentCatalogResolutionError.CatalogCapacityExceeded, out error);
        if (baseCatalog.Fingerprint != plan.RequiredBaseCatalogFingerprint)
            return Fail(ContentCatalogResolutionError.BaseCatalogMismatch, out error);

        var available = new Dictionary<PackageManifestIdentity, ContentPackageManifest>();
        foreach (VerifiedContentPackage? verified in availablePackages ?? [])
        {
            ContentPackageManifest? package = verified?.Manifest;
            if (package is null)
                return Fail(ContentCatalogResolutionError.InvalidAvailablePackage, out error);
            var identity = new PackageManifestIdentity(package.PackageId, package.ManifestHash);
            if (!available.TryAdd(identity, package))
                return Fail(ContentCatalogResolutionError.DuplicateAvailablePackage, out error);
            if (available.Count > limits.MaximumPackages)
                return Fail(ContentCatalogResolutionError.CatalogCapacityExceeded, out error);
        }

        Dictionary<LogicalContentId, PendingContentEntry> pending = baseCatalog.CopyPending();
        foreach (ContentActivationPackage requirement in plan.Packages)
        {
            var identity = new PackageManifestIdentity(requirement.PackageId, requirement.ManifestHash);
            if (!available.TryGetValue(identity, out ContentPackageManifest? package))
            {
                if (requirement.Requirement == ContentRequirementPolicy.Optional) continue;
                return Fail(ContentCatalogResolutionError.MissingRequiredPackage, out error);
            }
            if (package.TrustClass != ContentTrustClass.ScopeAuthorizedPassive)
                return Fail(ContentCatalogResolutionError.UnauthorizedPackageTrust, out error);
            if (!ContentBaseCatalog.ManifestWithinLimits(package, limits))
                return Fail(ContentCatalogResolutionError.CatalogCapacityExceeded, out error);

            foreach (ContentDefinitionDescriptor definition in package.Definitions)
            {
                bool exists = pending.ContainsKey(definition.Id);
                if (definition.Operation == ContentDefinitionOperation.Add)
                {
                    if (exists)
                        return Fail(ContentCatalogResolutionError.AddConflictsWithExistingDefinition, out error);
                    pending.Add(definition.Id, new PendingContentEntry(package, definition));
                }
                else if (definition.Operation == ContentDefinitionOperation.Replace)
                {
                    if (!exists)
                        return Fail(ContentCatalogResolutionError.ReplaceTargetsMissingDefinition, out error);
                    pending[definition.Id] = new PendingContentEntry(package, definition);
                }
                else
                {
                    return Fail(ContentCatalogResolutionError.InvalidAvailablePackage, out error);
                }
            }
            if (!ContentBaseCatalog.WithinCatalogCapacity(pending.Count, limits))
                return Fail(ContentCatalogResolutionError.CatalogCapacityExceeded, out error);
        }

        if (!ContentBaseCatalog.TryMaterialize(pending, out ResolvedContentEntry[] entries))
            return Fail(ContentCatalogResolutionError.InvalidDefinitionReference, out error);
        catalog = new ResolvedContentCatalog(plan, entries);
        return true;
    }

    private static bool Fail(ContentCatalogResolutionError value, out ContentCatalogResolutionError error)
    {
        error = value;
        return false;
    }
}

internal readonly record struct PackageManifestIdentity(ContentPackageId PackageId, ContentHash ManifestHash);

internal readonly struct PendingContentEntry
{
    internal PendingContentEntry(ContentPackageManifest manifest, ContentDefinitionDescriptor definition)
    {
        OwnerPackageId = manifest.PackageId;
        OwnerManifestHash = manifest.ManifestHash;
        OwningManifest = manifest;
        Definition = definition;
        ResolvedReferences = null;
    }

    internal PendingContentEntry(
        ContentPackageId ownerPackageId,
        ContentHash ownerManifestHash,
        ContentDefinitionDescriptor definition,
        ContentHash[] resolvedReferences)
    {
        OwnerPackageId = ownerPackageId;
        OwnerManifestHash = ownerManifestHash;
        OwningManifest = null;
        Definition = definition;
        ResolvedReferences = resolvedReferences;
    }

    internal ContentPackageId OwnerPackageId { get; }
    internal ContentHash OwnerManifestHash { get; }
    internal ContentPackageManifest? OwningManifest { get; }
    internal ContentDefinitionDescriptor Definition { get; }
    internal ContentHash[]? ResolvedReferences { get; }
}

internal static class ContentCatalogLookup
{
    internal static bool TryGet(
        ReadOnlyCollection<ResolvedContentEntry> entries,
        LogicalContentId id,
        out ResolvedContentEntry? entry)
    {
        int low = 0, high = entries.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int comparison = entries[middle].Id.CompareTo(id);
            if (comparison == 0)
            {
                entry = entries[middle];
                return true;
            }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        entry = null;
        return false;
    }
}
