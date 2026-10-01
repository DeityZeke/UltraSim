using System.Security.Cryptography;

namespace UltraSim.Client.Content;

public enum PresentationSourceKind : byte
{
    Primitive = 0,
    Local = 1,
    Realm = 2,
    User = 3,
}

/// <summary>Process-local fence for one published effective presentation composition.</summary>
public readonly record struct PresentationGenerationId(ulong Value)
{
    public bool IsValid => Value != 0;

    internal static PresentationGenerationId Next()
    {
        long next = Interlocked.Increment(ref Sequence.Value);
        if (next <= 0) throw new InvalidOperationException("Presentation generation exhausted.");
        return new PresentationGenerationId((ulong)next);
    }

    private static class Sequence { internal static long Value; }
}

public readonly record struct PresentationGenerationSnapshot(
    PresentationGenerationId Generation,
    ContentHash StableFingerprint,
    bool UserLayerPresent,
    bool RealmLayerPresent,
    bool LocalLayerPresent,
    int EnabledUserPacks,
    int UserSelections,
    int RealmSelections,
    int LocalSelections,
    int PrimitiveSelections,
    int ActorSelections = 0,
    int TextureSelections = 0,
    int MaterialSelections = 0,
    int UserResourceSelections = 0,
    int RealmResourceSelections = 0,
    int LocalResourceSelections = 0,
    bool RealmResourceLayerPresent = false,
    bool LocalResourceLayerPresent = false,
    int WorldMeshSelections = 0)
{
    public bool EffectiveUserLayerPresent => UserSelections != 0 || UserResourceSelections != 0;
}

/// <summary>
/// Cold/event-driven, bounded actor/texture/material presentation composition. Content truth is
/// immutable and engine-neutral; renderer-native derived objects remain outside this type.
/// </summary>
public sealed class PresentationGeneration : IActorPresentationGenerationSource, IWorldPresentationGenerationSource, IDisposable
{
    private static readonly string[] s_actorIds =
    [
        ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        ConceptDemoActorPresentationProfile.HumanYDefinitionId,
    ];

    private readonly ContentCatalogLease? _realm;
    private readonly ContentCatalogLease? _local;
    private readonly ContentCatalogLease? _realmResources;
    private readonly ContentCatalogLease? _localResources;
    private readonly UserResourcePackStackLease? _user;
    private readonly SelectedActor[] _actors = new SelectedActor[2];
    private readonly SelectedTexture[] _textures = [];
    private readonly SelectedMaterial[] _materials = [];
    private readonly CatalogSelection[] _worldMeshes = [];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _references = 1;
    private int _ownerRetired;

