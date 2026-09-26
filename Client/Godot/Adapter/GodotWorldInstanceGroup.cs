using Godot;

namespace UltraSim.Client.Godot;

/// <summary>Native realization choice, not a Game/profile or user graphics setting.</summary>
public enum GodotWorldInstanceBackend : byte { NodeMultiMesh = 1, DirectRid = 2 }
public enum GodotWorldInstanceUpload : byte { PerRow = 1, Bulk = 2 }

/// <summary>
/// One chunk-local compatible group. The mesh/material are borrowed from the resolved resource
/// owner; the MultiMesh and optional visual RID are exclusively owned here. No semantic identity
/// is derived from native slots. Public refresh replaces the whole group from disclosed records.
/// </summary>
internal sealed class GodotWorldInstanceGroup : IDisposable
{
    internal const int FloatsPerTransform = 12;
    private readonly MultiMeshInstance3D? _node;
    private readonly Mesh _mesh;
    private readonly Material? _material;
    private Rid _visual;
    private bool _bound;
    private bool _disposed;
    internal Rid Scenario { get; private set; }
    internal Transform3D GlobalTransform { get; private set; }
    internal bool Visible { get; private set; }
    internal string Name { get; }
    internal MultiMesh MultiMesh { get; }
    internal Aabb Bounds { get; }
    internal int Count { get; }
    internal bool Direct => _node is null;
    internal bool BulkUploaded { get; }
    internal ulong TransformWrites { get; private set; }
    internal ulong ScenarioBindings { get; private set; }
    internal Rid VisualRid => Direct ? _visual : _node!.GetInstance();
    internal Rid MeshRid => _mesh.GetRid();
    internal Rid MaterialOverrideRid => _material?.GetRid() ?? default;

    internal GodotWorldInstanceGroup(Node3D parent, string name, Mesh mesh,
        List<Transform3D> transforms, Material? material, GodotWorldInstanceBackend backend,
        GodotWorldInstanceUpload upload, Span<float> scratch)
    {
        if (transforms.Count == 0) throw new ArgumentException("Empty groups are not native objects.");
        Name = name;
        Count = transforms.Count;
        _mesh = mesh;
        _material = material;
        Aabb meshBounds = mesh.GetAabb();
        Aabb combined = transforms[0] * meshBounds;
        for (int index = 1; index < transforms.Count; index++)
            combined = combined.Merge(transforms[index] * meshBounds);
        Bounds = combined;
        MultiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = transforms.Count,
            CustomAabb = combined,
        };
        try
        {
            BulkUploaded = upload == GodotWorldInstanceUpload.Bulk;
            if (BulkUploaded)
            {
                Span<float> buffer = scratch[..checked(transforms.Count * FloatsPerTransform)];
                for (int index = 0; index < transforms.Count; index++)
                    WriteTransform(transforms[index], buffer.Slice(index * FloatsPerTransform, FloatsPerTransform));
                RenderingServer.MultimeshSetBuffer(MultiMesh.GetRid(), (ReadOnlySpan<float>)buffer);
            }
            else
                for (int index = 0; index < transforms.Count; index++)
                    MultiMesh.SetInstanceTransform(index, transforms[index]);
            if (backend == GodotWorldInstanceBackend.NodeMultiMesh)
            {
                _node = new MultiMeshInstance3D
                {
                    Name = name, Multimesh = MultiMesh, CustomAabb = combined,
                    MaterialOverride = material,
                };
                parent.AddChild(_node);
            }
            else
            {
                _visual = RenderingServer.InstanceCreate();
                RenderingServer.InstanceSetBase(_visual, MultiMesh.GetRid());
                RenderingServer.InstanceSetCustomAabb(_visual, combined);
                if (material is not null)
                    RenderingServer.InstanceGeometrySetMaterialOverride(_visual, material.GetRid());
                // A prepared candidate is invisible and scenario-less until native commit.
                RenderingServer.InstanceSetVisible(_visual, false);
            }
        }
        catch { Dispose(); throw; }
    }

    internal void Synchronize(Rid scenario, Transform3D transform, bool visible)
    {
        if (!Direct || _disposed) return;
        if (!_bound || Scenario != scenario)
        {
            RenderingServer.InstanceSetScenario(_visual, scenario);
            Scenario = scenario;
            ScenarioBindings++;
        }
        if (!_bound || GlobalTransform != transform)
        {
            RenderingServer.InstanceSetTransform(_visual, transform);
            GlobalTransform = transform;
            TransformWrites++;
        }
        if (!_bound || Visible != visible) RenderingServer.InstanceSetVisible(_visual, visible);
        Visible = visible;
        _bound = true;
    }

    // Godot 4.7.1 RenderingServer documents a row-major 3×4 buffer, not Transform3D's
    // column-major managed memory layout. Keep this explicit and prove native round trips.
    internal static void WriteTransform(in Transform3D transform, Span<float> output)
    {
        output[0] = transform.Basis.X.X; output[1] = transform.Basis.Y.X;
        output[2] = transform.Basis.Z.X; output[3] = transform.Origin.X;
        output[4] = transform.Basis.X.Y; output[5] = transform.Basis.Y.Y;
        output[6] = transform.Basis.Z.Y; output[7] = transform.Origin.Y;
        output[8] = transform.Basis.X.Z; output[9] = transform.Basis.Y.Z;
        output[10] = transform.Basis.Z.Z; output[11] = transform.Origin.Z;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_visual.IsValid) RenderingServer.FreeRid(_visual);
        _visual = default;
        if (_node is not null && GodotObject.IsInstanceValid(_node)) _node.Free();
        MultiMesh.Dispose();
        _disposed = true;
        // Explicit references keep borrowed source resources alive until native ownership ends.
        GC.KeepAlive(_mesh);
        GC.KeepAlive(_material);
    }
}

/// <summary>Bounded native diagnostic; never a semantic identity or picking authority.</summary>
public readonly record struct GodotWorldInstanceGroupDiagnostics(
    string Name, bool Direct, int Count, Aabb Bounds, Rid Scenario, Rid VisualRid,
    Transform3D GlobalTransform, bool Visible, ulong TransformWrites, ulong ScenarioBindings,
    Rid MultiMeshRid, Rid MeshRid, Rid MaterialOverrideRid);
