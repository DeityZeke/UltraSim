using System.Diagnostics;
using Godot;
using UltraSim.Client.Core;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Client.Godot;

public interface IGodotWorldAssetResolver
{
    Mesh? ResolveStaticMesh(WorldStaticRenderKey key);
    Mesh? ResolvePublicMesh(uint prefabId);
    Material? ResolveTerrainMaterial(ushort materialId);
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
    float FirstAdditionalTerrainLocalY);

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
    private readonly IGodotWorldAssetResolver? _assets;
    private readonly RenderChunk?[] _renderChunks;
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
    private readonly BoxMesh _fallbackStaticMesh;
    private readonly BoxMesh _fallbackPublicMesh;
    private int _freeRenderSlotCount;
    private int _buildHead;
    private int _buildCount;
    private int _retireHead;
    private int _retireCount;
    private WorldRenderOrigin _origin;
    private int _activeChunks;
    private int _visibleChunks;
    private int _activeNodes;
    private int _activeResources;
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
    private bool _verticalAnchorInitialized = true;
    private int _verticalAnchorMillimeters;

    public GodotWorldRenderer(Node3D host, ClientWorldSemanticCache cache,
        GodotWorldRendererLimits limits = default, IGodotWorldAssetResolver? assets = null)
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
        _fallbackStaticMesh = new BoxMesh { Size = Vector3.One };
        _fallbackPublicMesh = new BoxMesh { Size = new Vector3(0.6f, 1.8f, 0.6f) };
        Root = new Node3D { Name = "UltraSimWorld" };
        host.AddChild(Root);
        _activeNodes = 1;
    }

    public Node3D Root { get; }
    public GodotWorldRendererLimits Limits => _limits;

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
    }

    public void ProcessFrame(GodotWorldFrameBudget budget)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (!budget.IsValid) throw new ArgumentOutOfRangeException(nameof(budget));
        long frameStart = Stopwatch.GetTimestamp();
        int builds = 0;
        while (builds < budget.MaximumBuilds && TryProcessNextBuild()) builds++;
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
            chunk.AdditionalTerrainTriangleCount, chunk.FirstAdditionalTerrainLocalY);
        return true;
    }

    public GodotWorldRendererSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new GodotWorldRendererSnapshot(
            _activeChunks, _freeRenderSlotCount, _visibleChunks, _buildCount, _retireCount,
            _activeNodes, _activeResources, _completedBuilds, _completedRetirements,
            _rebuilds, _publicRefreshes, _renderCapacityRejections, _groupCapacityRejections,
            _rebases, _windowMoves, _cancelledBuilds, _cancelledRetirements,
            _chunkRootTransformWrites, _groupValidationCalls, _verticalRebases,
            _lastMeshBuildTicks, _maximumMeshBuildTicks,
            _lastResourceCommitTicks, _maximumResourceCommitTicks,
            _lastFrameTicks, _maximumFrameTicks);
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
        _buildHead = 0;
        _buildCount = 0;
        _retireHead = 0;
        _retireCount = 0;
        _activeNodes = 0;
        _activeResources = 0;
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
        _activeChunks++;
        if (candidate.Root.Visible) _visibleChunks++;
        _activeNodes += candidate.NodeCount;
        _activeResources += candidate.ResourceCount;
        _completedBuilds++;
        if (replacing) _rebuilds++;
        _lastResourceCommitTicks = Stopwatch.GetTimestamp() - commitStart;
        _maximumResourceCommitTicks = Math.Max(_maximumResourceCommitTicks, _lastResourceCommitTicks);
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
        if (GodotObject.IsInstanceValid(chunk.PublicRoot)) chunk.PublicRoot.Free();
        _activeNodes -= chunk.PublicNodeCount;
        _activeResources -= chunk.PublicResourceCount;
        chunk.PublicRoot = candidate.Root;
        chunk.PublicNodeCount = candidate.NodeCount;
        chunk.PublicResourceCount = candidate.ResourceCount;
        chunk.FirstPublicLocalY = candidate.FirstLocalY;
        chunk.SemanticHash = semantic.Info.SemanticHash;
        chunk.Root.AddChild(candidate.Root);
        _activeNodes += candidate.NodeCount;
        _activeResources += candidate.ResourceCount;
        _publicRefreshes++;
        _lastResourceCommitTicks = Stopwatch.GetTimestamp() - commitStart;
        _maximumResourceCommitTicks = Math.Max(_maximumResourceCommitTicks, _lastResourceCommitTicks);
    }

    private RenderChunk CreateRenderChunk(ClientSemanticChunkHandle handle,
        ClientSemanticChunkView semantic, ClientPresentationOffset windowOffset)
    {
        var root = new Node3D { Name = $"Chunk_{semantic.Info.Key.Coord.X}_{semantic.Info.Key.Coord.Z}" };
        var structural = new Node3D { Name = "Structural" };
        root.AddChild(structural);
        try
        {
            ArrayMesh terrainMesh = CreateTerrainMesh(
                handle, semantic.Info.Key, out int yAnchor, out float firstTerrainY,
                out int terrainTriangleCount);
            structural.AddChild(new MeshInstance3D { Name = "BaseTerrain", Mesh = terrainMesh });
            int additionalTerrainTriangles = 0;
            float firstAdditionalTerrainY = float.NaN;
            for (int fragment = 0; fragment < semantic.AdditionalTerrainSurfaces.Count; fragment++)
            {
                ArrayMesh additionalMesh = CreateAdditionalTerrainMesh(
                    handle, fragment, yAnchor, out int fragmentTriangles, out float fragmentFirstY,
                    out WorldSurfaceId surfaceId);
                structural.AddChild(new MeshInstance3D
                {
                    Name = $"AdditionalTerrain_{surfaceId.Value}_{fragment}",
                    Mesh = additionalMesh,
                });
                additionalTerrainTriangles += fragmentTriangles;
                if (float.IsNaN(firstAdditionalTerrainY)) firstAdditionalTerrainY = fragmentFirstY;
            }

            var staticGroups = new SortedDictionary<WorldStaticRenderKey, List<Transform3D>>();
            float firstStaticY = float.NaN;
            foreach (WorldFrozenStaticRecord record in semantic.FrozenStatics)
                AddStatic(staticGroups, semantic.Info.Key, record.RenderKey, record.Position,
                    record.Orientation, record.Flags, yAnchor, ref firstStaticY);
            foreach (WorldMultiFragmentRecord record in semantic.MultiFragments)
                AddStatic(staticGroups, semantic.Info.Key, record.RenderKey, record.Position,
                    record.Orientation, record.Flags, yAnchor, ref firstStaticY);
            foreach ((WorldStaticRenderKey key, List<Transform3D> transforms) in staticGroups)
            {
                Mesh mesh = _assets?.ResolveStaticMesh(key) ?? _fallbackStaticMesh;
                structural.AddChild(CreateMultiMesh(
                    $"Static_{key.DefinitionId.Value}_{key.Variant}_{(ushort)key.Flags}", mesh, transforms));
            }

            RenderLayer publicLayer = CreatePublicLayer(semantic, yAnchor);
            root.AddChild(publicLayer.Root);
            return new RenderChunk(handle, semantic.Info.Key, semantic.Info.SemanticHash,
                windowOffset, root,
                structural, publicLayer.Root, terrainMesh, yAnchor, firstTerrainY,
                terrainTriangleCount, firstStaticY,
                publicLayer.FirstLocalY,
                3 + semantic.AdditionalTerrainSurfaces.Count + staticGroups.Count,
                1 + semantic.AdditionalTerrainSurfaces.Count + staticGroups.Count,
                semantic.AdditionalTerrainSurfaces.Count, additionalTerrainTriangles,
                firstAdditionalTerrainY,
                publicLayer.NodeCount, publicLayer.ResourceCount);
        }
        catch
        {
            root.Free();
            throw;
        }
    }

    private RenderLayer CreatePublicLayer(ClientSemanticChunkView semantic, int yAnchor)
    {
        var root = new Node3D { Name = "Public" };
        try
        {
            var groups = new SortedDictionary<uint, List<Transform3D>>();
            float firstY = float.NaN;
            foreach (WorldPublicRecord record in semantic.PublicRecords)
            {
                if ((record.StateFlags & WorldPublicStateFlags.Visible) == 0) continue;
                if (!groups.TryGetValue(record.PrefabId, out List<Transform3D>? transforms))
                    groups.Add(record.PrefabId, transforms = []);
                Transform3D transform = ToLocalTransform(
                    semantic.Info.Key, record.Position, record.Orientation, yAnchor);
                transforms.Add(transform);
                if (float.IsNaN(firstY)) firstY = transform.Origin.Y;
            }
            foreach ((uint prefab, List<Transform3D> transforms) in groups)
            {
                Mesh mesh = _assets?.ResolvePublicMesh(prefab) ?? _fallbackPublicMesh;
                root.AddChild(CreateMultiMesh($"Public_{prefab}", mesh, transforms));
            }
            return new RenderLayer(root, 1 + groups.Count, groups.Count, firstY);
        }
        catch
        {
            root.Free();
            throw;
        }
    }

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
        for (int index = 0; index < vertices.Length; index++)
        {
            TerrainBaseMeshVertex source = _terrainVertices[index];
            vertices[index] = new Vector3(
                source.LocalXMillimeters * MillimetersToMeters,
                checked((long)source.AbsoluteYMillimeters - yAnchor) * MillimetersToMeters,
                source.LocalZMillimeters * MillimetersToMeters);
            normals[index] = new Vector3(source.NormalX, source.NormalY, source.NormalZ).Normalized();
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
        foreach ((ushort material, List<int> indices) in trianglesByMaterial)
        {
            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            Material? resolved = _assets?.ResolveTerrainMaterial(material);
            if (resolved is not null) mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, resolved);
        }
        return mesh;
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
        for (int index = 0; index < summary.VertexCount; index++)
        {
            TerrainBaseMeshVertex source = _additionalTerrainVertices[index];
            vertices[index] = new Vector3(
                source.LocalXMillimeters * MillimetersToMeters,
                checked((long)source.AbsoluteYMillimeters - yAnchor) * MillimetersToMeters,
                source.LocalZMillimeters * MillimetersToMeters);
            normals[index] = new Vector3(source.NormalX, source.NormalY, source.NormalZ).Normalized();
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
        foreach ((ushort material, List<int> indices) in trianglesByMaterial)
        {
            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            Material? resolved = _assets?.ResolveTerrainMaterial(material);
            if (resolved is not null) mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, resolved);
        }
        return mesh;
    }

    private static MultiMeshInstance3D CreateMultiMesh(string name, Mesh mesh,
        List<Transform3D> transforms)
    {
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = transforms.Count,
        };
        for (int index = 0; index < transforms.Count; index++)
            multiMesh.SetInstanceTransform(index, transforms[index]);
        return new MultiMeshInstance3D { Name = name, Multimesh = multiMesh };
    }

    private static void AddStatic(SortedDictionary<WorldStaticRenderKey, List<Transform3D>> groups,
        SpatialChunkKey key, WorldStaticRenderKey renderKey, WorldPosition position,
        WorldYaw orientation, WorldStaticFlags flags, int yAnchor, ref float firstY)
    {
        if ((flags & WorldStaticFlags.Renderable) == 0) return;
        if (!groups.TryGetValue(renderKey, out List<Transform3D>? transforms))
            groups.Add(renderKey, transforms = []);
        Transform3D transform = ToLocalTransform(key, position, orientation, yAnchor);
        transforms.Add(transform);
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
            if ((record.StateFlags & WorldPublicStateFlags.Visible) == 0) continue;
            bool prior = false;
            for (int scan = 0; scan < index; scan++)
                if ((semantic.PublicRecords[scan].StateFlags & WorldPublicStateFlags.Visible) != 0 &&
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
        _activeResources -= chunk.ResourceCount;
        if (GodotObject.IsInstanceValid(chunk.Root)) chunk.Root.Free();
        _renderChunks[renderSlot] = null;
        int semanticIndex = checked((int)chunk.Handle.Index);
        if (_associations[semanticIndex].Generation == chunk.Handle.Generation &&
            _associations[semanticIndex].RenderSlotPlusOne == renderSlot + 1)
            _associations[semanticIndex] = default;
        if (releaseSlot) _freeRenderSlots[_freeRenderSlotCount++] = renderSlot;
    }

    private void SetVisible(RenderChunk chunk, bool visible)
    {
        if (chunk.Root.Visible == visible) return;
        chunk.Root.Visible = visible;
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
    private readonly record struct RenderLayer(Node3D Root, int NodeCount, int ResourceCount, float FirstLocalY);

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
            int publicNodeCount, int publicResourceCount)
        {
            Handle = handle;
            Key = key;
            SemanticHash = semanticHash;
            WindowOffset = windowOffset;
            Root = root;
            StructuralRoot = structuralRoot;
            PublicRoot = publicRoot;
            TerrainMesh = terrainMesh;
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
            PublicNodeCount = publicNodeCount;
            PublicResourceCount = publicResourceCount;
        }

        internal ClientSemanticChunkHandle Handle { get; }
        internal SpatialChunkKey Key { get; }
        internal ClientSemanticHash SemanticHash { get; set; }
        internal ClientPresentationOffset WindowOffset { get; set; }
        internal Node3D Root { get; }
        internal Node3D StructuralRoot { get; }
        internal Node3D PublicRoot { get; set; }
        internal ArrayMesh TerrainMesh { get; }
        internal int YAnchorMillimeters { get; }
        internal float FirstTerrainLocalY { get; }
        internal int TerrainTriangleCount { get; }
        internal int AdditionalTerrainSurfaceFragmentCount { get; }
        internal int AdditionalTerrainTriangleCount { get; }
        internal float FirstAdditionalTerrainLocalY { get; }
        internal float FirstStaticLocalY { get; }
        internal float FirstPublicLocalY { get; set; }
        internal int StructuralNodeCount { get; }
        internal int StructuralResourceCount { get; }
        internal int PublicNodeCount { get; set; }
        internal int PublicResourceCount { get; set; }
        internal int NodeCount => StructuralNodeCount + PublicNodeCount;
        internal int ResourceCount => StructuralResourceCount + PublicResourceCount;
    }
}
