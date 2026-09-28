using Godot;
using UltraSim.Client.Core;
using UltraSim.Game.World;

namespace UltraSim.Client.Godot;

/// <summary>Owner-local finite dense rows. Shape/color families are not actor identities.</summary>
internal sealed class GodotPrimitiveActorBatches : IDisposable
{
    private readonly Node3D _host;
    private readonly int _capacity;
    private readonly Group?[] _groups = new Group?[4];
    private readonly int[] _family, _row;
    internal int Count { get; private set; }
    internal int GroupCount { get; private set; }
    internal ulong TransformWrites { get; private set; }
    internal ulong BoundsWrites { get; private set; }
    internal long ReservedPayloadBytes => (long)_capacity * sizeof(int) * 2 +
        (long)GroupCount * _capacity * (sizeof(int) +
            System.Runtime.CompilerServices.Unsafe.SizeOf<WorldPublicEntityId>() +
            System.Runtime.CompilerServices.Unsafe.SizeOf<Transform3D>());

    internal GodotPrimitiveActorBatches(Node3D host, int capacity)
    {
        _host = host; _capacity = capacity;
        _family = new int[capacity]; _row = new int[capacity];
        Array.Fill(_row, -1);
    }

    internal void Add(int owner, WorldPublicEntityId id, in ClientActorDefinition definition,
        in Transform3D transform)
    {
        if (_row[owner] >= 0) throw new InvalidOperationException("Actor already has a cheap row.");
        int family = (int)definition.VisualKind;
        Group group = _groups[family] ??= Create(definition.VisualKind);
        int row = group.Count;
        if (row == _capacity) throw new InvalidOperationException("Cheap actor group is full.");
        group.Owners[row] = owner; group.Ids[row] = id;
        group.Count++; Count++;
        _family[owner] = family; _row[owner] = row;
        SetTransform(owner, definition, transform);
        group.MultiMesh.VisibleInstanceCount = group.Count;
    }

    internal void Remove(int owner)
    {
        int row = _row[owner];
        if (row < 0) return;
        int family = _family[owner];
        Group group = _groups[family]!;
        int last = --group.Count;
        if (row != last)
        {
            group.Owners[row] = group.Owners[last]; group.Ids[row] = group.Ids[last];
            group.Transforms[row] = group.Transforms[last];
            _row[group.Owners[row]] = row;
            group.MultiMesh.SetInstanceTransform(row, group.Transforms[row]); TransformWrites++;
        }
        group.Ids[last] = default; group.Owners[last] = -1;
        group.Transforms[last] = default;
        _row[owner] = -1; _family[owner] = 0; Count--;
        group.MultiMesh.VisibleInstanceCount = group.Count;
        if (group.Count == 0)
        {
            group.Dispose(); _groups[family] = null; GroupCount--;
        }
    }

    internal void SetTransform(int owner, in ClientActorDefinition definition, in Transform3D actorTransform)
    {
        Group group = _groups[_family[owner]]!;
        int row = _row[owner];
        float scale = definition.VisualScalePermille * 0.001f;
        Transform3D transform = actorTransform * new Transform3D(Basis.Identity.Scaled(Vector3.One * scale),
            Vector3.Zero) * group.MeshOffset;
        if (group.Transforms[row] == transform) return;
        group.Transforms[row] = transform;
        group.MultiMesh.SetInstanceTransform(row, transform); TransformWrites++;
    }

    internal void UpdateBounds()
    {
        for (int family = 1; family < _groups.Length; family++)
        {
            Group? group = _groups[family];
            if (group is null) continue;
            Aabb bounds = group.Transforms[0] * group.MeshBounds;
            for (int row = 1; row < group.Count; row++)
                bounds = bounds.Merge(group.Transforms[row] * group.MeshBounds);
            if (bounds == group.Node.CustomAabb) continue;
            group.MultiMesh.CustomAabb = bounds; group.Node.CustomAabb = bounds; BoundsWrites++;
        }
    }

    internal bool TryGet(int owner, out GodotCheapActorDiagnostics diagnostic)
    {
        diagnostic = default;
        if (_row[owner] < 0) return false;
        Group group = _groups[_family[owner]]!;
        int row = _row[owner];
        diagnostic = new(group.Ids[row], row, group.MultiMesh.VisibleInstanceCount, group.Transforms[row],
            group.Node.CustomAabb, group.MultiMesh.GetRid(), group.Node.GetInstanceId())
            { NativeTransform = group.MultiMesh.GetInstanceTransform(row) };
        return true;
    }

    public void Dispose()
    {
        for (int family = 1; family < _groups.Length; family++)
        { _groups[family]?.Dispose(); _groups[family] = null; }
        Array.Fill(_row, -1); Array.Clear(_family); Count = GroupCount = 0;
    }

    private Group Create(ClientActorVisualKind kind)
    {
        // Same permanent primitive definition/color as Detailed fallback; no content resolver.
        var resources = new GodotOwnedResources();
        MeshInstance3D? template = null;
        MultiMesh? multiMesh = null; MultiMeshInstance3D? node = null;
        try
        {
            template = (MeshInstance3D)GodotPrimitiveSubstrate.CreateActor(kind, resources);
            Mesh mesh = template.Mesh!; Transform3D offset = template.Transform;
            template.Free();
            template = null;
            multiMesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = mesh, InstanceCount = _capacity, VisibleInstanceCount = 0 };
            node = new MultiMeshInstance3D { Name = $"CheapActors_{kind}", Multimesh = multiMesh };
            _host.AddChild(node);
            var group = new Group(node, multiMesh, mesh, offset, _capacity, resources);
            GroupCount++; return group;
        }
        catch
        {
            template?.Free();
            node?.Free();
            multiMesh?.Dispose();
            resources.Dispose();
            throw;
        }
    }

    private sealed class Group(MultiMeshInstance3D node, MultiMesh multiMesh, Mesh mesh,
        Transform3D meshOffset, int capacity, GodotOwnedResources resources) : IDisposable
    {
        internal readonly MultiMeshInstance3D Node = node;
        internal readonly MultiMesh MultiMesh = multiMesh;
        internal readonly Mesh Mesh = mesh;
        internal readonly Transform3D MeshOffset = meshOffset;
        internal readonly Aabb MeshBounds = mesh.GetAabb();
        internal readonly WorldPublicEntityId[] Ids = new WorldPublicEntityId[capacity];
        internal readonly int[] Owners = new int[capacity];
        internal readonly Transform3D[] Transforms = new Transform3D[capacity];
        internal int Count;
        public void Dispose()
        { Node.Free(); MultiMesh.Dispose(); resources.Dispose(); }
    }
}

public readonly record struct GodotCheapActorDiagnostics(WorldPublicEntityId Id, int Row,
    int VisibleRows, Transform3D Transform, Aabb GroupBounds, Rid MultiMeshRid, ulong NativeOwnerId)
{
    public Transform3D NativeTransform { get; init; }
}
