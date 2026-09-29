using Godot;
using UltraSim.Client.Content;
using UltraSim.Client.Core;

namespace UltraSim.Client.Godot;

public readonly record struct GodotContentActorVisualResolverSnapshot(
    int RealizedResources,
    ulong RealizationAttempts,
    ulong RealizationFailures,
    ulong InstancesCreated,
    long RetainedArtifactBytes,
    GodotActorNativeRealizationError LastFailure);

public enum GodotActorNativeRealizationError : byte
{
    None = 0,
    ContentUnavailable,
    ImportRejected,
    SceneTypeMismatch,
    SkeletonMissing,
    BoneCountMismatch,
    MainHandBoneMismatch,
    AnimationPlayerMissing,
    SemanticClipsMissing,
    DuplicationRejected,
}

public readonly record struct GodotContentActorResourceDiagnostics(
    LogicalContentId DefinitionId,
    ContentPackageId OwnerPackage,
    ContentHash OwnerManifest,
    ContentHash FileHash,
    ContentCatalogGenerationId CatalogGeneration,
    ActorPresentationArtifactInfo Artifact)
{
    public PresentationGenerationId PresentationGeneration { get; init; }
    public PresentationSourceKind Source { get; init; }
}

/// <summary>
/// Lazy engine-thread cache which turns exact applied-catalog actor bytes into inert Godot scene
/// data. UltraSim owns the resulting node structure; no downloaded scene or script is instantiated.
/// </summary>
public sealed class GodotContentActorVisualResolver : IGodotActorVisualResolver, IDisposable
{
    private IActorPresentationContentSource _source;
    private readonly ResourceEntry?[] _entries = new ResourceEntry?[4];
    private readonly List<ResourceEntry> _retired = new(4);
    private readonly ActorPresentationResolutionRequirements _requirements;
    private readonly int _ownerThreadId = System.Environment.CurrentManagedThreadId;
    private ulong _attempts;
    private ulong _failures;
    private ulong _instances;
    private GodotActorNativeRealizationError _lastFailure;
    private bool _disposed;

    public GodotContentActorVisualResolver(IActorPresentationContentSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _requirements = ConceptDemoActorPresentationProfile.Requirements;
    }