    private PresentationGeneration(
        UserResourcePackStackLease? user,
        ContentCatalogLease? realm,
        ContentCatalogLease? local,
        ContentCatalogLease? realmResources,
        ContentCatalogLease? localResources)
    {
        ValidateLayer(realm, local: false, nameof(realm));
        ValidateLayer(local, local: true, nameof(local));
        ValidateLayer(realmResources, local: false, nameof(realmResources));
        ValidateLayer(localResources, local: true, nameof(localResources));
        _realm = realm;
        _local = local;
        _realmResources = realmResources;
        _localResources = localResources;
        _user = user;
        Generation = PresentationGenerationId.Next();
        List<SelectedTexture>? pendingTextures = null;

        try
        {
            int userSelections = 0;
            int realmSelections = 0;
            int localSelections = 0;
            for (int index = 0; index < s_actorIds.Length; index++)
            {
                if (!LogicalContentId.TryCreate(s_actorIds[index], out LogicalContentId id))
                    throw new InvalidOperationException("The stock actor logical identity is invalid.");
                ActorPresentationContentLease? selected = null;
                PresentationSourceKind source = PresentationSourceKind.Primitive;
                UserResourcePackIdentity userIdentity = default;
                if (user is not null && user.TryFindHighest(
                        id, out userIdentity, out _, out ContentDefinitionDescriptor? userDefinition) &&
                    userDefinition?.Kind == ContentDefinitionKind.ActorPresentation)
                {
                    if (!ContentActorPresentationResolver.TryResolveUserResourcePack(
                            user, id, ConceptDemoActorPresentationProfile.Requirements,
                            out selected, out ContentActorPresentationResolutionError userError))
                        throw new InvalidDataException(
                            $"The selected user presentation for {id} is incompatible: {userError}.");
                    source = PresentationSourceKind.User;
                    userSelections++;
                }
                else if (realm is not null && ContentActorPresentationResolver.TryResolve(
                        realm, id, ConceptDemoActorPresentationProfile.Requirements,
                        out selected, out _))
                {
                    source = PresentationSourceKind.Realm;
                    realmSelections++;
                }
                else if (local is not null && ContentActorPresentationResolver.TryResolve(
                             local, id, ConceptDemoActorPresentationProfile.Requirements,
                             out selected, out _))
                {
                    source = PresentationSourceKind.Local;
                    localSelections++;
                }
                _actors[index] = new SelectedActor(id, source, selected, userIdentity);
            }

            var lowerTextures = new SortedDictionary<LogicalContentId, CatalogSelection>();
            var lowerMaterials = new SortedDictionary<LogicalContentId, CatalogSelection>();
            var lowerWorldMeshes = new SortedDictionary<LogicalContentId, CatalogSelection>();
            int userResourceSelections = 0;
            int realmResourceSelections = 0;
            int localResourceSelections = 0;
            AddCatalogResources(localResources, PresentationSourceKind.Local,
                lowerTextures, lowerMaterials, lowerWorldMeshes, replace: false);
            AddCatalogResources(realmResources, PresentationSourceKind.Realm,
                lowerTextures, lowerMaterials, lowerWorldMeshes, replace: true);
            _worldMeshes = lowerWorldMeshes.Values.ToArray();

            var textures = new List<SelectedTexture>(lowerTextures.Count);
            pendingTextures = textures;
            foreach ((LogicalContentId id, CatalogSelection lower) in lowerTextures)
            {
                ResolvedTexturePresentation texture;
                if (user is not null && user.TryFindHighest(
                        id, out UserResourcePackIdentity identity,
                        out ContentPackageManifest? manifest,
                        out ContentDefinitionDescriptor? definition) &&
                    definition?.Kind == ContentDefinitionKind.Texture)
                {
                    if (!TryOpenUserTexture(user, identity, manifest!, definition,
                            out texture))
                        throw new InvalidDataException(
                            $"The selected user texture for {id} is invalid or unavailable.");
                    userResourceSelections++;
                }
                else
                {
                    if (!TryOpenCatalogTexture(lower.Catalog, lower.Entry, lower.Source,
                            out texture))
                        throw new InvalidDataException(
                            $"The selected {lower.Source} texture for {id} is invalid or unavailable.");
                    if (lower.Source == PresentationSourceKind.Realm) realmResourceSelections++;
                    else localResourceSelections++;
                }
                textures.Add(new SelectedTexture(texture));
            }
            _textures = textures.ToArray();
            pendingTextures = null;

            var materials = new List<SelectedMaterial>(lowerMaterials.Count);
            foreach ((LogicalContentId id, CatalogSelection selected) in lowerMaterials)
            {
                ContentDefinitionDescriptor definition = selected.Entry.Definition;
                ResolvedTexturePresentation? texture = FindTexture(definition.Material.AlbedoTexture);
                if (definition.Material.AlbedoTexture.IsValid && texture is null)
                    throw new InvalidDataException(
                        $"Material {id} has no effective texture {definition.Material.AlbedoTexture}.");
                ContentHash resourceFingerprint = PresentationResourceFingerprint.Material(
                    id, definition.DefinitionHash,
                    texture?.StableResourceFingerprint ?? default);
                materials.Add(new SelectedMaterial(
                    id, selected.Source, definition.Material,
                    definition.DefinitionHash, resourceFingerprint,
                    definition.Material.AlbedoTexture));
                if (selected.Source == PresentationSourceKind.Realm) realmResourceSelections++;
                else localResourceSelections++;
            }
            _materials = materials.ToArray();

            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData("UltraSim.Presentation.F1.6.v1"u8);
            Span<byte> digest = stackalloc byte[ContentHash.ByteLength];
            foreach (SelectedActor actor in _actors)
            {
                hash.AppendData([1]);
                PresentationResourceFingerprint.AppendText(hash, actor.Id.Value!);
                if (actor.Lease is not null)
                {
                    actor.Lease.FileHash.WriteDigest(digest);
                    hash.AppendData(digest);
                }
                else hash.AppendData([0]);
            }
            foreach (SelectedTexture texture in _textures)
            {
                hash.AppendData([2]);
                texture.Lease.StableResourceFingerprint.WriteDigest(digest);
                hash.AppendData(digest);
            }
            foreach (SelectedMaterial material in _materials)
            {
                hash.AppendData([3]);
                material.StableResourceFingerprint.WriteDigest(digest);
                hash.AppendData(digest);
            }
            foreach (CatalogSelection worldMesh in _worldMeshes)
            {
                hash.AppendData([4]);
                PresentationResourceFingerprint.AppendText(hash, worldMesh.Entry.Id.Value!);
                worldMesh.Entry.Definition.DefinitionHash.WriteDigest(digest);
                hash.AppendData(digest);
                worldMesh.Entry.ReferencedFiles[0].WriteDigest(digest);
                hash.AppendData(digest);
            }
            StableFingerprint = ContentHash.FromDigest(hash.GetHashAndReset());
            Snapshot = new PresentationGenerationSnapshot(
                Generation, StableFingerprint, user is { Identities.Count: > 0 },
                realm is not null,
                local is not null,
                user?.Identities.Count ?? 0, userSelections,
                realmSelections, localSelections,
                s_actorIds.Length - _actors.Count(static value => value.Lease is not null),
                _actors.Length, _textures.Length, _materials.Length,
                userResourceSelections, realmResourceSelections, localResourceSelections,
                realmResources is not null, localResources is not null,
                _worldMeshes.Length);
        }
        catch
        {
            if (pendingTextures is not null)
                foreach (SelectedTexture texture in pendingTextures) texture.Lease.Dispose();
            foreach (SelectedActor actor in _actors) actor.Lease?.Dispose();
            foreach (SelectedTexture texture in _textures) texture.Lease.Dispose();
            user?.Dispose();
            realm?.Dispose();
            local?.Dispose();
            realmResources?.Dispose();
            localResources?.Dispose();
            throw;
        }
    }

