using System.Diagnostics;
using Godot;
using UltraSim.Client.Core;
using UltraSim.Game.Composition;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Client.Godot;

public interface IGodotWorldAssetResolver
{
    GodotWorldMeshResolution ResolveStaticMesh(WorldStaticRenderKey key);
    GodotWorldMeshResolution ResolvePublicMesh(uint prefabId);
    Material? ResolveTerrainMaterial(ushort materialId);
}

/// <summary>
/// Presentation-only availability of one resolved model/material pair. This is deliberately not
/// authoritative content state: World identity, collision, and interaction remain unchanged.
/// </summary>
public enum GodotWorldAssetAvailability : byte
{
    None = 0,
    Available,
    ModelMissing,
    TextureMissing,
    ModelAndTextureMissing,
}

/// <summary>Bounded result of resolving one semantic world presentation key.</summary>
public readonly record struct GodotWorldMeshResolution(
    GodotWorldAssetAvailability Availability,
    Mesh? Mesh)
{
    public bool IsValid => Availability switch
    {
        GodotWorldAssetAvailability.Available or GodotWorldAssetAvailability.TextureMissing =>
            Mesh is not null,
        GodotWorldAssetAvailability.ModelMissing or
            GodotWorldAssetAvailability.ModelAndTextureMissing => Mesh is null,
        _ => false,
    };

    public static GodotWorldMeshResolution Available(Mesh mesh) =>
        new(GodotWorldAssetAvailability.Available,
            mesh ?? throw new ArgumentNullException(nameof(mesh)));

    public static GodotWorldMeshResolution MissingModel() =>
        new(GodotWorldAssetAvailability.ModelMissing, null);

    public static GodotWorldMeshResolution MissingTexture(Mesh mesh) =>
        new(GodotWorldAssetAvailability.TextureMissing,
            mesh ?? throw new ArgumentNullException(nameof(mesh)));

    public static GodotWorldMeshResolution MissingModelAndTexture() =>
        new(GodotWorldAssetAvailability.ModelAndTextureMissing, null);
}

/// <summary>Client presentation policy for a missing texture; never authoritative Game state.</summary>
public enum GodotWorldFallbackMaterialMode : byte
{
    Diagnostic = 1,
    Neutral = 2,
}

/// <param name="MaximumRenderChunks">Maximum concurrently realized presentation chunks.</param>
/// <param name="MaximumGroupsPerChunk">
/// Total admitted terrain surfaces plus static, Multi-fragment, and public prefab groups.
/// </param>
public readonly record struct GodotWorldRendererLimits(int MaximumRenderChunks, int MaximumGroupsPerChunk)
{
    public static GodotWorldRendererLimits Default => new(256, 128);

    /// <summary>
    /// Spare slots required to build a complete one-chunk diagonal fringe before retiring the
    /// old fringe. Smaller profiles remain valid and converge by retiring before building.
    /// </summary>
    public static int MinimumSeamlessDiagonalSpare(int renderRadiusChunks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(renderRadiusChunks);
        return checked(renderRadiusChunks * 4 + 1);
    }

    /// <summary>Derives presentation capacity from an X/Z view radius and bounded spare slots.</summary>
    public static GodotWorldRendererLimits ForStructuralWindow(
        int renderRadiusChunks,
        int spareRenderChunks,
        int maximumGroupsPerChunk = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(renderRadiusChunks);
        ArgumentOutOfRangeException.ThrowIfNegative(spareRenderChunks);
        int diameter = checked(renderRadiusChunks * 2 + 1);
        int active = checked(diameter * diameter);
        return new GodotWorldRendererLimits(
            checked(active + spareRenderChunks), maximumGroupsPerChunk);
    }

    internal void Validate(ClientWorldSemanticCache cache)
    {
        if (MaximumRenderChunks <= 0 || MaximumRenderChunks > cache.Limits.MaximumResidentChunks ||
            MaximumGroupsPerChunk <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumRenderChunks));
    }
}

public readonly record struct GodotWorldFrameBudget(int MaximumBuilds, int MaximumRetirements)
{
    public static GodotWorldFrameBudget Default => new(2, 4);
    public bool IsValid => MaximumBuilds >= 0 && MaximumRetirements >= 0;
}

/// <summary>Player-relative origin; all coordinates remain integer until float conversion.</summary>
public readonly record struct WorldRenderOrigin(
    SpatialChunkCoord Chunk,
    int YMillimeters,
    ushort LocalXMillimeters = 0,
    ushort LocalZMillimeters = 0)
{
    public bool IsCanonical =>
        LocalXMillimeters < GameWorldSpatialProfile.WorldChunkEdgeUnits &&
        LocalZMillimeters < GameWorldSpatialProfile.WorldChunkEdgeUnits;
}

public enum GodotWorldRenderQueueError : byte
{
    None = 0,
    InvalidHandle,
    SemanticChunkNotResident,
    QueueCapacityExceeded,
    RenderCapacityExceeded,
    GroupCapacityExceeded,
}

public readonly record struct GodotWorldRendererSnapshot(
    int ActiveRenderChunks,
    int FreeRenderSlots,
    int VisibleRenderChunks,
    int PendingBuilds,
    int PendingRetirements,
    int ActiveNodes,
    int ActiveResources,
    int ActiveTerrainSurfaceMeshes,
    int ActiveStaticBatchGroups,
    int ActivePublicBatchGroups,
    int ActiveFallbackGroups,
    ulong CompletedBuilds,
    ulong CompletedRetirements,
    ulong Rebuilds,
    ulong PublicRefreshes,
    ulong RenderCapacityRejections,
    ulong GroupCapacityRejections,
    ulong Rebases,
    ulong WindowMoves,
    ulong CancelledBuilds,
    ulong CancelledRetirements,
    ulong ChunkRootTransformWrites,
    ulong GroupValidationCalls,
    ulong VerticalRebases,
    long LastMeshBuildTicks,
    long MaximumMeshBuildTicks,
    long LastResourceCommitTicks,
    long MaximumResourceCommitTicks,
    long LastFrameTicks,
    long MaximumFrameTicks)
{
    public GodotWorldInstanceBackend InstanceBackend { get; init; }
    public GodotWorldInstanceUpload InstanceUpload { get; init; }
    public int NodeBackedGroups { get; init; }
    public int DirectRidGroups { get; init; }
    public int ActiveMultiMeshes { get; init; }
    public int ActiveVisualInstanceRids { get; init; }
    public int ActiveInstances { get; init; }
    public ulong BulkUploads { get; init; }
    public ulong TargetedInstanceUpdates { get; init; }
    public ulong UploadedInstanceRows { get; init; }
    public ulong BulkUploadBytes { get; init; }
    public long GroupPreparationTicks { get; init; }
    public long GroupingTicks { get; init; }
    public long UploadScratchBytes { get; init; }
    /// <summary>Live public-layer roots, batch nodes and known-name labels; excludes structural nodes.</summary>
    public int ActivePublicNodes { get; init; }
    public int PresentedPublicPrefabKinds { get; init; }
    public int PresentedPublicInstances { get; init; }
    /// <summary>On-demand debug formatting, never called by the renderer hot path.</summary>
    public string ToDiagnosticText() =>
        $"world={InstanceBackend}/{InstanceUpload} chunks={ActiveRenderChunks} nodes={ActiveNodes}\n" +
        $"groups={ActiveMultiMeshes} visualRids={ActiveVisualInstanceRids} instances={ActiveInstances}\n" +
        $"bulk={BulkUploads} targetedRows={TargetedInstanceUpdates}";
    public double LastMeshBuildMicroseconds => LastMeshBuildTicks * 1_000_000d / Stopwatch.Frequency;
    public double MaximumMeshBuildMicroseconds => MaximumMeshBuildTicks * 1_000_000d / Stopwatch.Frequency;
    public double LastResourceCommitMicroseconds => LastResourceCommitTicks * 1_000_000d / Stopwatch.Frequency;
    public double MaximumResourceCommitMicroseconds => MaximumResourceCommitTicks * 1_000_000d / Stopwatch.Frequency;
    public double LastFrameMicroseconds => LastFrameTicks * 1_000_000d / Stopwatch.Frequency;
    public double MaximumFrameMicroseconds => MaximumFrameTicks * 1_000_000d / Stopwatch.Frequency;
}

/// <summary>Presentation diagnostics only; none of these values are world identity.</summary>
public readonly record struct GodotWorldRenderChunkDiagnostics(
    ClientSemanticChunkHandle SemanticHandle,
    int PrivateRenderSlot,
    ClientPresentationOffset WindowOffset,
    int YAnchorMillimeters,
    float RootLocalX,
    float RootLocalY,
    float RootLocalZ,
    float FirstTerrainLocalY,
    int TerrainTriangleCount,
    float FirstStaticLocalY,
    float FirstPublicLocalY,
    ulong StructuralObjectId,
    ulong PublicObjectId,
    ulong TerrainMeshObjectId,
    int AdditionalTerrainSurfaceFragmentCount,
    int AdditionalTerrainTriangleCount,
    float FirstAdditionalTerrainLocalY,
    int ModelFallbackGroups,
    int TextureFallbackGroups,
    int ModelAndTextureFallbackGroups);

/// <summary>
/// Engine-thread presentation over the production semantic cache. Private render slots are mapped
/// from semantic handles; slot address spaces and capacities remain independent. Immutable
/// structural presentation and refreshable public presentation have separate ownership.
/// </summary>
public sealed class GodotWorldRenderer : IDisposable
{
    private const float MillimetersToMeters = 0.001f;
    private const int VerticalRebaseThresholdMillimeters = 256_000;