    /// <summary>
    /// Publishes a fully prepared immutable presentation source. Existing native templates remain
    /// leased until actors have replaced their visual children and retired resources are released.
    /// </summary>
    public void UpdateSource(IActorPresentationContentSource source)
    {
        EnsureOperational();
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public Node3D? Instantiate(in ClientActorDefinition definition)
    {
        EnsureOperational();
        if (!TryDefinitionId(definition.VisualKind, out LogicalContentId id)) return null;
        int slot = (int)definition.VisualKind;
        ResourceEntry? entry = _entries[slot];
        if (_source is IActorPresentationGenerationSource generations &&
            entry is not null &&
            entry.Lease.PresentationGeneration != generations.CurrentPresentationGeneration)
        {
            _retired.Add(entry);
            _entries[slot] = null;
            entry = null;
        }
        if (entry is null)
        {
            SaturatingIncrement(ref _attempts);
            if (!_source.TryResolveActorPresentation(
                    id, _requirements, out ActorPresentationContentLease? lease,
                    out _, ActorPresentationArtifactLimits.Default))
            {
                _lastFailure = GodotActorNativeRealizationError.ContentUnavailable;
                SaturatingIncrement(ref _failures);
                return null;
            }
            try
            {
                if (!TryRealize(
                        lease!, out Node3D? template, out GodotOwnedResources? nativeResources,
                        out GodotActorNativeRealizationError failure))
                {
                    _lastFailure = failure;
                    SaturatingIncrement(ref _failures);
                    return null;
                }
                entry = new ResourceEntry(template!, lease!, nativeResources!);
                _entries[slot] = entry;
                lease = null;
            }
            finally
            {
                lease?.Dispose();
            }
        }

        Node duplicate = entry.Template.Duplicate();
        if (duplicate is not Node3D visual)
        {
            duplicate.Free();
            _lastFailure = GodotActorNativeRealizationError.DuplicationRejected;
            SaturatingIncrement(ref _failures);
            return null;
        }
        _lastFailure = GodotActorNativeRealizationError.None;
        SaturatingIncrement(ref _instances);
        return visual;
    }

    public bool TryGetDiagnostics(
        ClientActorVisualKind kind,
        out GodotContentActorResourceDiagnostics diagnostics)
    {
        EnsureOperational();
        int slot = (int)kind;
        if ((uint)slot >= (uint)_entries.Length || _entries[slot] is not { } entry)
        {
            diagnostics = default;
            return false;
        }
        ActorPresentationContentLease lease = entry.Lease;
        diagnostics = new GodotContentActorResourceDiagnostics(
            lease.DefinitionId, lease.OwnerPackage, lease.OwnerManifest, lease.FileHash,
            lease.CatalogGeneration, lease.Info)
        {
            PresentationGeneration = lease.PresentationGeneration,
            Source = lease.PresentationSource,
        };
        return true;
    }

    /// <summary>Call after actor visuals have been replaced/freed for a new generation.</summary>
    public void ReleaseRetiredResources()
    {
        EnsureOperational();
        while (_retired.Count != 0)
        {
            FreeEntry(_retired[0]);
            _retired.RemoveAt(0);
        }
    }

    public GodotContentActorVisualResolverSnapshot GetSnapshot()
    {
        EnsureOperational();
        int realized = 0;
        long bytes = 0;
        foreach (ResourceEntry? entry in _entries)
        {
            if (entry is null) continue;
            realized++;
            bytes = checked(bytes + entry.Lease.Bytes.Length);
        }
        return new GodotContentActorVisualResolverSnapshot(
            realized, _attempts, _failures, _instances, bytes, _lastFailure);
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        for (int index = 0; index < _entries.Length; index++)
        {
            ResourceEntry? entry = _entries[index];
            if (entry is null) continue;
            FreeEntry(entry);
            _entries[index] = null;
        }
        while (_retired.Count != 0)
        {
            FreeEntry(_retired[0]);
            _retired.RemoveAt(0);
        }
        _disposed = true;
    }

    private static void FreeEntry(ResourceEntry entry)
    {
        if (GodotObject.IsInstanceValid(entry.Template)) entry.Template.Free();
        entry.NativeResources.Dispose();
        entry.Lease.Dispose();
    }

    private static bool TryRealize(
        ActorPresentationContentLease lease,
        out Node3D? template,
        out GodotOwnedResources? nativeResources,
        out GodotActorNativeRealizationError error)
    {
        template = null;
        nativeResources = null;
        error = GodotActorNativeRealizationError.None;
        using var document = new GltfDocument();
        using var state = new GltfState();
        Error imported = document.AppendFromBuffer(lease.Bytes.Span, string.Empty, state, 0);
        if (imported != Error.Ok)
        {
            error = GodotActorNativeRealizationError.ImportRejected;
            return false;
        }
        Node? scene = document.GenerateScene(state, 30f, false, false);
        var resources = new GodotOwnedResources();
        try
        {
            // GLTF scene nodes own native references, not the managed ArrayMesh wrappers
            // returned by Mesh. Keep those wrappers alive through every duplicated visual,
            // then retire them after all consumers and the template have been freed.
            var captured = new HashSet<ulong>();
            CaptureMeshes(scene, resources, captured);
            if (scene is not Node3D visual)
            {
                error = GodotActorNativeRealizationError.SceneTypeMismatch;
                return false;
            }
            if (!ValidateNativeScene(visual, lease.Info, out error)) return false;
            visual.Name = $"ContentActor_{lease.DefinitionId.Value!.Replace('/', '_')}";
            template = visual;
            nativeResources = resources;
            scene = null;
            return true;
        }
        finally
        {
            if (scene is not null)
            {
                try { scene.Free(); }
                finally { resources.Dispose(); }
            }
        }
    }

    private static void CaptureMeshes(Node node, GodotOwnedResources resources, HashSet<ulong> captured)
    {
        if (node is MeshInstance3D instance && instance.Mesh is { } mesh &&
            captured.Add(mesh.GetInstanceId()))
            resources.Own(mesh);
        for (int index = 0; index < node.GetChildCount(); index++)
            CaptureMeshes(node.GetChild(index), resources, captured);
    }

    private static bool ValidateNativeScene(
        Node3D visual,
        in ActorPresentationArtifactInfo info,
        out GodotActorNativeRealizationError error)
    {
        Skeleton3D? skeleton = FindFirst<Skeleton3D>(visual);
        AnimationPlayer? animations = FindFirst<AnimationPlayer>(visual);
        if (skeleton is null)
        {
            error = GodotActorNativeRealizationError.SkeletonMissing;
            return false;
        }
        if (skeleton.GetBoneCount() != info.Bones)
        {
            error = GodotActorNativeRealizationError.BoneCountMismatch;
            return false;
        }
        bool mainHand = false;
        for (int bone = 0; bone < skeleton.GetBoneCount(); bone++)
            // Godot canonicalizes the Mixamo namespace separator while importing. The passive
            // validator already proved the exact ordered skeleton fingerprint; this native gate
            // only needs to prove that the semantic hand endpoint survived engine import.
            if (skeleton.GetBoneName(bone).ToString().EndsWith(
                    "RightHand", StringComparison.Ordinal) &&
                info.MainHandBone.EndsWith("RightHand", StringComparison.Ordinal))
                mainHand = true;
        if (!mainHand)
        {
            error = GodotActorNativeRealizationError.MainHandBoneMismatch;
            return false;
        }
        if (animations is null)
        {
            error = GodotActorNativeRealizationError.AnimationPlayerMissing;
            return false;
        }
        if (!HasAnimation(animations, "Idle") || !HasAnimation(animations, "Walk") ||
            !HasAnimation(animations, "Jump"))
        {
            error = GodotActorNativeRealizationError.SemanticClipsMissing;
            return false;
        }
        error = GodotActorNativeRealizationError.None;
        return true;
    }

    private static bool HasAnimation(AnimationPlayer player, string requested)
    {
        foreach (string candidate in player.GetAnimationList())
        {
            string text = candidate;
            int separator = Math.Max(text.LastIndexOf('/'), text.LastIndexOf(':'));
            string leaf = separator >= 0 ? text[(separator + 1)..] : text;
            if (leaf.EndsWith("-loop", StringComparison.OrdinalIgnoreCase)) leaf = leaf[..^5];
            if (string.Equals(leaf, requested, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static T? FindFirst<T>(Node root) where T : Node
    {
        if (root is T match) return match;
        foreach (Node child in root.GetChildren())
        {
            T? nested = FindFirst<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static bool TryDefinitionId(
        ClientActorVisualKind kind,
        out LogicalContentId id)
    {
        string? value = kind switch
        {
            ClientActorVisualKind.HumanX => ConceptDemoActorPresentationProfile.HumanXDefinitionId,
            ClientActorVisualKind.HumanY => ConceptDemoActorPresentationProfile.HumanYDefinitionId,
            _ => null,
        };
        return LogicalContentId.TryCreate(value, out id);
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOperational()
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwner()
    {
        if (System.Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Godot actor content realization is engine-thread-bound.");
    }

    private sealed record ResourceEntry(
        Node3D Template,
        ActorPresentationContentLease Lease,
        GodotOwnedResources NativeResources);
}