    public PresentationGenerationId Generation { get; }
    public PresentationGenerationId CurrentPresentationGeneration => Generation;
    public ContentHash StableFingerprint { get; }
    public PresentationGenerationSnapshot Snapshot { get; }

    public static PresentationGeneration Create(
        ContentCatalogLease? realm, ContentCatalogLease? local) =>
        new(null, realm, local, null, null);

    public static PresentationGeneration Create(
        UserResourcePackStackLease? user,
        ContentCatalogLease? realm,
        ContentCatalogLease? local) =>
        new(user, realm, local, null, null);

    public static PresentationGeneration CreateWithResources(
        UserResourcePackStackLease? user,
        ContentCatalogLease? realmActors,
        ContentCatalogLease? localActors,
        ContentCatalogLease? realmResources,
        ContentCatalogLease? localResources) =>
        new(user, realmActors, localActors, realmResources, localResources);

    public PresentationSourceKind GetSource(LogicalContentId id)
    {
        foreach (SelectedActor actor in _actors)
            if (actor.Id == id) return actor.Source;
        foreach (SelectedTexture texture in _textures)
            if (texture.Lease.LogicalId == id) return texture.Lease.Source;
        foreach (SelectedMaterial material in _materials)
            if (material.Id == id) return material.Source;
        foreach (CatalogSelection worldMesh in _worldMeshes)
            if (worldMesh.Entry.Id == id) return worldMesh.Source;
        return PresentationSourceKind.Primitive;
    }

    public bool TryGetUserResourcePack(
        LogicalContentId id,
        out UserResourcePackIdentity identity)
    {
        foreach (SelectedActor actor in _actors)
        {
            if (actor.Id != id || actor.Source != PresentationSourceKind.User) continue;
            identity = actor.UserResourcePack;
            return identity.IsValid;
        }
        foreach (SelectedTexture texture in _textures)
        {
            if (texture.Lease.LogicalId != id ||
                texture.Lease.Source != PresentationSourceKind.User) continue;
            identity = texture.Lease.UserResourcePack;
            return identity.IsValid;
        }
        identity = default;
        return false;
    }

