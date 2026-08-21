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

public readonly record struct GodotWorldRendererLimits(
    int MaximumRenderChunks,
    int MaximumGroupsPerChunk)
{
    public static GodotWorldRendererLimits Default => new(256, 128);

    internal void Validate(ClientWorldSemanticCache cache)
    {
        if (MaximumRenderChunks <= 0 ||
            MaximumRenderChunks > cache.Limits.MaximumResidentChunks ||
            MaximumGroupsPerChunk <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumRenderChunks));
    }
}

public readonly record struct GodotWorldFrameBudget(int MaximumBuilds, int MaximumRetirements)
{
    public static GodotWorldFrameBudget Default => new(2, 4);
    public bool IsValid => MaximumBuilds >= 0 && MaximumRetirements >= 0;
}

public readonly record struct WorldRenderOrigin(SpatialChunkCoord Chunk, int YMillimeters);

public enum GodotWorldRenderQueueError : byte
{
    None = 0,
    InvalidHandle,
    SemanticChunkNotResident,
    QueueCapacityExceeded,
}

public readonly record struct GodotWorldRendererSnapshot(
    int ActiveRenderChunks,
    int VisibleRenderChunks,
    int PendingBuilds,
    int PendingRetirements,
    int ActiveNodes,
    int ActiveResources,
    ulong CompletedBuilds,
    ulong CompletedRetirements,
    ulong Rebuilds,
    ulong Rebases,
    long LastMeshBuildTicks,
    long MaximumMeshBuildTicks,
    long LastResourceCommitTicks,
    long MaximumResourceCommitTicks,
    long LastFrameTicks,
    long MaximumFrameTicks)
{
    public double LastMeshBuildMicroseconds =>
        LastMeshBuildTicks * 1_000_000d / Stopwatch.Frequency;
    public double MaximumMeshBuildMicroseconds =>
        MaximumMeshBuildTicks * 1_000_000d / Stopwatch.Frequency;
    public double LastResourceCommitMicroseconds =>
        LastResourceCommitTicks * 1_000_000d / Stopwatch.Frequency;
    public double MaximumResourceCommitMicroseconds =>
        MaximumResourceCommitTicks * 1_000_000d / Stopwatch.Frequency;
    public double LastFrameMicroseconds => LastFrameTicks * 1_000_000d / Stopwatch.Frequency;
    public double MaximumFrameMicroseconds => MaximumFrameTicks * 1_000_000d / Stopwatch.Frequency;
}

/// <summary>
/// Engine-thread Godot presentation over the production semantic cache. Render chunks are bounded,
/// origin-relative, and disposable; they never become world authority or semantic residency.
/// </summary>
public sealed class GodotWorldRenderer : IDisposable
{
    private const float MillimetersToMeters = 0.001f;

    private readonly int _ownerThreadId = System.Environment.CurrentManagedThreadId;
    private readonly ClientWorldSemanticCache _cache;
    private readonly GodotWorldRendererLimits _limits;
    private readonly IGodotWorldAssetResolver? _assets;
    private readonly RenderChunk?[] _renderChunks;
    private readonly ClientSemanticChunkHandle[] _buildQueue;
    private readonly ClientSemanticChunkHandle[] _retireQueue;
    private readonly bool[] _desiredVisible;
    private readonly TerrainBaseMeshVertex[] _terrainVertices =
        new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
    private readonly ushort[] _terrainIndices =
        new ushort[TerrainChunkFormat.TriangleIndexCount];
    private readonly ushort[] _terrainMaterials =
        new ushort[TerrainChunkFormat.TriangleCount];
    private readonly BoxMesh _fallbackStaticMesh;
    private readonly BoxMesh _fallbackPublicMesh;
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
    private ulong _rebases;
    private long _lastMeshBuildTicks;
    private long _maximumMeshBuildTicks;
    private long _lastResourceCommitTicks;
    private long _maximumResourceCommitTicks;
    private long _lastFrameTicks;
    private long _maximumFrameTicks;
    private bool _disposed;