    private readonly int _ownerThreadId = System.Environment.CurrentManagedThreadId;
    private readonly ClientWorldSemanticCache _cache;
    private readonly GodotWorldRendererLimits _limits;
    private IGodotWorldAssetResolver? _assets;
    private readonly GodotVegetation? _vegetation;
    private long _vegetationPreparationTicks;
    private bool _vegetationShadows;
    private readonly IGodotWorldPublicBatchFilter? _publicBatchFilter;
    private readonly RenderChunk?[] _renderChunks;
    private readonly Dictionary<uint, int> _presentedPublicPrefabs = new();
    private readonly RenderAssociation[] _associations;
    private readonly int[] _freeRenderSlots;
    private readonly RenderWorkItem[] _buildQueue;
    private readonly ClientSemanticChunkHandle[] _retireQueue;
    private readonly bool[] _desiredVisible;
    private readonly bool[] _desiredResident;
    private readonly ClientPresentationOffset[] _desiredWindowOffsets;
    private readonly TerrainBaseMeshVertex[] _terrainVertices =
        new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
    private readonly ushort[] _terrainIndices = new ushort[TerrainChunkFormat.TriangleIndexCount];
    private readonly ushort[] _terrainMaterials = new ushort[TerrainChunkFormat.TriangleCount];
    private readonly TerrainBaseMeshVertex[] _additionalTerrainVertices =
        new TerrainBaseMeshVertex[TerrainSurfaceBandMesh.MaximumVertexCount];
    private readonly ushort[] _additionalTerrainIndices =
        new ushort[TerrainSurfaceBandMesh.MaximumIndexCount];
    private readonly ushort[] _additionalTerrainMaterials =
        new ushort[TerrainSurfaceBandMesh.MaximumTriangleCount];
    private readonly ArrayMesh _fallbackStaticMesh;
    private readonly ArrayMesh _fallbackPublicMesh;
    private readonly StandardMaterial3D _neutralFallbackMaterial;
    private readonly ShaderMaterial _diagnosticFallbackMaterial;
    private readonly Shader _diagnosticFallbackShader;
    private readonly GodotWorldFallbackMaterialMode _fallbackMaterialMode;
    private readonly GodotWorldInstanceBackend _instanceBackend;
    private readonly GodotWorldInstanceUpload _instanceUpload;
    private readonly float[] _uploadScratch;
    private int _activeInstances;
    private ulong _bulkUploads;
    private ulong _targetedInstanceUpdates;
    private ulong _uploadedInstanceRows;
    private ulong _bulkUploadBytes;
    private long _groupPreparationTicks;
    private long _groupingTicks;
    private int _freeRenderSlotCount;
    private int _buildHead;
    private int _buildCount;
    private int _retireHead;
    private int _retireCount;
    private WorldRenderOrigin _origin;
    private int _activeChunks;
    private int _visibleChunks;
    private int _activeNodes;
    private int _activePublicNodes;
    private int _activeResources;
    private int _activeTerrainSurfaceMeshes;
    private int _activeStaticBatchGroups;
    private int _activePublicBatchGroups;
    private int _activeFallbackGroups;
    private ulong _completedBuilds;
    private ulong _completedRetirements;
    private ulong _rebuilds;
    private ulong _publicRefreshes;
    private ulong _renderCapacityRejections;
    private ulong _groupCapacityRejections;
    private ulong _rebases;
    private ulong _windowMoves;
    private ulong _cancelledBuilds;
    private ulong _cancelledRetirements;
    private ulong _chunkRootTransformWrites;
    private ulong _groupValidationCalls;
    private ulong _verticalRebases;
    private long _lastMeshBuildTicks;
    private long _maximumMeshBuildTicks;
    private long _lastResourceCommitTicks;
    private long _maximumResourceCommitTicks;
    private long _lastFrameTicks;
    private long _maximumFrameTicks;
    private bool _disposed;
    private bool _publicNamesRevealHeld;
    private bool _verticalAnchorInitialized = true;
    private int _verticalAnchorMillimeters;

    public GodotWorldRenderer(Node3D host, ClientWorldSemanticCache cache,
        GodotWorldRendererLimits limits = default, IGodotWorldAssetResolver? assets = null,
        GodotWorldFallbackMaterialMode fallbackMaterialMode =
            GodotWorldFallbackMaterialMode.Diagnostic,
        IGodotWorldPublicBatchFilter? publicBatchFilter = null,
        GodotWorldInstanceBackend instanceBackend = GodotWorldInstanceBackend.NodeMultiMesh,
        GodotWorldInstanceUpload instanceUpload = GodotWorldInstanceUpload.PerRow,
        bool vegetationPrototype = false)
    {
        ArgumentNullException.ThrowIfNull(host);
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        if (limits == default) limits = GodotWorldRendererLimits.Default with
        {
            MaximumRenderChunks = Math.Min(
                GodotWorldRendererLimits.Default.MaximumRenderChunks,
                cache.Limits.MaximumResidentChunks),
        };
        limits.Validate(cache);
        _limits = limits;
        _assets = assets;
        _publicBatchFilter = publicBatchFilter;
        if (!Enum.IsDefined(instanceBackend) || !Enum.IsDefined(instanceUpload))
            throw new ArgumentOutOfRangeException(nameof(instanceBackend));
        _instanceBackend = instanceBackend;
        _instanceUpload = instanceUpload;
        _uploadScratch = instanceUpload == GodotWorldInstanceUpload.PerRow ? [] :
            new float[checked(GodotWorldInstanceGroup.FloatsPerTransform * Math.Max(
                cache.Limits.MaximumFrozenStaticsPerChunk + cache.Limits.MaximumMultiFragmentsPerChunk,
                cache.Limits.MaximumPublicRecordsPerChunk))];
        if (fallbackMaterialMode is not (GodotWorldFallbackMaterialMode.Diagnostic or
                GodotWorldFallbackMaterialMode.Neutral))
            throw new ArgumentOutOfRangeException(nameof(fallbackMaterialMode));
        _fallbackMaterialMode = fallbackMaterialMode;
        _renderChunks = new RenderChunk[limits.MaximumRenderChunks];
        _associations = new RenderAssociation[cache.Limits.MaximumResidentChunks];
        _freeRenderSlots = new int[limits.MaximumRenderChunks];
        for (int index = 0; index < _freeRenderSlots.Length; index++)
            _freeRenderSlots[index] = _freeRenderSlots.Length - index - 1;
        _freeRenderSlotCount = _freeRenderSlots.Length;
        _buildQueue = new RenderWorkItem[cache.Limits.MaximumResidentChunks];
        _retireQueue = new ClientSemanticChunkHandle[cache.Limits.MaximumResidentChunks];
        _desiredVisible = new bool[cache.Limits.MaximumResidentChunks];
        _desiredResident = new bool[cache.Limits.MaximumResidentChunks];
        _desiredWindowOffsets = new ClientPresentationOffset[cache.Limits.MaximumResidentChunks];
        _neutralFallbackMaterial = GodotPrimitiveSubstrate.CreateNeutralMaterial();
        _diagnosticFallbackMaterial = GodotPrimitiveSubstrate.CreateMissingTextureMaterial(
            out _diagnosticFallbackShader);
        _fallbackStaticMesh = GodotMeshGeometry.CreateGroundedBox(
            Vector3.One, _neutralFallbackMaterial);
        _fallbackPublicMesh = GodotMeshGeometry.CreateGroundedBox(
            Vector3.One, _neutralFallbackMaterial);
        Root = new Node3D { Name = "UltraSimWorld" };
        host.AddChild(Root);
        _activeNodes = 1;
        if (vegetationPrototype) { _vegetation = new GodotVegetation(); _activeResources = 3; }
    }

    public Node3D Root { get; }
    public GodotWorldRendererLimits Limits => _limits;
    public GodotWorldFallbackMaterialMode FallbackMaterialMode => _fallbackMaterialMode;

    /// <summary>Committed public instances, including fallback and direct-RID groups.</summary>
    public int GetPresentedPrefabCount(uint prefabId)
    {
        EnsureOwner();
        ThrowIfDisposed();
        return _presentedPublicPrefabs.TryGetValue(prefabId, out int count) ? count : 0;
    }