    public bool TryResolveTexturePresentation(
        LogicalContentId definitionId,
        out ResolvedTexturePresentation? presentation,
        out PresentationResourceResolutionError error)
    {
        EnsureOwner();
        presentation = null;
        if (!definitionId.IsValid)
            return Fail(PresentationResourceResolutionError.InvalidRequest, out error);
        if (Volatile.Read(ref _ownerRetired) != 0)
            return Fail(PresentationResourceResolutionError.GenerationRetired, out error);
        ResolvedTexturePresentation? selected = FindTexture(definitionId);
        if (selected is null)
            return Fail(PresentationResourceResolutionError.DefinitionNotFound, out error);
        Interlocked.Increment(ref _references);
        presentation = selected.WithLifetime(new GenerationReference(this), Generation);
        error = PresentationResourceResolutionError.None;
        return true;
    }

    public bool TryResolveMaterialPresentation(
        LogicalContentId definitionId,
        out ResolvedMaterialPresentation? presentation,
        out PresentationResourceResolutionError error)
    {
        EnsureOwner();
        presentation = null;
        if (!definitionId.IsValid)
            return Fail(PresentationResourceResolutionError.InvalidRequest, out error);
        if (Volatile.Read(ref _ownerRetired) != 0)
            return Fail(PresentationResourceResolutionError.GenerationRetired, out error);
        foreach (SelectedMaterial material in _materials)
        {
            if (material.Id != definitionId) continue;
            ResolvedTexturePresentation? texture = FindTexture(material.AlbedoTexture);
            Interlocked.Increment(ref _references);
            presentation = new ResolvedMaterialPresentation(
                new GenerationReference(this), material.Id, material.Source,
                material.Definition, material.DefinitionHash,
                material.StableResourceFingerprint, Generation,
                texture?.WithLifetime(NoopLifetime.Instance, Generation));
            error = PresentationResourceResolutionError.None;
            return true;
        }
        return Fail(PresentationResourceResolutionError.DefinitionNotFound, out error);
    }

    public bool TryReadWorldMeshPresentation(LogicalContentId id, out byte[]? bytes,
        out ContentHash hash, out PresentationSourceKind source)
    {
        EnsureOwner();
        bytes = null;
        hash = default;
        source = PresentationSourceKind.Primitive;
        if (!id.IsValid || Volatile.Read(ref _ownerRetired) != 0) return false;
        foreach (CatalogSelection selection in _worldMeshes)
        {
            if (selection.Entry.Id != id) continue;
            ContentCache? cache = selection.Catalog.Owner;
            if (cache is null || selection.Entry.ReferencedFiles.Count != 1 ||
                !cache.TryOpenPackage(selection.Catalog.Catalog.Scope,
                    selection.Entry.OwnerPackageId, selection.Entry.OwnerManifestHash,
                    out VerifiedContentPackage? package, out _) ||
                !package!.Manifest.TryGetFile(selection.Entry.Definition.PrimaryFile,
                    out ContentFileDescriptor file) ||
                file.Kind != ContentFileKind.WorldMeshBinary ||
                file.Sha256 != selection.Entry.ReferencedFiles[0] ||
                file.SizeBytes > WorldMeshArtifact.MaximumBytes ||
                !cache.TryOpenBlob(file, out ContentBlobLease? blob, out _))
                return false;
            using (blob)
            {
                byte[] candidate = new byte[checked((int)file.SizeBytes)];
                if (!TryReadExact(blob!.Stream, candidate) || blob.Stream.ReadByte() != -1 ||
                    !WorldMeshArtifact.TryInspect(candidate, out _)) return false;
                bytes = candidate;
                hash = file.Sha256;
                source = selection.Source;
                return true;
            }
        }
        return false;
    }

