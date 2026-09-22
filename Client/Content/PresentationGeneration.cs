using System.Security.Cryptography;
using System.Text;

namespace UltraSim.Client.Content;

public enum PresentationSourceKind : byte
{
    Primitive = 0,
    Local = 1,
    Realm = 2,
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
    bool RealmLayerPresent,
    bool LocalLayerPresent,
    int RealmSelections,
    int LocalSelections,
    int PrimitiveSelections);

/// <summary>Cold/event-driven, bounded actor presentation composition for F1.2.</summary>
/// <remarks>
/// The current Concept Demo has two compatible actor IDs. The generation owns catalog and blob
/// leases; native users obtain a reference-counted lease, so retiring the current generation cannot
/// release old catalog/blob ownership underneath an existing native resource.
/// </remarks>
public sealed class PresentationGeneration : IActorPresentationGenerationSource, IDisposable
{
    private static readonly string[] s_actorIds =
    [
        ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        ConceptDemoActorPresentationProfile.HumanYDefinitionId,
    ];

    private readonly ContentCatalogLease? _realm;
    private readonly ContentCatalogLease? _local;
    private readonly SelectedActor[] _actors = new SelectedActor[2];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _references = 1;
    private int _ownerRetired;

    private PresentationGeneration(ContentCatalogLease? realm, ContentCatalogLease? local)
    {
        if (realm is not null && !realm.Catalog.Scope.IsRealm)
            throw new ArgumentException("The Realm layer must be Realm-authorized.", nameof(realm));
        if (local is not null && !local.Catalog.Scope.IsLocal)
            throw new ArgumentException("The Local layer must be world.local-authorized.", nameof(local));
        _realm = realm;
        _local = local;
        Generation = PresentationGenerationId.Next();

        try
        {
            int realmSelections = 0;
            int localSelections = 0;
            for (int index = 0; index < s_actorIds.Length; index++)
            {
                if (!LogicalContentId.TryCreate(s_actorIds[index], out LogicalContentId id))
                    throw new InvalidOperationException("The stock actor logical identity is invalid.");
                ActorPresentationContentLease? selected = null;
                PresentationSourceKind source = PresentationSourceKind.Primitive;
                if (realm is not null && ContentActorPresentationResolver.TryResolve(
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
                _actors[index] = new SelectedActor(id, source, selected);
            }
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData("UltraSim.Presentation.F1.2.v1"u8);
            Span<byte> digest = stackalloc byte[ContentHash.ByteLength];
            foreach (SelectedActor actor in _actors)
            {
                hash.AppendData(Encoding.ASCII.GetBytes(actor.Id.Value!));
                hash.AppendData([(byte)actor.Source]);
                if (actor.Lease is not null)
                {
                    actor.Lease.FileHash.WriteDigest(digest);
                    hash.AppendData(digest);
                }
            }
            StableFingerprint = ContentHash.FromDigest(hash.GetHashAndReset());
            Snapshot = new PresentationGenerationSnapshot(
                Generation, StableFingerprint, realm is not null, local is not null,
                realmSelections, localSelections, s_actorIds.Length - realmSelections - localSelections);
        }
        catch
        {
            foreach (SelectedActor actor in _actors) actor.Lease?.Dispose();
            realm?.Dispose();
            local?.Dispose();
            throw;
        }
    }

    public PresentationGenerationId Generation { get; }
    public PresentationGenerationId CurrentPresentationGeneration => Generation;
    public ContentHash StableFingerprint { get; }
    public PresentationGenerationSnapshot Snapshot { get; }

    public static PresentationGeneration Create(
        ContentCatalogLease? realm, ContentCatalogLease? local) => new(realm, local);

    public PresentationSourceKind GetSource(LogicalContentId id)
    {
        foreach (SelectedActor actor in _actors)
            if (actor.Id == id) return actor.Source;
        return PresentationSourceKind.Primitive;
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

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Presentation generation publication is owner-thread-bound.");
    }

    private void Release()
    {
        if (Interlocked.Decrement(ref _references) != 0) return;
        foreach (SelectedActor actor in _actors) actor.Lease?.Dispose();
        _realm?.Dispose();
        _local?.Dispose();
    }

    private readonly record struct SelectedActor(
        LogicalContentId Id, PresentationSourceKind Source, ActorPresentationContentLease? Lease);

    private sealed class GenerationReference(PresentationGeneration owner) : IDisposable
    {
        private PresentationGeneration? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}

public interface IActorPresentationGenerationSource : IActorPresentationContentSource
{
    PresentationGenerationId CurrentPresentationGeneration { get; }
}