    public GodotWorldRenderer(
        Node3D host,
        ClientWorldSemanticCache cache,
        GodotWorldRendererLimits limits = default,
        IGodotWorldAssetResolver? assets = null)
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
        _buildQueue = new ClientSemanticChunkHandle[limits.MaximumRenderChunks];
        _retireQueue = new ClientSemanticChunkHandle[limits.MaximumRenderChunks];
        _desiredVisible = new bool[limits.MaximumRenderChunks];
        _fallbackStaticMesh = new BoxMesh { Size = Vector3.One };
        _fallbackPublicMesh = new BoxMesh { Size = new Vector3(0.6f, 1.8f, 0.6f) };
        Root = new Node3D { Name = "UltraSimWorld" };
        host.AddChild(Root);
        _activeNodes = 1;
    }

    public Node3D Root { get; }

    public bool TryQueueResident(
        ClientSemanticChunkHandle handle,
        bool visible,
        out GodotWorldRenderQueueError error)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (!TryGetIndex(handle, out int index))
        {
            error = GodotWorldRenderQueueError.InvalidHandle;
            return false;
        }
        if (!_cache.TryGetInfo(handle, out ClientSemanticChunkInfo info) ||
            info.State != ClientSemanticChunkState.Resident)
        {
            error = GodotWorldRenderQueueError.SemanticChunkNotResident;
            return false;
        }
        _desiredVisible[index] = visible;
        RenderChunk? current = _renderChunks[index];
        if (current is not null && current.Handle == handle && current.SemanticHash == info.SemanticHash)
        {
            SetVisible(current, visible);
            error = GodotWorldRenderQueueError.None;
            return true;
        }
        if (Contains(_buildQueue, _buildHead, _buildCount, handle))
        {
            error = GodotWorldRenderQueueError.None;
            return true;
        }
        if (_buildCount == _buildQueue.Length)
        {
            error = GodotWorldRenderQueueError.QueueCapacityExceeded;
            return false;
        }
        Enqueue(_buildQueue, ref _buildHead, ref _buildCount, handle);
        error = GodotWorldRenderQueueError.None;
        return true;
    }

    public bool TryQueueRetire(
        ClientSemanticChunkHandle handle,
        out GodotWorldRenderQueueError error)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (!TryGetIndex(handle, out int index))
        {
            error = GodotWorldRenderQueueError.InvalidHandle;
            return false;
        }
        _desiredVisible[index] = false;
        RemoveQueued(_buildQueue, ref _buildHead, ref _buildCount, handle);
        if (_renderChunks[index] is null || _renderChunks[index]!.Handle != handle ||
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
        if (!TryGetIndex(handle, out int index)) return false;
        _desiredVisible[index] = visible;
        RenderChunk? chunk = _renderChunks[index];
        if (chunk is null || chunk.Handle != handle) return false;
        SetVisible(chunk, visible);
        return true;
    }

    public void SetRenderOrigin(WorldRenderOrigin origin)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (_origin == origin) return;
        _origin = origin;
        for (int index = 0; index < _renderChunks.Length; index++)
        {
            RenderChunk? chunk = _renderChunks[index];
            if (chunk is not null) ApplyOrigin(chunk);
        }
        _rebases++;
    }

    public void ProcessFrame(GodotWorldFrameBudget budget)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (!budget.IsValid) throw new ArgumentOutOfRangeException(nameof(budget));
        long frameStart = Stopwatch.GetTimestamp();
        for (int count = 0; count < budget.MaximumRetirements && _retireCount != 0; count++)
            Retire(Dequeue(_retireQueue, ref _retireHead, ref _retireCount));
        for (int count = 0; count < budget.MaximumBuilds && _buildCount != 0; count++)
            Build(Dequeue(_buildQueue, ref _buildHead, ref _buildCount));
        _lastFrameTicks = Stopwatch.GetTimestamp() - frameStart;
        _maximumFrameTicks = Math.Max(_maximumFrameTicks, _lastFrameTicks);
    }

    public bool IsRendered(ClientSemanticChunkHandle handle)
    {
        EnsureOwner();
        if (!TryGetIndex(handle, out int index)) return false;
        return _renderChunks[index]?.Handle == handle;
    }

    public GodotWorldRendererSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new GodotWorldRendererSnapshot(
            _activeChunks, _visibleChunks, _buildCount, _retireCount,
            _activeNodes, _activeResources, _completedBuilds, _completedRetirements,
            _rebuilds, _rebases, _lastMeshBuildTicks, _maximumMeshBuildTicks,
            _lastResourceCommitTicks, _maximumResourceCommitTicks,
            _lastFrameTicks, _maximumFrameTicks);
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        for (int index = 0; index < _renderChunks.Length; index++)
            DisposeChunk(index);
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
        if (!TryGetIndex(handle, out int index) ||
            !_cache.TryGetResidentView(handle, out ClientSemanticChunkView semantic))
            return;

        long buildStart = Stopwatch.GetTimestamp();
        RenderChunk candidate = CreateRenderChunk(handle, semantic);
        _lastMeshBuildTicks = Stopwatch.GetTimestamp() - buildStart;
        _maximumMeshBuildTicks = Math.Max(_maximumMeshBuildTicks, _lastMeshBuildTicks);

        long commitStart = Stopwatch.GetTimestamp();
        bool replacing = _renderChunks[index] is not null;
        if (replacing) DisposeChunk(index);
        _renderChunks[index] = candidate;
        ApplyOrigin(candidate);
        candidate.Root.Visible = _desiredVisible[index];
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

    private RenderChunk CreateRenderChunk(
        ClientSemanticChunkHandle handle,
        ClientSemanticChunkView semantic)
    {
        var root = new Node3D { Name = $"Chunk_{semantic.Info.Key.Coord.X}_{semantic.Info.Key.Coord.Z}" };
        int nodes = 1;
        int resources = 0;
        try
        {
            ArrayMesh terrainMesh = CreateTerrainMesh(handle, semantic.Info.Key);
            resources++;
            var terrain = new MeshInstance3D
            {
                Name = "BaseTerrain",
                Mesh = terrainMesh,
            };
            root.AddChild(terrain);
            nodes++;

            var staticGroups = new SortedDictionary<WorldStaticRenderKey, List<Transform3D>>();
            foreach (WorldFrozenStaticRecord record in semantic.FrozenStatics)
                AddStatic(staticGroups, semantic.Info.Key, record.RenderKey,
                    record.Position, record.Orientation, record.Flags);
            foreach (WorldMultiFragmentRecord record in semantic.MultiFragments)
                AddStatic(staticGroups, semantic.Info.Key, record.RenderKey,
                    record.Position, record.Orientation, record.Flags);
            if (staticGroups.Count > _limits.MaximumGroupsPerChunk)
                throw new InvalidOperationException("Static render-group capacity was exceeded.");
            foreach ((WorldStaticRenderKey key, List<Transform3D> transforms) in staticGroups)
            {
                Mesh mesh = _assets?.ResolveStaticMesh(key) ?? _fallbackStaticMesh;
                MultiMeshInstance3D instance = CreateMultiMesh(
                    $"Static_{key.DefinitionId.Value}_{key.Variant}_{(ushort)key.Flags}", mesh, transforms);
                root.AddChild(instance);
                nodes++;
                resources++;
            }

            var publicGroups = new SortedDictionary<uint, List<Transform3D>>();
            foreach (WorldPublicRecord record in semantic.PublicRecords)
            {
                if ((record.StateFlags & WorldPublicStateFlags.Visible) == 0) continue;
                if (!publicGroups.TryGetValue(record.PrefabId, out List<Transform3D>? transforms))
                    publicGroups.Add(record.PrefabId, transforms = []);
                transforms.Add(ToLocalTransform(semantic.Info.Key, record.Position, record.Orientation));
            }
            if (staticGroups.Count + publicGroups.Count > _limits.MaximumGroupsPerChunk)
                throw new InvalidOperationException("Combined render-group capacity was exceeded.");
            foreach ((uint prefab, List<Transform3D> transforms) in publicGroups)
            {
                Mesh mesh = _assets?.ResolvePublicMesh(prefab) ?? _fallbackPublicMesh;
                MultiMeshInstance3D instance = CreateMultiMesh($"Public_{prefab}", mesh, transforms);
                root.AddChild(instance);
                nodes++;
                resources++;
            }
            return new RenderChunk(handle, semantic.Info.Key, semantic.Info.SemanticHash,
                root, nodes, resources);
        }
        catch
        {
            root.Free();
            throw;
        }
    }

    private ArrayMesh CreateTerrainMesh(ClientSemanticChunkHandle handle, SpatialChunkKey key)
    {
        if (!_cache.TryBuildResidentTerrainMesh(
                handle, _terrainVertices, _terrainIndices, _terrainMaterials, out _,
                out ClientSemanticChunkError semanticError, out TerrainBaseMeshBuildError meshError))
            throw new InvalidOperationException(
                $"Could not prepare resident terrain mesh for {key}: {semanticError}/{meshError}.");

        var vertices = new Vector3[TerrainChunkFormat.HeightVertexCount];
        var normals = new Vector3[TerrainChunkFormat.HeightVertexCount];
        for (int index = 0; index < vertices.Length; index++)
        {
            TerrainBaseMeshVertex source = _terrainVertices[index];
            vertices[index] = new Vector3(
                source.LocalXMillimeters * MillimetersToMeters,
                source.AbsoluteYMillimeters * MillimetersToMeters,
                source.LocalZMillimeters * MillimetersToMeters);
            normals[index] = new Vector3(source.NormalX, source.NormalY, source.NormalZ).Normalized();
        }

        var trianglesByMaterial = new SortedDictionary<ushort, List<int>>();
        for (int triangle = 0; triangle < TerrainChunkFormat.TriangleCount; triangle++)
        {
            ushort material = _terrainMaterials[triangle];
            if (!trianglesByMaterial.TryGetValue(material, out List<int>? indices))
                trianglesByMaterial.Add(material, indices = []);
            int source = triangle * 3;
            indices.Add(_terrainIndices[source]);
            indices.Add(_terrainIndices[source + 1]);
            indices.Add(_terrainIndices[source + 2]);
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

    private static MultiMeshInstance3D CreateMultiMesh(
        string name,
        Mesh mesh,
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

    private static void AddStatic(
        SortedDictionary<WorldStaticRenderKey, List<Transform3D>> groups,
        SpatialChunkKey key,
        WorldStaticRenderKey renderKey,
        WorldPosition position,
        WorldYaw orientation,
        WorldStaticFlags flags)
    {
        if ((flags & WorldStaticFlags.Renderable) == 0) return;
        if (!groups.TryGetValue(renderKey, out List<Transform3D>? transforms))
            groups.Add(renderKey, transforms = []);
        transforms.Add(ToLocalTransform(key, position, orientation));
    }

    private static Transform3D ToLocalTransform(
        SpatialChunkKey key,
        in WorldPosition position,
        WorldYaw orientation)
    {
        float radians = orientation.Units * (MathF.Tau / WorldYaw.UnitsPerTurn);
        var basis = new Basis(Vector3.Up, radians);
        long chunkOriginX = checked((long)key.Coord.X * GameWorldSpatialProfile.WorldChunkEdgeUnits);
        long chunkOriginZ = checked((long)key.Coord.Z * GameWorldSpatialProfile.WorldChunkEdgeUnits);
        var origin = new Vector3(
            checked(position.GlobalXMillimeters - chunkOriginX) * MillimetersToMeters,
            position.YMillimeters * MillimetersToMeters,
            checked(position.GlobalZMillimeters - chunkOriginZ) * MillimetersToMeters);
        return new Transform3D(basis, origin);
    }

    private void ApplyOrigin(RenderChunk chunk)
    {
        chunk.Root.Position = new Vector3(
            checked((long)chunk.Key.Coord.X - _origin.Chunk.X) * GameWorldSpatialProfile.WorldChunkEdgeUnits * MillimetersToMeters,
            -_origin.YMillimeters * MillimetersToMeters,
            checked((long)chunk.Key.Coord.Z - _origin.Chunk.Z) * GameWorldSpatialProfile.WorldChunkEdgeUnits * MillimetersToMeters);
    }

    private void Retire(ClientSemanticChunkHandle handle)
    {
        if (!TryGetIndex(handle, out int index) || _renderChunks[index]?.Handle != handle) return;
        DisposeChunk(index);
        _completedRetirements++;
    }

    private void DisposeChunk(int index)
    {
        RenderChunk? chunk = _renderChunks[index];
        if (chunk is null) return;
        if (chunk.Root.Visible) _visibleChunks--;
        _activeChunks--;
        _activeNodes -= chunk.NodeCount;
        _activeResources -= chunk.ResourceCount;
        if (GodotObject.IsInstanceValid(chunk.Root)) chunk.Root.Free();
        _renderChunks[index] = null;
    }

    private void SetVisible(RenderChunk chunk, bool visible)
    {
        if (chunk.Root.Visible == visible) return;
        chunk.Root.Visible = visible;
        _visibleChunks += visible ? 1 : -1;
    }

    private bool TryGetIndex(ClientSemanticChunkHandle handle, out int index)
    {
        if (!handle.IsValid || handle.Index >= (uint)_renderChunks.Length)
        {
            index = -1;
            return false;
        }
        index = (int)handle.Index;
        return true;
    }

    private static bool Contains(
        ClientSemanticChunkHandle[] queue,
        int head,
        int count,
        ClientSemanticChunkHandle handle)
    {
        for (int offset = 0; offset < count; offset++)
            if (queue[(head + offset) % queue.Length] == handle) return true;
        return false;
    }

    private static void Enqueue(
        ClientSemanticChunkHandle[] queue,
        ref int head,
        ref int count,
        ClientSemanticChunkHandle handle)
    {
        queue[(head + count) % queue.Length] = handle;
        count++;
    }

    private static ClientSemanticChunkHandle Dequeue(
        ClientSemanticChunkHandle[] queue,
        ref int head,
        ref int count)
    {
        ClientSemanticChunkHandle handle = queue[head];
        queue[head] = default;
        head = (head + 1) % queue.Length;
        count--;
        return handle;
    }

    private static void RemoveQueued(
        ClientSemanticChunkHandle[] queue,
        ref int head,
        ref int count,
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
        if (found < 0) return;
        for (int offset = found; offset < count - 1; offset++)
            queue[(head + offset) % queue.Length] = queue[(head + offset + 1) % queue.Length];
        queue[(head + count - 1) % queue.Length] = default;
        count--;
        if (count == 0) head = 0;
    }

    private void EnsureOwner()
    {
        if (System.Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The Godot world renderer is engine-thread bound.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record RenderChunk(
        ClientSemanticChunkHandle Handle,
        SpatialChunkKey Key,
        ClientSemanticHash SemanticHash,
        Node3D Root,
        int NodeCount,
        int ResourceCount);
}