    public bool TryResolveActorPresentation(
        LogicalContentId definitionId,
        ActorPresentationResolutionRequirements requirements,
        out ActorPresentationContentLease? lease,
        out ContentActorPresentationResolutionError error,
        ActorPresentationArtifactLimits limits = default)
    {
        EnsureOwner();
        lease = null;
        error = ContentActorPresentationResolutionError.DefinitionNotFound;
        if (Volatile.Read(ref _ownerRetired) != 0 || !requirements.IsValid)
        {
            error = ContentActorPresentationResolutionError.InvalidRequest;
            return false;
        }
        foreach (SelectedActor actor in _actors)
        {
            if (actor.Id != definitionId) continue;
            if (actor.Lease is null) return false;
            ActorPresentationArtifactInfo info = actor.Lease.Info;
            if (info.Bones != requirements.ExactBoneCount ||
                info.OrderedSkeletonFingerprint != requirements.OrderedSkeletonFingerprint ||
                (info.SemanticClips & requirements.RequiredClips) != requirements.RequiredClips)
            {
                error = ContentActorPresentationResolutionError.SkeletonProfileMismatch;
                return false;
            }
            // Publication used the normal bounded profile. A consumer may request a stricter
            // profile; the cached inspection cannot certify every per-string bound, so validate
            // that uncommon request against the same exact bytes before lending the generation.
            ActorPresentationArtifactLimits resolvedLimits =
                ActorPresentationArtifactLimits.Resolve(limits);
            if (resolvedLimits != ActorPresentationArtifactLimits.Default &&
                !ActorPresentationArtifact.TryInspect(
                    actor.Lease.Bytes.Span, out _, out _, resolvedLimits))
            {
                error = ContentActorPresentationResolutionError.InvalidArtifact;
                return false;
            }
            Interlocked.Increment(ref _references);
            lease = actor.Lease.WithLifetime(new GenerationReference(this), Generation, actor.Source);
            error = ContentActorPresentationResolutionError.None;
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        EnsureOwner();
        if (Interlocked.Exchange(ref _ownerRetired, 1) != 0) return;
        Release();
    }

    private static void ValidateLayer(ContentCatalogLease? layer, bool local, string parameter)
    {
        if (layer is null) return;
        if (local ? !layer.Catalog.Scope.IsLocal : !layer.Catalog.Scope.IsRealm)
            throw new ArgumentException(
                local ? "The Local layer must be world.local-authorized." :
                    "The Realm layer must be Realm-authorized.", parameter);
    }

    private static void AddCatalogResources(
        ContentCatalogLease? catalog,
        PresentationSourceKind source,
        SortedDictionary<LogicalContentId, CatalogSelection> textures,
        SortedDictionary<LogicalContentId, CatalogSelection> materials,
        SortedDictionary<LogicalContentId, CatalogSelection> worldMeshes,
        bool replace)
    {
        if (catalog is null) return;
        foreach (ResolvedContentEntry entry in catalog.Catalog.Entries)
        {
            SortedDictionary<LogicalContentId, CatalogSelection>? target = entry.Kind switch
            {
                ContentDefinitionKind.Texture => textures,
                ContentDefinitionKind.Material => materials,
                ContentDefinitionKind.WorldPresentation => worldMeshes,
                _ => null,
            };
            if (target is null) continue;
            var selection = new CatalogSelection(catalog, entry, source);
            if (replace) target[entry.Id] = selection;
            else target.TryAdd(entry.Id, selection);
        }
    }

    private static bool TryOpenUserTexture(
        UserResourcePackStackLease user,
        UserResourcePackIdentity identity,
        ContentPackageManifest manifest,
        ContentDefinitionDescriptor definition,
        out ResolvedTexturePresentation texture)
    {
        texture = null!;
        ContentCache? cache = user.Owner;
        if (cache is null || manifest.TrustClass != ContentTrustClass.UserResourcePack ||
            definition.Kind != ContentDefinitionKind.Texture ||
            definition.Operation != ContentDefinitionOperation.Replace ||
            !manifest.TryGetFile(definition.PrimaryFile, out ContentFileDescriptor file)) return false;
        return TryOpenTexture(cache, definition.Id, PresentationSourceKind.User,
            identity, file, out texture);
    }

    private static bool TryOpenCatalogTexture(
        ContentCatalogLease catalog,
        ResolvedContentEntry entry,
        PresentationSourceKind source,
        out ResolvedTexturePresentation texture)
    {
        texture = null!;
        ContentCache? cache = catalog.Owner;
        if (cache is null || entry.Kind != ContentDefinitionKind.Texture ||
            entry.ReferencedFiles.Count != 1 ||
            !cache.TryOpenPackage(catalog.Catalog.Scope, entry.OwnerPackageId,
                entry.OwnerManifestHash, out VerifiedContentPackage? package, out _) ||
            !package!.Manifest.TryGetFile(entry.Definition.PrimaryFile,
                out ContentFileDescriptor file) || file.Sha256 != entry.ReferencedFiles[0])
            return false;
        return TryOpenTexture(cache, entry.Id, source, default, file, out texture);
    }

    private static bool TryOpenTexture(
        ContentCache cache,
        LogicalContentId id,
        PresentationSourceKind source,
        UserResourcePackIdentity userIdentity,
        in ContentFileDescriptor file,
        out ResolvedTexturePresentation texture)
    {
        texture = null!;
        if (file.Kind is not (ContentFileKind.PngTexture or ContentFileKind.WebpTexture) ||
            file.SizeBytes > int.MaxValue ||
            !cache.TryOpenBlob(file, out ContentBlobLease? blob, out _)) return false;
        try
        {
            byte[] bytes = new byte[checked((int)file.SizeBytes)];
            if (!TryReadExact(blob!.Stream, bytes) || blob.Stream.ReadByte() != -1 ||
                !ContentRasterArtifact.TryInspect(file.Kind, bytes, out ContentRasterArtifactInfo info))
                return false;
            texture = new ResolvedTexturePresentation(
                blob, id, source, userIdentity, file, info, bytes, default,
                PresentationResourceFingerprint.Texture(id, file));
            blob = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or
            OverflowException)
        {
            return false;
        }
        finally { blob?.Dispose(); }
    }

    private ResolvedTexturePresentation? FindTexture(LogicalContentId id)
    {
        if (!id.IsValid) return null;
        foreach (SelectedTexture texture in _textures)
            if (texture.Lease.LogicalId == id) return texture.Lease;
        return null;
    }

    private static bool TryReadExact(Stream stream, Span<byte> destination)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = stream.Read(destination[offset..]);
            if (read <= 0) return false;
            offset += read;
        }
        return true;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Presentation generation publication is owner-thread-bound.");
    }

    private void Release()
    {
        if (Interlocked.Decrement(ref _references) != 0) return;
        foreach (SelectedActor actor in _actors) actor.Lease?.Dispose();
        foreach (SelectedTexture texture in _textures) texture.Lease.Dispose();
        _user?.Dispose();
        _realm?.Dispose();
        _local?.Dispose();
        _realmResources?.Dispose();
        _localResources?.Dispose();
    }

    private static bool Fail(
        PresentationResourceResolutionError value,
        out PresentationResourceResolutionError error)
    {
        error = value;
        return false;
    }

    private readonly record struct CatalogSelection(
        ContentCatalogLease Catalog,
        ResolvedContentEntry Entry,
        PresentationSourceKind Source);

    private readonly record struct SelectedActor(
        LogicalContentId Id,
        PresentationSourceKind Source,
        ActorPresentationContentLease? Lease,
        UserResourcePackIdentity UserResourcePack);

    private readonly record struct SelectedTexture(ResolvedTexturePresentation Lease);

    private readonly record struct SelectedMaterial(
        LogicalContentId Id,
        PresentationSourceKind Source,
        ContentMaterialDefinition Definition,
        ContentHash DefinitionHash,
        ContentHash StableResourceFingerprint,
        LogicalContentId AlbedoTexture);

    private sealed class GenerationReference(PresentationGeneration owner) : IDisposable
    {
        private PresentationGeneration? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }

    private sealed class NoopLifetime : IDisposable
    {
        internal static NoopLifetime Instance { get; } = new();
        public void Dispose() { }
    }
}

public interface IActorPresentationGenerationSource : IActorPresentationContentSource
{
    PresentationGenerationId CurrentPresentationGeneration { get; }
}