    /// <summary>Chunk-local committed count for a semantic record's exact presentation owner.</summary>
    public int GetPresentedPrefabCount(ClientSemanticChunkHandle handle, uint prefabId)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (!TryFindRenderSlot(handle, out int slot)) return 0;
        foreach (PresentedPrefabCount entry in _renderChunks[slot]!.PublicPrefabCounts)
            if (entry.PrefabId == prefabId) return entry.Count;
        return 0;
    }

    /// <summary>
    /// Shows only generic world-object names retained by this renderer. Detailed item inspection
    /// remains pointer-driven and is deliberately not part of the reveal modifier.
    /// </summary>
    public void SetPublicNamesRevealHeld(bool held)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (_publicNamesRevealHeld == held) return;
        _publicNamesRevealHeld = held;
        foreach (RenderChunk? chunk in _renderChunks)
        {
            Node3D? names = chunk?.PublicRoot.GetNodeOrNull<Node3D>("PublicNames");
            if (names is not null) names.Visible = held;
        }
    }

    public bool TryEnterPresentation(ClientSemanticChunkHandle handle, bool visible,
        out GodotWorldRenderQueueError error)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (!_cache.TryGetInfo(handle, out ClientSemanticChunkInfo info))
        {
            error = GodotWorldRenderQueueError.InvalidHandle;
            return false;
        }
        long offsetX = checked((long)info.Key.Coord.X - _origin.Chunk.X);
        long offsetZ = checked((long)info.Key.Coord.Z - _origin.Chunk.Z);
        if (offsetX is < int.MinValue or > int.MaxValue ||
            offsetZ is < int.MinValue or > int.MaxValue)
        {
            error = GodotWorldRenderQueueError.InvalidHandle;
            return false;
        }
        return TryEnterPresentationCore(
            handle, new ClientPresentationOffset((int)offsetX, (int)offsetZ), visible, out error);
    }

    private bool TryEnterPresentationCore(ClientSemanticChunkHandle handle,
        ClientPresentationOffset windowOffset,
        bool visible, out GodotWorldRenderQueueError error)
        => TryEnterPresentationCore(handle, windowOffset, visible, validateGroups: true, out error);

    private bool TryEnterPresentationCore(ClientSemanticChunkHandle handle,
        ClientPresentationOffset windowOffset,
        bool visible,
        bool validateGroups,
        out GodotWorldRenderQueueError error)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (!TryGetSemanticIndex(handle, out int semanticIndex))
        {
            error = GodotWorldRenderQueueError.InvalidHandle;
            return false;
        }
        if (!_cache.TryGetResidentView(handle, out ClientSemanticChunkView semantic))
        {
            error = GodotWorldRenderQueueError.SemanticChunkNotResident;
            return false;
        }
        if (validateGroups && !GroupsFit(semantic))
        {
            _groupCapacityRejections++;
            error = GodotWorldRenderQueueError.GroupCapacityExceeded;
            return false;
        }

        bool hasCurrent = TryFindRenderSlot(handle, out int renderSlot);
        bool hasPendingBuild = Contains(_buildQueue, _buildHead, _buildCount, handle);
        bool hasPendingRetirement = Contains(_retireQueue, _retireHead, _retireCount, handle);
        RenderWorkKind kind = hasCurrent ? RenderWorkKind.PublicRefresh : RenderWorkKind.FullBuild;
        bool replacesStaleAssociation = !hasCurrent && TryFindAnyAssociation(semanticIndex, out _);
        if (!hasPendingBuild && kind == RenderWorkKind.FullBuild && !replacesStaleAssociation &&
            _activeChunks - (_retireCount - (hasPendingRetirement ? 1 : 0)) +
                CountPendingNewBuilds() >= _limits.MaximumRenderChunks)
        {
            _renderCapacityRejections++;
            error = GodotWorldRenderQueueError.RenderCapacityExceeded;
            return false;
        }
        if (!hasPendingBuild && _buildCount == _buildQueue.Length)
        {
            error = GodotWorldRenderQueueError.QueueCapacityExceeded;
            return false;
        }

        _desiredResident[semanticIndex] = true;
        _desiredVisible[semanticIndex] = visible;
        _desiredWindowOffsets[semanticIndex] = windowOffset;
        if (RemoveQueued(_retireQueue, ref _retireHead, ref _retireCount, handle))
            _cancelledRetirements++;
        if (hasCurrent)
        {
            RenderChunk current = _renderChunks[renderSlot]!;
            SetWindowOffset(current, windowOffset);
            if (current.SemanticHash == semantic.Info.SemanticHash)
            {
                SetVisible(current, visible);
                error = GodotWorldRenderQueueError.None;
                return true;
            }
        }
        if (hasPendingBuild)
        {
            error = GodotWorldRenderQueueError.None;
            return true;
        }
        Enqueue(_buildQueue, ref _buildHead, ref _buildCount, new RenderWorkItem(handle, kind));
        error = GodotWorldRenderQueueError.None;
        return true;
    }

    /// <summary>
    /// Queues public-layer work only for presentation already desired by the window owner. This
    /// operation never enters residency, changes visibility/offset, or cancels retirement.
    /// </summary>
    public bool TryQueuePublicRefresh(
        ClientSemanticChunkHandle handle,
        out GodotWorldRenderQueueError error)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (!TryGetSemanticIndex(handle, out int semanticIndex))
        {
            error = GodotWorldRenderQueueError.InvalidHandle;
            return false;
        }
        if (!_cache.TryGetResidentView(handle, out ClientSemanticChunkView semantic))
        {
            error = GodotWorldRenderQueueError.SemanticChunkNotResident;
            return false;
        }
        if (!_desiredResident[semanticIndex])
        {
            error = GodotWorldRenderQueueError.None;
            return true;
        }
        if (!GroupsFit(semantic))
        {
            _groupCapacityRejections++;
            error = GodotWorldRenderQueueError.GroupCapacityExceeded;
            return false;
        }
        if (Contains(_buildQueue, _buildHead, _buildCount, handle) ||
            !TryFindRenderSlot(handle, out int renderSlot) ||
            _renderChunks[renderSlot]!.SemanticHash == semantic.Info.SemanticHash)
        {
            error = GodotWorldRenderQueueError.None;
            return true;
        }
        if (_buildCount == _buildQueue.Length)
        {
            error = GodotWorldRenderQueueError.QueueCapacityExceeded;
            return false;
        }
        Enqueue(_buildQueue, ref _buildHead, ref _buildCount,
            new RenderWorkItem(handle, RenderWorkKind.PublicRefresh));
        error = GodotWorldRenderQueueError.None;
        return true;
    }

    public bool TryQueueRetire(ClientSemanticChunkHandle handle, out GodotWorldRenderQueueError error)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (!TryGetSemanticIndex(handle, out int semanticIndex))
        {
            error = GodotWorldRenderQueueError.InvalidHandle;
            return false;
        }
        _desiredResident[semanticIndex] = false;
        _desiredVisible[semanticIndex] = false;
        if (RemoveQueued(_buildQueue, ref _buildHead, ref _buildCount, handle))
            _cancelledBuilds++;
        if (!TryFindRenderSlot(handle, out _) ||
            Contains(_retireQueue, _retireHead, _retireCount, handle))
        {
            error = GodotWorldRenderQueueError.None;
            return true;
        }
        if (_retireCount == _retireQueue.Length)
        {
            error = GodotWorldRenderQueueError.QueueCapacityExceeded;
            return false;
        }
        Enqueue(_retireQueue, ref _retireHead, ref _retireCount, handle);
        error = GodotWorldRenderQueueError.None;
        return true;
    }

    public bool TrySetVisible(ClientSemanticChunkHandle handle, bool visible)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (!TryGetSemanticIndex(handle, out int semanticIndex)) return false;
        if (!TryFindRenderSlot(handle, out int renderSlot)) return false;
        _desiredVisible[semanticIndex] = visible;
        SetVisible(_renderChunks[renderSlot]!, visible);
        return true;
    }

    /// <summary>Moves one retained presentation binding without changing semantic identity.</summary>
    public bool TrySetWindowOffset(ClientSemanticChunkHandle handle, ClientPresentationOffset offset)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (!TryGetSemanticIndex(handle, out int semanticIndex)) return false;
        if (!TryFindRenderSlot(handle, out int renderSlot)) return false;
        _desiredWindowOffsets[semanticIndex] = offset;
        SetWindowOffset(_renderChunks[renderSlot]!, offset);
        return true;
    }

    private bool TryMovePresentation(
        ClientSemanticChunkHandle handle,
        ClientPresentationOffset offset)
    {
        if (!TryGetSemanticIndex(handle, out int semanticIndex) ||
            !_cache.TryGetInfo(handle, out ClientSemanticChunkInfo info) ||
            info.State != ClientSemanticChunkState.Resident)
            return false;
        bool rendered = TryFindRenderSlot(handle, out int renderSlot);
        bool pendingBuild = Contains(_buildQueue, _buildHead, _buildCount, handle);
        bool pendingRetire = Contains(_retireQueue, _retireHead, _retireCount, handle);
        if (!rendered && !pendingBuild && !pendingRetire) return false;
        _desiredResident[semanticIndex] = true;
        _desiredVisible[semanticIndex] = true;
        _desiredWindowOffsets[semanticIndex] = offset;
        if (RemoveQueued(_retireQueue, ref _retireHead, ref _retireCount, handle))
            _cancelledRetirements++;
        if (rendered)
        {
            RenderChunk chunk = _renderChunks[renderSlot]!;
            SetWindowOffset(chunk, offset);
            SetVisible(chunk, true);
        }
        return true;
    }

    public void SetRenderOrigin(WorldRenderOrigin origin)
        => SetRenderOriginCore(origin, windowOffsetsAlreadyUpdated: false,
            rebasePendingRetirements: true);

    internal void SetRenderOriginFromWindow(
        WorldRenderOrigin origin,
        bool rebasePendingRetirements = true)
        => SetRenderOriginCore(origin, windowOffsetsAlreadyUpdated: true,
            rebasePendingRetirements);

    private void SetRenderOriginCore(
        WorldRenderOrigin origin,
        bool windowOffsetsAlreadyUpdated,
        bool rebasePendingRetirements)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (!origin.IsCanonical) throw new ArgumentOutOfRangeException(nameof(origin));
        if (_origin == origin) return;
        bool centerChanged = _origin.Chunk != origin.Chunk;
        bool verticalRebase = !_verticalAnchorInitialized ||
            Math.Abs(checked((long)origin.YMillimeters - _verticalAnchorMillimeters)) >
                VerticalRebaseThresholdMillimeters;
        if (verticalRebase)
        {
            _verticalAnchorMillimeters = origin.YMillimeters;
            _verticalAnchorInitialized = true;
            _verticalRebases++;
        }
        _origin = origin;
        Root.Position = new Vector3(
            -origin.LocalXMillimeters * MillimetersToMeters,
            -checked((long)origin.YMillimeters - _verticalAnchorMillimeters) *
                MillimetersToMeters,
            -origin.LocalZMillimeters * MillimetersToMeters);

        if (centerChanged && windowOffsetsAlreadyUpdated && rebasePendingRetirements)
        {
            for (int offset = 0; offset < _retireCount; offset++)
            {
                ClientSemanticChunkHandle handle =
                    _retireQueue[(_retireHead + offset) % _retireQueue.Length];
                if (!TryFindRenderSlot(handle, out int renderSlot)) continue;
                RenderChunk chunk = _renderChunks[renderSlot]!;
                if (!TryGetRelativeOffset(chunk.Key, origin.Chunk, out ClientPresentationOffset relative))
                {
                    SetVisible(chunk, false);
                    continue;
                }
                _desiredWindowOffsets[checked((int)handle.Index)] = relative;
                SetWindowOffset(chunk, relative, countWindowMove: false);
            }
        }

        if ((centerChanged && !windowOffsetsAlreadyUpdated) || verticalRebase)
        {
            for (int index = 0; index < _renderChunks.Length; index++)
            {
                if (_renderChunks[index] is not { } chunk) continue;
                if (centerChanged && !windowOffsetsAlreadyUpdated)
                {
                    if (!TryGetRelativeOffset(chunk.Key, origin.Chunk,
                        out ClientPresentationOffset relative))
                        throw new InvalidOperationException(
                            "A rendered chunk is outside relative offset range.");
                    chunk.WindowOffset = relative;
                    _desiredWindowOffsets[checked((int)chunk.Handle.Index)] = relative;
                }
                ApplyOrigin(chunk);
            }
        }
        _rebases++;
        SynchronizeDirectGroups();
    }

    public void ProcessFrame(GodotWorldFrameBudget budget)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (!budget.IsValid) throw new ArgumentOutOfRangeException(nameof(budget));
        long frameStart = Stopwatch.GetTimestamp();
        int builds = 0;
        while (builds < budget.MaximumBuilds && TryProcessNextBuild()) builds++;
        SynchronizeDirectGroups();
        for (int count = 0; count < budget.MaximumRetirements && _retireCount != 0; count++)
            Retire(Dequeue(_retireQueue, ref _retireHead, ref _retireCount));
        while (builds < budget.MaximumBuilds && TryProcessNextBuild()) builds++;
        _lastFrameTicks = Stopwatch.GetTimestamp() - frameStart;
        _maximumFrameTicks = Math.Max(_maximumFrameTicks, _lastFrameTicks);
    }

    public bool IsRendered(ClientSemanticChunkHandle handle)
    {
        EnsureOwner();
        return TryFindRenderSlot(handle, out _);
    }

    public bool TryGetChunkDiagnostics(ClientSemanticChunkHandle handle,
        out GodotWorldRenderChunkDiagnostics diagnostics)
    {
        EnsureOwner();
        if (!TryFindRenderSlot(handle, out int renderSlot))
        {
            diagnostics = default;
            return false;
        }
        RenderChunk chunk = _renderChunks[renderSlot]!;
        diagnostics = new GodotWorldRenderChunkDiagnostics(
            handle, renderSlot, chunk.WindowOffset, chunk.YAnchorMillimeters,
            chunk.Root.Position.X, chunk.Root.Position.Y, chunk.Root.Position.Z,
            chunk.FirstTerrainLocalY, chunk.TerrainTriangleCount,
            chunk.FirstStaticLocalY, chunk.FirstPublicLocalY,
            chunk.StructuralRoot.GetInstanceId(), chunk.PublicRoot.GetInstanceId(),
            chunk.TerrainMesh.GetInstanceId(), chunk.AdditionalTerrainSurfaceFragmentCount,
            chunk.AdditionalTerrainTriangleCount, chunk.FirstAdditionalTerrainLocalY,
            (chunk.StructuralFallbacks + chunk.PublicFallbacks).Model,
            (chunk.StructuralFallbacks + chunk.PublicFallbacks).Texture,
            (chunk.StructuralFallbacks + chunk.PublicFallbacks).ModelAndTexture);
        return true;
    }

    public GodotVegetationSnapshot GetVegetationSnapshot()
    {
        EnsureOwner();
        int chunks = 0, patches = 0, collapsed = 0; ulong writes = 0;
        foreach (var chunk in _renderChunks)
            if (chunk?.Vegetation is { } grass)
            { chunks++; patches += grass.Count; collapsed += grass.CollapsedBlades; writes += grass.CutWrites; }
        return new(chunks, patches, patches * GodotVegetation.BladesPerPatch,
            patches * GodotVegetation.BladesPerPatch - collapsed,
            _vegetation is null ? 0 : GodotVegetation.BladesPerPatch * GodotVegetation.VerticesPerBlade,
            (long)patches * GodotVegetation.BladesPerPatch * GodotVegetation.VerticesPerBlade,
            _vegetation is null ? 0 : ClientVegetationScatter.MaximumPatchesPerChunk * 20L,
            writes, _vegetationPreparationTicks, _vegetation is not null && _vegetationShadows);
    }

    /// <summary>Diagnostic-only shadow comparison; no Game policy or vegetation state changes.</summary>
    public void SetVegetationShadows(bool enabled)
    {
        EnsureOwner(); ThrowIfDisposed(); _vegetationShadows = enabled;
        foreach (var chunk in _renderChunks)
            if (chunk?.Vegetation is { } grass)
                grass.Node.CastShadow = enabled ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
    }

    public bool TryGetVegetation(ClientSemanticChunkHandle handle, out GodotVegetationChunk? vegetation)
    {
        EnsureOwner();
        vegetation = TryFindRenderSlot(handle, out int slot) ? _renderChunks[slot]?.Vegetation : null;
        return vegetation is not null;
    }

    public GodotWorldRendererSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new GodotWorldRendererSnapshot(
            _activeChunks, _freeRenderSlotCount, _visibleChunks, _buildCount, _retireCount,
            _activeNodes, _activeResources,
            _activeTerrainSurfaceMeshes, _activeStaticBatchGroups,
            _activePublicBatchGroups, _activeFallbackGroups,
            _completedBuilds, _completedRetirements,
            _rebuilds, _publicRefreshes, _renderCapacityRejections, _groupCapacityRejections,
            _rebases, _windowMoves, _cancelledBuilds, _cancelledRetirements,
            _chunkRootTransformWrites, _groupValidationCalls, _verticalRebases,
            _lastMeshBuildTicks, _maximumMeshBuildTicks,
            _lastResourceCommitTicks, _maximumResourceCommitTicks,
            _lastFrameTicks, _maximumFrameTicks)
        {
            InstanceBackend = _instanceBackend, InstanceUpload = _instanceUpload,
            NodeBackedGroups = _instanceBackend == GodotWorldInstanceBackend.NodeMultiMesh
                ? _activeStaticBatchGroups + _activePublicBatchGroups : 0,
            DirectRidGroups = _instanceBackend == GodotWorldInstanceBackend.DirectRid
                ? _activeStaticBatchGroups + _activePublicBatchGroups : 0,
            ActiveMultiMeshes = _activeStaticBatchGroups + _activePublicBatchGroups,
            ActiveVisualInstanceRids = _activeStaticBatchGroups + _activePublicBatchGroups,
            ActiveInstances = _activeInstances,
            BulkUploads = _bulkUploads, TargetedInstanceUpdates = _targetedInstanceUpdates,
            UploadedInstanceRows = _uploadedInstanceRows, BulkUploadBytes = _bulkUploadBytes,
            GroupPreparationTicks = _groupPreparationTicks, GroupingTicks = _groupingTicks,
            UploadScratchBytes = (long)_uploadScratch.Length * sizeof(float),
            ActivePublicNodes = _activePublicNodes,
            PresentedPublicPrefabKinds = _presentedPublicPrefabs.Count,
            PresentedPublicInstances = SumPresentedPublicInstances(),
        };
    }

    private int SumPresentedPublicInstances()
    {
        int count = 0;
        foreach (int instances in _presentedPublicPrefabs.Values)
            count = checked(count + instances);
        return count;
    }

    /// <summary>Native diagnostics by renderer-local group name, not semantic/native identity binding.</summary>
    public bool TryGetInstanceGroup(ClientSemanticChunkHandle handle, string name,
        out GodotWorldInstanceGroupDiagnostics diagnostics, Span<Transform3D> transforms)
    {
        EnsureOwner();
        if (TryFindRenderSlot(handle, out int slot))
        {
            RenderChunk chunk = _renderChunks[slot]!;
            foreach (GodotWorldInstanceGroup group in chunk.StructuralGroups.Concat(chunk.PublicGroups))
            {
                if (group.Name != name) continue;
                if (transforms.Length < group.Count) throw new ArgumentException("Diagnostic destination too small.");
                for (int index = 0; index < group.Count; index++)
                    transforms[index] = group.MultiMesh.GetInstanceTransform(index);
                diagnostics = new(group.Name, group.Direct, group.Count, group.Bounds,
                    group.Direct ? group.Scenario : chunk.Root.GetWorld3D().Scenario,
                    group.VisualRid, group.Direct ? group.GlobalTransform : chunk.Root.GlobalTransform,
                    group.Direct ? group.Visible : chunk.Root.IsVisibleInTree(), group.TransformWrites, group.ScenarioBindings,
                    group.MultiMesh.GetRid(), group.MeshRid, group.MaterialOverrideRid);
                return true;
            }
        }
        diagnostics = default;
        return false;
    }

    internal bool TryQueueWindowTransition(
        ReadOnlySpan<ClientPresentationChange> changes,
        bool hideExits,
        out GodotWorldRenderQueueError error)
    {
        EnsureOwner();
        ThrowIfDisposed();
        for (int index = 0; index < changes.Length; index++)
        {
            ClientPresentationChange change = changes[index];
            if (change.Kind == ClientPresentationChangeKind.Enter)
            {
                if (!_cache.TryGetResidentView(change.Handle, out ClientSemanticChunkView semantic))
                {
                    error = GodotWorldRenderQueueError.SemanticChunkNotResident;
                    return false;
                }
                if (!GroupsFit(semantic))
                {
                    error = GodotWorldRenderQueueError.GroupCapacityExceeded;
                    return false;
                }
            }
            else if (change.Kind == ClientPresentationChangeKind.Move &&
                (!_cache.TryGetInfo(change.Handle, out ClientSemanticChunkInfo info) ||
                    info.State != ClientSemanticChunkState.Resident ||
                    !TryFindRenderSlot(change.Handle, out _) &&
                    !Contains(_buildQueue, _buildHead, _buildCount, change.Handle) &&
                    !Contains(_retireQueue, _retireHead, _retireCount, change.Handle)))
            {
                error = GodotWorldRenderQueueError.InvalidHandle;
                return false;
            }
            else if (change.Kind == ClientPresentationChangeKind.Exit &&
                !TryFindRenderSlot(change.Handle, out _) &&
                !Contains(_buildQueue, _buildHead, _buildCount, change.Handle) &&
                !Contains(_retireQueue, _retireHead, _retireCount, change.Handle))
            {
                error = GodotWorldRenderQueueError.InvalidHandle;
                return false;
            }
        }
        for (int index = 0; index < changes.Length; index++)
        {
            ClientPresentationChange change = changes[index];
            if (change.Kind == ClientPresentationChangeKind.Exit &&
                !TryQueueRetire(change.Handle, out error)) return false;
        }
        for (int index = 0; index < changes.Length; index++)
        {
            ClientPresentationChange change = changes[index];
            if (change.Kind == ClientPresentationChangeKind.Move &&
                !TryMovePresentation(change.Handle, change.CurrentOffset))
            {
                error = GodotWorldRenderQueueError.InvalidHandle;
                return false;
            }
        }
        for (int index = 0; index < changes.Length; index++)
        {
            ClientPresentationChange change = changes[index];
            if (change.Kind == ClientPresentationChangeKind.Enter &&
                !TryEnterPresentationCore(change.Handle, change.CurrentOffset, true,
                    validateGroups: false, out error)) return false;
        }
        if (hideExits)
        {
            for (int offset = 0; offset < _retireCount; offset++)
            {
                ClientSemanticChunkHandle handle =
                    _retireQueue[(_retireHead + offset) % _retireQueue.Length];
                if (TryFindRenderSlot(handle, out int renderSlot))
                    SetVisible(_renderChunks[renderSlot]!, false);
            }
        }
        error = GodotWorldRenderQueueError.None;
        return true;
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        for (int index = 0; index < _renderChunks.Length; index++) DisposeChunk(index);
        if (GodotObject.IsInstanceValid(Root)) Root.Free();
        _fallbackStaticMesh.Dispose();
        _fallbackPublicMesh.Dispose();
        _diagnosticFallbackMaterial.Dispose();
        _diagnosticFallbackShader.Dispose();
        _neutralFallbackMaterial.Dispose();
        _buildHead = 0;
        _buildCount = 0;
        _retireHead = 0;
        _retireCount = 0;
        _activeNodes = 0;
        _activePublicNodes = 0;
        _vegetation?.Dispose();
        _activeResources = 0;
        _presentedPublicPrefabs.Clear();
        _disposed = true;
    }

    private void Build(ClientSemanticChunkHandle handle)
    {
        if (!_cache.TryGetResidentView(handle, out ClientSemanticChunkView semantic) || !GroupsFit(semantic))
            return;
        int semanticIndex = checked((int)handle.Index);
        bool replacing = TryFindAnyAssociation(semanticIndex, out int renderSlot);
        if (!replacing)
        {
            if (_freeRenderSlotCount == 0)
            {
                _renderCapacityRejections++;
                return;
            }
            renderSlot = _freeRenderSlots[--_freeRenderSlotCount];
        }

        long buildStart = Stopwatch.GetTimestamp();
        RenderChunk candidate = CreateRenderChunk(
            handle, semantic, _desiredWindowOffsets[semanticIndex]);
        _lastMeshBuildTicks = Stopwatch.GetTimestamp() - buildStart;
        _maximumMeshBuildTicks = Math.Max(_maximumMeshBuildTicks, _lastMeshBuildTicks);

        long commitStart = Stopwatch.GetTimestamp();
        if (replacing) DisposeChunk(renderSlot, releaseSlot: false);
        _renderChunks[renderSlot] = candidate;
        _associations[semanticIndex] = new RenderAssociation(handle.Generation, renderSlot + 1);
        ApplyOrigin(candidate);
        candidate.Root.Visible = _desiredVisible[semanticIndex];
        Root.AddChild(candidate.Root);
        SynchronizeDirectGroups(candidate);
        _activeChunks++;
        if (candidate.Root.Visible) _visibleChunks++;
        _activeNodes += candidate.NodeCount;
        _activePublicNodes += candidate.PublicNodeCount;
        _activeResources += candidate.ResourceCount;
        _activeTerrainSurfaceMeshes += candidate.TerrainSurfaceMeshCount;
        _activeStaticBatchGroups += candidate.StaticBatchGroupCount;
        _activePublicBatchGroups += candidate.PublicResourceCount;
        _activeFallbackGroups += candidate.FallbackGroupCount;
        _activeInstances += candidate.InstanceCount;
        AdjustPresentedPrefabCounts(candidate.PublicPrefabCounts, 1);
        _completedBuilds++;
        if (replacing) _rebuilds++;
        _lastResourceCommitTicks = Stopwatch.GetTimestamp() - commitStart;
        _maximumResourceCommitTicks = Math.Max(_maximumResourceCommitTicks, _lastResourceCommitTicks);
    }

    /// <summary>Refreshes native resources only; residency, handles, visibility and origin are retained.</summary>
    public void RefreshAssets(IGodotWorldAssetResolver? assets)
    {
        EnsureOwner();
        ThrowIfDisposed();
        _assets = assets;
        for (int index = 0; index < _renderChunks.Length; index++)
            if (_renderChunks[index] is { } chunk) Build(chunk.Handle);
    }

    private void RefreshPublic(ClientSemanticChunkHandle handle)
    {
        if (!TryFindRenderSlot(handle, out int renderSlot) ||
            !_cache.TryGetResidentView(handle, out ClientSemanticChunkView semantic) || !GroupsFit(semantic))
            return;
        RenderChunk chunk = _renderChunks[renderSlot]!;
        long buildStart = Stopwatch.GetTimestamp();
        RenderLayer candidate = CreatePublicLayer(semantic, chunk.YAnchorMillimeters);
        _lastMeshBuildTicks = Stopwatch.GetTimestamp() - buildStart;
        _maximumMeshBuildTicks = Math.Max(_maximumMeshBuildTicks, _lastMeshBuildTicks);

        long commitStart = Stopwatch.GetTimestamp();
        AdjustPresentedPrefabCounts(chunk.PublicPrefabCounts, -1);
        foreach (GodotWorldInstanceGroup group in chunk.PublicGroups) group.Dispose();
        if (GodotObject.IsInstanceValid(chunk.PublicRoot)) chunk.PublicRoot.Free();
        _activeInstances -= chunk.PublicInstanceCount;
        _activeNodes -= chunk.PublicNodeCount;
        _activePublicNodes -= chunk.PublicNodeCount;
        _activeResources -= chunk.PublicResourceCount;
        _activePublicBatchGroups -= chunk.PublicResourceCount;
        _activeFallbackGroups -= FallbackGroupCount(chunk.PublicFallbacks);
        chunk.PublicRoot = candidate.Root;
        chunk.PublicNodeCount = candidate.NodeCount;
        chunk.PublicResourceCount = candidate.ResourceCount;
        chunk.PublicFallbacks = candidate.Fallbacks;
        chunk.PublicGroups = candidate.Groups;
        chunk.PublicPrefabCounts = candidate.PrefabCounts;
        chunk.FirstPublicLocalY = candidate.FirstLocalY;
        chunk.SemanticHash = semantic.Info.SemanticHash;
        chunk.Root.AddChild(candidate.Root);
        SynchronizeDirectGroups(chunk);
        _activeInstances += chunk.PublicInstanceCount;
        AdjustPresentedPrefabCounts(chunk.PublicPrefabCounts, 1);
        _activeNodes += candidate.NodeCount;
        _activePublicNodes += candidate.NodeCount;
        _activeResources += candidate.ResourceCount;
        _activePublicBatchGroups += candidate.ResourceCount;
        _activeFallbackGroups += FallbackGroupCount(candidate.Fallbacks);
        _publicRefreshes++;
        _lastResourceCommitTicks = Stopwatch.GetTimestamp() - commitStart;
        _maximumResourceCommitTicks = Math.Max(_maximumResourceCommitTicks, _lastResourceCommitTicks);
    }

    private RenderChunk CreateRenderChunk(ClientSemanticChunkHandle handle,
        ClientSemanticChunkView semantic, ClientPresentationOffset windowOffset)
    {
        var root = new Node3D { Name = $"Chunk_{semantic.Info.Key.Coord.X}_{semantic.Info.Key.Coord.Z}" };
        var structural = new Node3D { Name = "Structural" };
        var nativeGroups = new List<GodotWorldInstanceGroup>();
        var terrainResources = new GodotOwnedResources();
        RenderLayer? publicLayer = null;
        GodotVegetationChunk? vegetation = null;
        root.AddChild(structural);
        try
        {
            ArrayMesh terrainMesh = CreateTerrainMesh(
                handle, semantic.Info.Key, out int yAnchor, out float firstTerrainY,
                out int terrainTriangleCount);
            terrainResources.Own(terrainMesh);
            structural.AddChild(new MeshInstance3D { Name = "BaseTerrain", Mesh = terrainMesh });
            if (_vegetation is not null)
            {
                long started = Stopwatch.GetTimestamp();
                vegetation = _vegetation.Prepare(_cache, handle, semantic, yAnchor);
                _vegetationPreparationTicks += Stopwatch.GetTimestamp() - started;
                if (vegetation is not null)
                {
                    vegetation.Node.CastShadow = _vegetationShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
                    structural.AddChild(vegetation.Node);
                }
            }
            int additionalTerrainTriangles = 0;
            float firstAdditionalTerrainY = float.NaN;
            for (int fragment = 0; fragment < semantic.AdditionalTerrainSurfaces.Count; fragment++)
            {
                ArrayMesh additionalMesh = CreateAdditionalTerrainMesh(
                    handle, fragment, yAnchor, out int fragmentTriangles, out float fragmentFirstY,
                    out WorldSurfaceId surfaceId);
                terrainResources.Own(additionalMesh);
                structural.AddChild(new MeshInstance3D
                {
                    Name = $"AdditionalTerrain_{surfaceId.Value}_{fragment}",
                    Mesh = additionalMesh,
                });
                additionalTerrainTriangles += fragmentTriangles;
                if (float.IsNaN(firstAdditionalTerrainY)) firstAdditionalTerrainY = fragmentFirstY;
            }

            long groupingStart = Stopwatch.GetTimestamp();
            var staticGroups = new SortedDictionary<WorldStaticRenderKey, RenderTransforms>();
            float firstStaticY = float.NaN;
            FallbackCounts structuralFallbacks = default;
            foreach (WorldFrozenStaticRecord record in semantic.FrozenStatics)
                AddStatic(staticGroups, semantic.Info.Key, record.RenderKey, record.Position,
                    record.Orientation, record.Flags, record.Bounds, false,
                    yAnchor, _assets is null, ref firstStaticY);
            foreach (WorldMultiFragmentRecord record in semantic.MultiFragments)
                AddStatic(staticGroups, semantic.Info.Key, record.RenderKey, record.Position,
                    record.Orientation, record.Flags, record.Bounds, true,
                    yAnchor, _assets is null, ref firstStaticY);
            _groupingTicks += Stopwatch.GetTimestamp() - groupingStart;
            foreach ((WorldStaticRenderKey key, RenderTransforms transforms) in staticGroups)
            {
                ResolvedMesh presentation = ResolveMesh(
                    _assets?.ResolveStaticMesh(key) ??
                        GodotWorldMeshResolution.MissingModelAndTexture(),
                    _fallbackStaticMesh);
                structuralFallbacks += FallbackCounts.From(presentation.Availability);
                nativeGroups.Add(CreateInstanceGroup(structural,
                    $"Static_{key.DefinitionId.Value}_{key.Variant}_{(ushort)key.Flags}" +
                        (_assets is null ? string.Empty :
                            FallbackNameSuffix(presentation.Availability)),
                    presentation.Mesh,
                    presentation.Availability is GodotWorldAssetAvailability.ModelMissing or
                        GodotWorldAssetAvailability.ModelAndTextureMissing
                        ? transforms.Fallback : transforms.Resolved,
                    presentation.MaterialOverride));
            }

            publicLayer = CreatePublicLayer(semantic, yAnchor);
            RenderLayer preparedPublic = publicLayer.Value;
            root.AddChild(preparedPublic.Root);
            return new RenderChunk(handle, semantic.Info.Key, semantic.Info.SemanticHash,
                windowOffset, root,
                structural, preparedPublic.Root, terrainMesh, yAnchor, firstTerrainY,
                terrainTriangleCount, firstStaticY,
                preparedPublic.FirstLocalY,
                3 + semantic.AdditionalTerrainSurfaces.Count +
                    (_instanceBackend == GodotWorldInstanceBackend.NodeMultiMesh ? staticGroups.Count : 0) + (vegetation is null ? 0 : 1),
                1 + semantic.AdditionalTerrainSurfaces.Count + staticGroups.Count + (vegetation is null ? 0 : 1),
                semantic.AdditionalTerrainSurfaces.Count, additionalTerrainTriangles,
                firstAdditionalTerrainY,
                structuralFallbacks,
                preparedPublic.NodeCount, preparedPublic.ResourceCount, preparedPublic.Fallbacks,
                nativeGroups.ToArray(), preparedPublic.Groups, preparedPublic.PrefabCounts,
                vegetation, terrainResources);
        }
        catch
        {
            vegetation?.Dispose();
            foreach (GodotWorldInstanceGroup group in nativeGroups) group.Dispose();
            if (publicLayer is { } preparedPublic)
                foreach (GodotWorldInstanceGroup group in preparedPublic.Groups) group.Dispose();
            try { root.Free(); }
            finally { terrainResources.Dispose(); }
            throw;
        }
    }

    private RenderLayer CreatePublicLayer(ClientSemanticChunkView semantic, int yAnchor)
    {
        var root = new Node3D { Name = "Public" };
        var nativeGroups = new List<GodotWorldInstanceGroup>();
        try
        {
            long groupingStart = Stopwatch.GetTimestamp();
            var groups = new SortedDictionary<uint, RenderTransforms>();
            Node3D? names = null;
            int nameCount = 0;
            float firstY = float.NaN;
            FallbackCounts fallbacks = default;
            foreach (WorldPublicRecord record in semantic.PublicRecords)
            {
                if ((record.StateFlags & WorldPublicStateFlags.Visible) == 0 ||
                    _publicBatchFilter?.ShouldBatch(in record) == false) continue;
                if (!groups.TryGetValue(record.PrefabId, out RenderTransforms? transforms))
                    groups.Add(record.PrefabId, transforms = new RenderTransforms());
                Transform3D transform = ToLocalTransform(
                    semantic.Info.Key, record.Position, record.Orientation, yAnchor);
                // Only the missing model uses declared fallback bounds. A valid model keeps
                // its own authored geometry; the same stock bounds also drive pointer nomination.
                // The primitive mesh is unit-sized and shared across every public group.
                ClientPrimitivePresentation primitive =
                    ClientPrimitivePresentationCatalog.ForPublic(record.PrefabId);
                Transform3D fallbackTransform = new(
                    transform.Basis.Scaled(new Vector3(
                        primitive.WidthMillimeters, primitive.HeightMillimeters,
                        primitive.DepthMillimeters) * MillimetersToMeters), transform.Origin);
                transforms.Resolved.Add(transform);
                transforms.Fallback.Add(fallbackTransform);
                string? publicName = ResolveGenericPublicName(in record);
                if (publicName is not null)
                {
                    names ??= new Node3D
                    {
                        Name = "PublicNames",
                        Visible = _publicNamesRevealHeld,
                    };
                    names.AddChild(new Label3D
                    {
                        Name = $"PublicName_{record.Id.Index}_{record.Id.Generation}",
                        Text = publicName,
                        Position = transform.Origin + new Vector3(0, 1.05f, 0),
                        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                        NoDepthTest = true,
                        FontSize = 28,
                    });
                    nameCount++;
                }
                if (float.IsNaN(firstY)) firstY = transform.Origin.Y;
            }
            _groupingTicks += Stopwatch.GetTimestamp() - groupingStart;
            foreach ((uint prefab, RenderTransforms transforms) in groups)
            {
                GodotWorldMeshResolution resolution = _assets?.ResolvePublicMesh(prefab) ??
                    GodotWorldMeshResolution.MissingModelAndTexture();
                ResolvedMesh presentation = ResolveMesh(resolution, _fallbackPublicMesh);
                fallbacks += FallbackCounts.From(presentation.Availability);
                nativeGroups.Add(CreateInstanceGroup(root,
                    $"Public_{prefab}" + (_assets is null ? string.Empty :
                        FallbackNameSuffix(presentation.Availability)),
                    presentation.Mesh,
                    presentation.Availability is GodotWorldAssetAvailability.ModelMissing or
                        GodotWorldAssetAvailability.ModelAndTextureMissing
                        ? transforms.Fallback : transforms.Resolved,
                    presentation.MaterialOverride));
            }
            var prefabCounts = new PresentedPrefabCount[groups.Count];
            int prefabIndex = 0;
            foreach (uint prefab in groups.Keys)
                prefabCounts[prefabIndex] = new PresentedPrefabCount(
                    prefab, nativeGroups[prefabIndex++].Count);
            if (names is not null) root.AddChild(names);
            return new RenderLayer(
                root,
                1 + (_instanceBackend == GodotWorldInstanceBackend.NodeMultiMesh ? groups.Count : 0) +
                    (names is null ? 0 : 1 + nameCount),
                groups.Count,
                firstY,
                fallbacks, nativeGroups.ToArray(), prefabCounts);
        }
        catch
        {
            foreach (GodotWorldInstanceGroup group in nativeGroups) group.Dispose();
            root.Free();
            throw;
        }
    }

    private static string? ResolveGenericPublicName(in WorldPublicRecord record) =>
        (record.StateFlags & WorldPublicStateFlags.GroundItem) != 0 &&
        record.PrefabId == UltraSim.Game.Composition.GameWorldPublicPrefabIds.IronSword
            ? "Iron Sword"
            : null;

    private ArrayMesh CreateTerrainMesh(ClientSemanticChunkHandle handle, SpatialChunkKey key,
        out int yAnchor, out float firstLocalY, out int triangleCount)
    {
        if (!_cache.TryBuildResidentTerrainMesh(handle, _terrainVertices, _terrainIndices,
                _terrainMaterials, out TerrainBaseMeshSummary summary,
                out ClientSemanticChunkError semanticError, out TerrainBaseMeshBuildError meshError))
            throw new InvalidOperationException(
                $"Could not prepare resident terrain mesh for {key}: {semanticError}/{meshError}.");

        yAnchor = summary.MinimumHeightMillimeters;
        triangleCount = summary.TriangleCount;
        var vertices = new Vector3[TerrainChunkFormat.HeightVertexCount];
        var normals = new Vector3[TerrainChunkFormat.HeightVertexCount];
        var uv = new Vector2[TerrainChunkFormat.HeightVertexCount];
        for (int index = 0; index < vertices.Length; index++)
        {
            TerrainBaseMeshVertex source = _terrainVertices[index];
            vertices[index] = new Vector3(
                source.LocalXMillimeters * MillimetersToMeters,
                checked((long)source.AbsoluteYMillimeters - yAnchor) * MillimetersToMeters,
                source.LocalZMillimeters * MillimetersToMeters);
            normals[index] = new Vector3(source.NormalX, source.NormalY, source.NormalZ).Normalized();
            uv[index] = new Vector2(
                ((long)key.Coord.X * GameWorldSpatialProfile.WorldChunkEdgeUnits +
                    source.LocalXMillimeters) / 3000f,
                ((long)key.Coord.Z * GameWorldSpatialProfile.WorldChunkEdgeUnits +
                    source.LocalZMillimeters) / 3000f);
        }
        firstLocalY = vertices[0].Y;

        var trianglesByMaterial = new SortedDictionary<ushort, List<int>>();
        for (int triangle = 0; triangle < summary.TriangleCount; triangle++)
        {
            ushort material = _terrainMaterials[triangle];
            if (!trianglesByMaterial.TryGetValue(material, out List<int>? indices))
                trianglesByMaterial.Add(material, indices = []);
            int source = triangle * 3;
            GodotMeshGeometry.ConvertCanonicalTriangle(
                _terrainIndices[source],
                _terrainIndices[source + 1],
                _terrainIndices[source + 2],
                out int first,
                out int second,
                out int third);
            indices.Add(first);
            indices.Add(second);
            indices.Add(third);
        }

        var mesh = new ArrayMesh();
        try
        {
            foreach ((ushort material, List<int> indices) in trianglesByMaterial)
            {
                using var arrays = new global::Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = vertices;
                arrays[(int)Mesh.ArrayType.Normal] = normals;
                arrays[(int)Mesh.ArrayType.TexUV] = uv;
                arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                Material? resolved = _assets?.ResolveTerrainMaterial(material);
                if (resolved is not null) mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, resolved);
            }
            return mesh;
        }
        catch
        {
            mesh.Dispose();
            throw;
        }
    }

    private ArrayMesh CreateAdditionalTerrainMesh(
        ClientSemanticChunkHandle handle,
        int fragmentIndex,
        int yAnchor,
        out int triangleCount,
        out float firstLocalY,
        out WorldSurfaceId surfaceId)
    {
        if (!_cache.TryBuildResidentAdditionalTerrainSurfaceMesh(
                handle, fragmentIndex, _additionalTerrainVertices, _additionalTerrainIndices,
                _additionalTerrainMaterials, out TerrainSurfaceBandMeshSummary summary,
                out ClientSemanticChunkError semanticError, out TerrainSurfaceBandMeshBuildError meshError))
            throw new InvalidOperationException(
                $"Could not prepare additional terrain fragment {fragmentIndex}: {semanticError}/{meshError}.");
        surfaceId = summary.SurfaceId;
        triangleCount = summary.TriangleCount;
        var vertices = new Vector3[summary.VertexCount];
        var normals = new Vector3[summary.VertexCount];
        var uv = new Vector2[summary.VertexCount];
        for (int index = 0; index < summary.VertexCount; index++)
        {
            TerrainBaseMeshVertex source = _additionalTerrainVertices[index];
            vertices[index] = new Vector3(
                source.LocalXMillimeters * MillimetersToMeters,
                checked((long)source.AbsoluteYMillimeters - yAnchor) * MillimetersToMeters,
                source.LocalZMillimeters * MillimetersToMeters);
            normals[index] = new Vector3(source.NormalX, source.NormalY, source.NormalZ).Normalized();
            uv[index] = new Vector2(source.LocalXMillimeters / 3000f,
                source.LocalZMillimeters / 3000f);
        }
        firstLocalY = vertices.Length == 0 ? float.NaN : vertices[0].Y;
        var trianglesByMaterial = new SortedDictionary<ushort, List<int>>();
        for (int triangle = 0; triangle < summary.TriangleCount; triangle++)
        {
            ushort material = _additionalTerrainMaterials[triangle];
            if (!trianglesByMaterial.TryGetValue(material, out List<int>? indices))
                trianglesByMaterial.Add(material, indices = []);
            int source = triangle * 3;
            GodotMeshGeometry.ConvertCanonicalTriangle(
                _additionalTerrainIndices[source], _additionalTerrainIndices[source + 1],
                _additionalTerrainIndices[source + 2], out int first, out int second, out int third);
            indices.Add(first); indices.Add(second); indices.Add(third);
        }
        var mesh = new ArrayMesh();
        try
        {
            foreach ((ushort material, List<int> indices) in trianglesByMaterial)
            {
                using var arrays = new global::Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = vertices;
                arrays[(int)Mesh.ArrayType.Normal] = normals;
                arrays[(int)Mesh.ArrayType.TexUV] = uv;
                arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                Material? resolved = _assets?.ResolveTerrainMaterial(material);
                if (resolved is not null) mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, resolved);
            }
            return mesh;
        }
        catch
        {
            mesh.Dispose();
            throw;
        }
    }

    private GodotWorldInstanceGroup CreateInstanceGroup(Node3D parent,
        string name,
        Mesh mesh,
        List<Transform3D> transforms,
        Material? materialOverride = null)
    {
        long started = Stopwatch.GetTimestamp();
        var group = new GodotWorldInstanceGroup(parent, name, mesh, transforms,
            materialOverride, _instanceBackend, _instanceUpload, _uploadScratch);
        _groupPreparationTicks += Stopwatch.GetTimestamp() - started;
        _uploadedInstanceRows += (ulong)transforms.Count;
        if (group.BulkUploaded)
        {
            _bulkUploads++;
            _bulkUploadBytes += checked((ulong)transforms.Count * 12 * sizeof(float));
        }
        else _targetedInstanceUpdates += (ulong)transforms.Count;
        return group;
    }

    private ResolvedMesh ResolveMesh(
        in GodotWorldMeshResolution resolution,
        Mesh fallbackMesh)
    {
        if (!resolution.IsValid)
            throw new InvalidOperationException(
                $"The world asset resolver returned an invalid {resolution.Availability} result.");
        bool modelMissing = resolution.Availability is GodotWorldAssetAvailability.ModelMissing or
            GodotWorldAssetAvailability.ModelAndTextureMissing;
        bool textureMissing = resolution.Availability is GodotWorldAssetAvailability.TextureMissing or
            GodotWorldAssetAvailability.ModelAndTextureMissing;
        Material? materialOverride = textureMissing
            ? _fallbackMaterialMode == GodotWorldFallbackMaterialMode.Diagnostic
                ? _diagnosticFallbackMaterial
                : _neutralFallbackMaterial
            : null;
        return new ResolvedMesh(
            modelMissing ? fallbackMesh : resolution.Mesh!,
            materialOverride,
            resolution.Availability);
    }

    private static string FallbackNameSuffix(GodotWorldAssetAvailability availability) =>
        availability == GodotWorldAssetAvailability.Available
            ? string.Empty
            : $"_Fallback_{availability}";

    private static void AddStatic(SortedDictionary<WorldStaticRenderKey, RenderTransforms> groups,
        SpatialChunkKey key, WorldStaticRenderKey renderKey, WorldPosition position,
        WorldYaw orientation, WorldStaticFlags flags, WorldStaticBounds bounds,
        bool multiFragment, int yAnchor, bool primitiveFallback, ref float firstY)
    {
        bool benchmarkWindow = renderKey.DefinitionId.Value == GameWorldStaticDefinitionIds.CabinWall;
        bool windowSkin = benchmarkWindow &&
            renderKey.Variant == GameWorldStaticVariants.CabinWindowSixMeters;
        bool windowStructure = benchmarkWindow && renderKey.Variant is
            GameWorldStaticVariants.CabinWindowJamb or
            GameWorldStaticVariants.CabinWindowSill or
            GameWorldStaticVariants.CabinWindowLintel;
        // Exact structural bounds provide Primitive's aperture when no catalog resolver is
        // present. Installed art uses the single painted skin and keeps collider pieces hidden.
        if (primitiveFallback && windowSkin ||
            (flags & WorldStaticFlags.Renderable) == 0 && !(primitiveFallback && windowStructure))
            return;
        if (!groups.TryGetValue(renderKey, out RenderTransforms? transforms))
            groups.Add(renderKey, transforms = new RenderTransforms());
        Transform3D transform = ToLocalTransform(key, position, orientation, yAnchor);
        if (!ClientPrimitivePresentation.TryFromStructuralBounds(
                bounds, position, multiFragment, out ClientPrimitivePresentation primitive))
            primitive = ClientPrimitivePresentation.Grounded(
                ClientPrimitiveKind.InteractiveMarker, ClientPrimitiveShape.Marker,
                400, 900, 400);
        // The source AABB already includes orientation. Scale one shared unit box and place it
        // at that exact structural volume, independently for every static or Multi fragment.
        Vector3 size = new(primitive.WidthMillimeters, primitive.HeightMillimeters,
            primitive.DepthMillimeters);
        Vector3 centre = new(
            (primitive.MinimumXMillimeters + primitive.MaximumXMillimeters) * 0.5f,
            primitive.MinimumYMillimeters,
            (primitive.MinimumZMillimeters + primitive.MaximumZMillimeters) * 0.5f);
        Vector3 positionFromBounds = new(
            checked((float)(position.GlobalXMillimeters -
                (long)key.Coord.X * GameWorldSpatialProfile.WorldChunkEdgeUnits)),
            checked((float)((long)position.YMillimeters - yAnchor)),
            checked((float)(position.GlobalZMillimeters -
                (long)key.Coord.Z * GameWorldSpatialProfile.WorldChunkEdgeUnits)));
        Transform3D fallback = new(
            Basis.Identity.Scaled(size * MillimetersToMeters),
            (positionFromBounds + centre) * MillimetersToMeters);
        transforms.Resolved.Add(transform);
        transforms.Fallback.Add(fallback);
        if (float.IsNaN(firstY)) firstY = transform.Origin.Y;
    }

    private static Transform3D ToLocalTransform(SpatialChunkKey key, in WorldPosition position,
        WorldYaw orientation, int yAnchor)
    {
        float radians = orientation.Units * (MathF.Tau / WorldYaw.UnitsPerTurn);
        var basis = new Basis(Vector3.Up, radians);
        long chunkOriginX = checked((long)key.Coord.X * GameWorldSpatialProfile.WorldChunkEdgeUnits);
        long chunkOriginZ = checked((long)key.Coord.Z * GameWorldSpatialProfile.WorldChunkEdgeUnits);
        var origin = new Vector3(
            checked(position.GlobalXMillimeters - chunkOriginX) * MillimetersToMeters,
            checked((long)position.YMillimeters - yAnchor) * MillimetersToMeters,
            checked(position.GlobalZMillimeters - chunkOriginZ) * MillimetersToMeters);
        return new Transform3D(basis, origin);
    }

    private bool GroupsFit(ClientSemanticChunkView semantic)
    {
        _groupValidationCalls++;
        int groups = 0;
        for (int index = 0; index < semantic.TerrainMaterials.Length; index++)
        {
            if (!TerrainSurfacePresence.IsPresent(semantic.TerrainSurfacePresence, index)) continue;
            ushort material = semantic.TerrainMaterials[index];
            bool prior = false;
            for (int scan = 0; scan < index; scan++)
            {
                if (TerrainSurfacePresence.IsPresent(semantic.TerrainSurfacePresence, scan) &&
                    semantic.TerrainMaterials[scan] == material)
                {
                    prior = true;
                    break;
                }
            }
            if (!prior) groups++;
        }
        for (int bandIndex = 0; bandIndex < semantic.AdditionalTerrainSurfaces.Count; bandIndex++)
        {
            WorldTerrainSurfaceBandView band = semantic.AdditionalTerrainSurfaces.GetBand(bandIndex);
            for (int z = 0; z < band.Bounds.CellDepth; z++)
            for (int x = 0; x < band.Bounds.CellWidth; x++)
            {
                if (!band.IsCellPresent(x, z)) continue;
                if (band.HasTop && IsFirstBandMaterial(
                        band, WorldTerrainSurfaceFace.Top, x, z)) groups++;
                if (band.HasBottom && IsFirstBandMaterial(
                        band, WorldTerrainSurfaceFace.Bottom, x, z)) groups++;
            }
            if (band.HasTop && band.HasBottom &&
                !BandContainsMaterialBeforeBoundary(band, band.SideMaterialId)) groups++;
            if (groups > _limits.MaximumGroupsPerChunk) return false;
        }
        for (int index = 0; index < semantic.FrozenStatics.Length; index++)
        {
            WorldFrozenStaticRecord record = semantic.FrozenStatics[index];
            if ((record.Flags & WorldStaticFlags.Renderable) == 0) continue;
            bool prior = false;
            for (int scan = 0; scan < index; scan++)
                if ((semantic.FrozenStatics[scan].Flags & WorldStaticFlags.Renderable) != 0 &&
                    semantic.FrozenStatics[scan].RenderKey == record.RenderKey) prior = true;
            if (!prior) groups++;
        }
        for (int index = 0; index < semantic.MultiFragments.Length; index++)
        {
            WorldMultiFragmentRecord record = semantic.MultiFragments[index];
            if ((record.Flags & WorldStaticFlags.Renderable) == 0) continue;
            bool prior = false;
            foreach (WorldFrozenStaticRecord frozen in semantic.FrozenStatics)
                if ((frozen.Flags & WorldStaticFlags.Renderable) != 0 &&
                    frozen.RenderKey == record.RenderKey) prior = true;
            for (int scan = 0; scan < index; scan++)
                if ((semantic.MultiFragments[scan].Flags & WorldStaticFlags.Renderable) != 0 &&
                    semantic.MultiFragments[scan].RenderKey == record.RenderKey) prior = true;
            if (!prior) groups++;
        }
        for (int index = 0; index < semantic.PublicRecords.Length; index++)
        {
            WorldPublicRecord record = semantic.PublicRecords[index];
            if ((record.StateFlags & WorldPublicStateFlags.Visible) == 0 ||
                _publicBatchFilter?.ShouldBatch(in record) == false) continue;
            bool prior = false;
            for (int scan = 0; scan < index; scan++)
                if ((semantic.PublicRecords[scan].StateFlags & WorldPublicStateFlags.Visible) != 0 &&
                    _publicBatchFilter?.ShouldBatch(
                        in semantic.PublicRecords[scan]) != false &&
                    semantic.PublicRecords[scan].PrefabId == record.PrefabId) prior = true;
            if (!prior) groups++;
        }
        return groups <= _limits.MaximumGroupsPerChunk;
    }

    private static bool IsFirstBandMaterial(
        in WorldTerrainSurfaceBandView band,
        WorldTerrainSurfaceFace face,
        int x,
        int z)
    {
        ushort candidate = band.GetMaterial(face, x, z);
        int target = z * band.Bounds.CellWidth + x;
        if (face == WorldTerrainSurfaceFace.Bottom && band.HasTop)
        {
            for (int scanZ = 0; scanZ < band.Bounds.CellDepth; scanZ++)
            for (int scanX = 0; scanX < band.Bounds.CellWidth; scanX++)
                if (band.IsCellPresent(scanX, scanZ) &&
                    band.GetMaterial(WorldTerrainSurfaceFace.Top, scanX, scanZ) == candidate)
                    return false;
        }
        for (int scan = 0; scan < target; scan++)
        {
            int scanX = scan % band.Bounds.CellWidth;
            int scanZ = scan / band.Bounds.CellWidth;
            if (band.IsCellPresent(scanX, scanZ) &&
                band.GetMaterial(face, scanX, scanZ) == candidate) return false;
        }
        return true;
    }

    private static bool BandContainsMaterialBeforeBoundary(
        in WorldTerrainSurfaceBandView band,
        ushort candidate)
    {
        for (int z = 0; z < band.Bounds.CellDepth; z++)
        for (int x = 0; x < band.Bounds.CellWidth; x++)
        {
            if (!band.IsCellPresent(x, z)) continue;
            if (band.HasTop && band.GetMaterial(WorldTerrainSurfaceFace.Top, x, z) == candidate ||
                band.HasBottom && band.GetMaterial(WorldTerrainSurfaceFace.Bottom, x, z) == candidate)
                return true;
        }
        return false;
    }

    private void ApplyOrigin(RenderChunk chunk)
    {
        chunk.Root.Position = new Vector3(
            checked((long)chunk.WindowOffset.X * GameWorldSpatialProfile.WorldChunkEdgeUnits) *
                MillimetersToMeters,
            checked((long)chunk.YAnchorMillimeters - _verticalAnchorMillimeters) *
                MillimetersToMeters,
            checked((long)chunk.WindowOffset.Z * GameWorldSpatialProfile.WorldChunkEdgeUnits) *
                MillimetersToMeters);
        _chunkRootTransformWrites++;
        SynchronizeDirectGroups(chunk);
    }

    private void SynchronizeDirectGroups()
    {
        if (_instanceBackend != GodotWorldInstanceBackend.DirectRid) return;
        foreach (RenderChunk? chunk in _renderChunks)
            if (chunk is not null) SynchronizeDirectGroups(chunk);
    }

    private void SynchronizeDirectGroups(RenderChunk chunk)
    {
        if (_instanceBackend != GodotWorldInstanceBackend.DirectRid || !chunk.Root.IsInsideTree()) return;
        Rid scenario = chunk.Root.GetWorld3D().Scenario;
        Transform3D transform = chunk.Root.GlobalTransform;
        bool visible = chunk.Root.IsVisibleInTree();
        foreach (GodotWorldInstanceGroup group in chunk.StructuralGroups)
            group.Synchronize(scenario, transform, visible);
        foreach (GodotWorldInstanceGroup group in chunk.PublicGroups)
            group.Synchronize(scenario, transform, visible);
    }

    private bool TryProcessNextBuild()
    {
        if (_buildCount == 0) return false;
        RenderWorkItem work = _buildQueue[_buildHead];
        bool needsFreeSlot = work.Kind == RenderWorkKind.FullBuild &&
            !TryFindAnyAssociation(checked((int)work.Handle.Index), out _);
        if (needsFreeSlot && _freeRenderSlotCount == 0) return false;
        _ = Dequeue(_buildQueue, ref _buildHead, ref _buildCount);
        if (work.Kind == RenderWorkKind.FullBuild) Build(work.Handle);
        else RefreshPublic(work.Handle);
        return true;
    }

    private void SetWindowOffset(
        RenderChunk chunk,
        ClientPresentationOffset offset,
        bool countWindowMove = true)
    {
        if (chunk.WindowOffset == offset) return;
        chunk.WindowOffset = offset;
        ApplyOrigin(chunk);
        if (countWindowMove) _windowMoves++;
    }

    private void Retire(ClientSemanticChunkHandle handle)
    {
        if (!TryFindRenderSlot(handle, out int renderSlot)) return;
        DisposeChunk(renderSlot);
        _completedRetirements++;
    }

    private void DisposeChunk(int renderSlot, bool releaseSlot = true)
    {
        RenderChunk? chunk = _renderChunks[renderSlot];
        if (chunk is null) return;
        if (chunk.Root.Visible) _visibleChunks--;
        _activeChunks--;
        _activeNodes -= chunk.NodeCount;
        _activePublicNodes -= chunk.PublicNodeCount;
        _activeResources -= chunk.ResourceCount;
        _activeTerrainSurfaceMeshes -= chunk.TerrainSurfaceMeshCount;
        _activeStaticBatchGroups -= chunk.StaticBatchGroupCount;
        _activePublicBatchGroups -= chunk.PublicResourceCount;
        _activeFallbackGroups -= chunk.FallbackGroupCount;
        _activeInstances -= chunk.InstanceCount;
        AdjustPresentedPrefabCounts(chunk.PublicPrefabCounts, -1);
        foreach (GodotWorldInstanceGroup group in chunk.StructuralGroups) group.Dispose();
        foreach (GodotWorldInstanceGroup group in chunk.PublicGroups) group.Dispose();
        chunk.Vegetation?.Dispose();
        if (GodotObject.IsInstanceValid(chunk.Root)) chunk.Root.Free();
        chunk.TerrainResources.Dispose();
        _renderChunks[renderSlot] = null;
        int semanticIndex = checked((int)chunk.Handle.Index);
        if (_associations[semanticIndex].Generation == chunk.Handle.Generation &&
            _associations[semanticIndex].RenderSlotPlusOne == renderSlot + 1)
            _associations[semanticIndex] = default;
        if (releaseSlot) _freeRenderSlots[_freeRenderSlotCount++] = renderSlot;
    }

    private void AdjustPresentedPrefabCounts(
        ReadOnlySpan<PresentedPrefabCount> counts, int direction)
    {
        foreach (PresentedPrefabCount entry in counts)
        {
            int previous = _presentedPublicPrefabs.TryGetValue(entry.PrefabId, out int count)
                ? count : 0;
            int next = checked(previous + direction * entry.Count);
            if (next < 0) throw new InvalidOperationException("Public presentation census underflow.");
            if (next == 0) _presentedPublicPrefabs.Remove(entry.PrefabId);
            else _presentedPublicPrefabs[entry.PrefabId] = next;
        }
    }

    private void SetVisible(RenderChunk chunk, bool visible)
    {
        if (chunk.Root.Visible == visible) return;
        chunk.Root.Visible = visible;
        SynchronizeDirectGroups(chunk);
        _visibleChunks += visible ? 1 : -1;
    }

    private bool TryGetSemanticIndex(ClientSemanticChunkHandle handle, out int index)
    {
        if (!handle.IsValid || handle.Index >= (uint)_associations.Length)
        {
            index = -1;
            return false;
        }
        index = (int)handle.Index;
        return true;
    }

    private bool TryFindRenderSlot(ClientSemanticChunkHandle handle, out int renderSlot)
    {
        if (!TryGetSemanticIndex(handle, out int semanticIndex))
        {
            renderSlot = -1;
            return false;
        }
        RenderAssociation association = _associations[semanticIndex];
        if (association.Generation != handle.Generation || association.RenderSlotPlusOne == 0)
        {
            renderSlot = -1;
            return false;
        }
        renderSlot = association.RenderSlotPlusOne - 1;
        return _renderChunks[renderSlot]?.Handle == handle;
    }

    private bool TryFindAnyAssociation(int semanticIndex, out int renderSlot)
    {
        int value = _associations[semanticIndex].RenderSlotPlusOne;
        renderSlot = value - 1;
        return value != 0 && _renderChunks[renderSlot] is not null;
    }

    private int CountPendingNewBuilds()
    {
        int count = 0;
        for (int offset = 0; offset < _buildCount; offset++)
        {
            RenderWorkItem work = _buildQueue[(_buildHead + offset) % _buildQueue.Length];
            if (work.Kind != RenderWorkKind.FullBuild) continue;
            if (!TryFindAnyAssociation(checked((int)work.Handle.Index), out _)) count++;
        }
        return count;
    }

    private static bool Contains(RenderWorkItem[] queue, int head, int count,
        ClientSemanticChunkHandle handle)
    {
        for (int offset = 0; offset < count; offset++)
            if (queue[(head + offset) % queue.Length].Handle == handle) return true;
        return false;
    }

    private static bool Contains(ClientSemanticChunkHandle[] queue, int head, int count,
        ClientSemanticChunkHandle handle)
    {
        for (int offset = 0; offset < count; offset++)
            if (queue[(head + offset) % queue.Length] == handle) return true;
        return false;
    }

    private static void Enqueue<T>(T[] queue, ref int head, ref int count, T value)
    {
        queue[(head + count) % queue.Length] = value;
        count++;
    }

    private static T Dequeue<T>(T[] queue, ref int head, ref int count)
    {
        T value = queue[head];
        queue[head] = default!;
        head = (head + 1) % queue.Length;
        count--;
        return value;
    }

    private static bool RemoveQueued(RenderWorkItem[] queue, ref int head, ref int count,
        ClientSemanticChunkHandle handle)
    {
        int found = -1;
        for (int offset = 0; offset < count; offset++)
        {
            if (queue[(head + offset) % queue.Length].Handle == handle)
            {
                found = offset;
                break;
            }
        }
        if (found < 0) return false;
        for (int offset = found; offset < count - 1; offset++)
            queue[(head + offset) % queue.Length] = queue[(head + offset + 1) % queue.Length];
        queue[(head + count - 1) % queue.Length] = default;
        count--;
        if (count == 0) head = 0;
        return true;
    }

    private static bool RemoveQueued(ClientSemanticChunkHandle[] queue, ref int head, ref int count,
        ClientSemanticChunkHandle handle)
    {
        int found = -1;
        for (int offset = 0; offset < count; offset++)
        {
            if (queue[(head + offset) % queue.Length] == handle)
            {
                found = offset;
                break;
            }
        }
        if (found < 0) return false;
        for (int offset = found; offset < count - 1; offset++)
            queue[(head + offset) % queue.Length] = queue[(head + offset + 1) % queue.Length];
        queue[(head + count - 1) % queue.Length] = default;
        count--;
        if (count == 0) head = 0;
        return true;
    }

    private static bool TryGetRelativeOffset(
        SpatialChunkKey key,
        SpatialChunkCoord center,
        out ClientPresentationOffset offset)
    {
        long x = checked((long)key.Coord.X - center.X);
        long z = checked((long)key.Coord.Z - center.Z);
        if (x is < int.MinValue or > int.MaxValue || z is < int.MinValue or > int.MaxValue)
        {
            offset = default;
            return false;
        }
        offset = new ClientPresentationOffset((int)x, (int)z);
        return true;
    }

    private void EnsureOwner()
    {
        if (System.Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The Godot world renderer is engine-thread bound.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private readonly record struct RenderAssociation(uint Generation, int RenderSlotPlusOne);
    private readonly record struct RenderWorkItem(ClientSemanticChunkHandle Handle, RenderWorkKind Kind);
    private enum RenderWorkKind : byte { FullBuild, PublicRefresh }
    private sealed class RenderTransforms
    {
        internal readonly List<Transform3D> Resolved = [];
        internal readonly List<Transform3D> Fallback = [];
    }

    private readonly record struct ResolvedMesh(
        Mesh Mesh,
        Material? MaterialOverride,
        GodotWorldAssetAvailability Availability);

    private readonly record struct FallbackCounts(int Model, int Texture, int ModelAndTexture)
    {
        internal static FallbackCounts From(GodotWorldAssetAvailability availability) =>
            availability switch
            {
                GodotWorldAssetAvailability.ModelMissing => new(1, 0, 0),
                GodotWorldAssetAvailability.TextureMissing => new(0, 1, 0),
                GodotWorldAssetAvailability.ModelAndTextureMissing => new(0, 0, 1),
                _ => default,
            };

        public static FallbackCounts operator +(FallbackCounts left, FallbackCounts right) =>
            new(left.Model + right.Model,
                left.Texture + right.Texture,
                left.ModelAndTexture + right.ModelAndTexture);
    }

    private static int FallbackGroupCount(in FallbackCounts counts) =>
        counts.Model + counts.Texture + counts.ModelAndTexture;

    private readonly record struct PresentedPrefabCount(uint PrefabId, int Count);

    private readonly record struct RenderLayer(
        Node3D Root,
        int NodeCount,
        int ResourceCount,
        float FirstLocalY,
        FallbackCounts Fallbacks,
        GodotWorldInstanceGroup[] Groups,
        PresentedPrefabCount[] PrefabCounts);

    private sealed class RenderChunk
    {
        internal RenderChunk(ClientSemanticChunkHandle handle, SpatialChunkKey key,
            ClientSemanticHash semanticHash, ClientPresentationOffset windowOffset,
            Node3D root, Node3D structuralRoot,
            Node3D publicRoot, ArrayMesh terrainMesh, int yAnchorMillimeters,
            float firstTerrainLocalY, int terrainTriangleCount,
            float firstStaticLocalY, float firstPublicLocalY,
            int structuralNodeCount, int structuralResourceCount,
            int additionalTerrainSurfaceFragmentCount, int additionalTerrainTriangleCount,
            float firstAdditionalTerrainLocalY,
            FallbackCounts structuralFallbacks,
            int publicNodeCount, int publicResourceCount,
            FallbackCounts publicFallbacks,
            GodotWorldInstanceGroup[] structuralGroups,
            GodotWorldInstanceGroup[] publicGroups, PresentedPrefabCount[] publicPrefabCounts,
            GodotVegetationChunk? vegetation,
            GodotOwnedResources terrainResources)
        {
            Handle = handle;
            Key = key;
            SemanticHash = semanticHash;
            WindowOffset = windowOffset;
            Root = root;
            StructuralRoot = structuralRoot;
            PublicRoot = publicRoot;
            TerrainMesh = terrainMesh;
            TerrainResources = terrainResources;
            YAnchorMillimeters = yAnchorMillimeters;
            FirstTerrainLocalY = firstTerrainLocalY;
            TerrainTriangleCount = terrainTriangleCount;
            FirstStaticLocalY = firstStaticLocalY;
            FirstPublicLocalY = firstPublicLocalY;
            StructuralNodeCount = structuralNodeCount;
            StructuralResourceCount = structuralResourceCount;
            AdditionalTerrainSurfaceFragmentCount = additionalTerrainSurfaceFragmentCount;
            AdditionalTerrainTriangleCount = additionalTerrainTriangleCount;
            FirstAdditionalTerrainLocalY = firstAdditionalTerrainLocalY;
            StructuralFallbacks = structuralFallbacks;
            PublicNodeCount = publicNodeCount;
            PublicResourceCount = publicResourceCount;
            PublicFallbacks = publicFallbacks;
            StructuralGroups = structuralGroups;
            PublicGroups = publicGroups;
            PublicPrefabCounts = publicPrefabCounts;
            Vegetation = vegetation;
        }

        internal ClientSemanticChunkHandle Handle { get; }
        internal SpatialChunkKey Key { get; }
        internal ClientSemanticHash SemanticHash { get; set; }
        internal ClientPresentationOffset WindowOffset { get; set; }
        internal Node3D Root { get; }
        internal Node3D StructuralRoot { get; }
        internal Node3D PublicRoot { get; set; }
        internal ArrayMesh TerrainMesh { get; }
        internal GodotOwnedResources TerrainResources { get; }
        internal int YAnchorMillimeters { get; }
        internal float FirstTerrainLocalY { get; }
        internal int TerrainTriangleCount { get; }
        internal int AdditionalTerrainSurfaceFragmentCount { get; }
        internal int AdditionalTerrainTriangleCount { get; }
        internal float FirstAdditionalTerrainLocalY { get; }
        internal float FirstStaticLocalY { get; }
        internal float FirstPublicLocalY { get; set; }
        internal FallbackCounts StructuralFallbacks { get; }
        internal FallbackCounts PublicFallbacks { get; set; }
        internal int StructuralNodeCount { get; }
        internal int StructuralResourceCount { get; }
        internal int PublicNodeCount { get; set; }
        internal int PublicResourceCount { get; set; }
        internal int TerrainSurfaceMeshCount => 1 + AdditionalTerrainSurfaceFragmentCount;
        internal int StaticBatchGroupCount =>
            StructuralResourceCount - TerrainSurfaceMeshCount - (Vegetation is null ? 0 : 1);
        internal GodotVegetationChunk? Vegetation { get; }
        internal int FallbackGroupCount =>
            GodotWorldRenderer.FallbackGroupCount(StructuralFallbacks) +
            GodotWorldRenderer.FallbackGroupCount(PublicFallbacks);
        internal int NodeCount => StructuralNodeCount + PublicNodeCount;
        internal int ResourceCount => StructuralResourceCount + PublicResourceCount;
        internal GodotWorldInstanceGroup[] StructuralGroups { get; }
        internal GodotWorldInstanceGroup[] PublicGroups { get; set; }
        internal PresentedPrefabCount[] PublicPrefabCounts { get; set; }
        internal int PublicInstanceCount => PublicGroups.Sum(static group => group.Count);
        internal int InstanceCount => StructuralGroups.Sum(static group => group.Count) + PublicInstanceCount;
    }
}
