using Godot;
using UltraSim.Client.Core;
using UltraSim.Game.Composition;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Client.Godot;

public readonly record struct GodotVegetationSnapshot(int ActiveChunks, int Patches, int LogicalBlades,
    int UncutLogicalBlades, int SourceVertices, long SubmittedVertices, long ReservedStagingBytes,
    ulong TargetedCutWrites, long PreparationTicks, bool CastsShadows);

/// <summary>Opt-in Compatibility prototype, with one finite shared shader/source mesh per renderer.</summary>
public sealed class GodotVegetation : IDisposable
{
    public const int BladesPerPatch = 12;
    public const int VerticesPerBlade = 12; // two disconnected triangle quads; no connector vertices
    // Includes radial scatter, wind, lean and blade width. Roof/floor exclusion must use the
    // whole envelope rather than only the patch anchor.
    public const int PatchEnvelopeMillimeters = 450;
    public const uint WorldSeed = 0xF1112026;
    private readonly Shader _shader;
    private readonly ShaderMaterial _material;
    private readonly ArrayMesh _mesh;
    private readonly ClientVegetationPatch[] _scratch = new ClientVegetationPatch[ClientVegetationScatter.MaximumPatchesPerChunk];
    private readonly bool[] _coveredByRoof = new bool[ClientVegetationScatter.MaximumPatchesPerChunk];

    public GodotVegetation()
    {
        _shader = new Shader { Code = """
            shader_type spatial;
            render_mode cull_disabled;
            void vertex() {
                int blade = VERTEX_ID / 12;
                int v = VERTEX_ID % 12;
                int segment = v / 6;
                int corner = v % 6;
                float side = (corner == 0 || corner == 2 || corner == 5) ? -1.0 : 1.0;
                float top = (corner == 1 || corner == 2 || corner == 4) ? 1.0 : 0.0;
                float t = (float(segment) + top) * 0.5;
                float seed = INSTANCE_CUSTOM.r * 65535.0 + float(blade) * 17.0;
                float angle = fract(sin((seed + 3.0) * 23.719) * 23817.271) * 6.2831853;
                float radius = sqrt(fract(sin((seed + 7.0) * 51.157) * 48271.813)) *
                    0.30 * INSTANCE_CUSTOM.a;
                vec2 offset = vec2(cos(angle), sin(angle)) * radius;
                float yaw = fract(sin((seed + 11.0) * 12.9898) * 43758.5453) * 6.2831853;
                float h = (0.20 + fract(sin((seed + 13.0) * 78.233) * 12345.6789) * 0.20) *
                    INSTANCE_CUSTOM.b;
                float width = 0.013 + fract(sin((seed + 17.0) * 19.31) * 13456.781) * 0.014;
                float sway = sin(TIME * 1.6 + seed) * 0.06 * t * t;
                float lean = 0.03 * sin(seed) * t;
                VERTEX = vec3(offset.x + cos(yaw) * side * width * (1.0 - 0.85*t) + sway + lean,
                    t*h, offset.y + sin(yaw) * side * width * (1.0 - 0.85*t));
                if (float(blade) < INSTANCE_CUSTOM.g * 12.0) VERTEX = vec3(0.0);
                NORMAL = vec3(-sin(yaw), 0.2, cos(yaw));
            }
            void fragment() { ALBEDO = vec3(0.12, 0.32, 0.08); ROUGHNESS = 0.95; }
            """ };
        _material = new ShaderMaterial { Shader = _shader };
        _mesh = new ArrayMesh();
        var vertices = new Vector3[BladesPerPatch * VerticesPerBlade];
        // A finite non-degenerate source keeps native bounds valid; the shader derives final blades.
        for (int i = 0; i < vertices.Length; i++) vertices[i] = new((i % 3) * .01f, (i % 2) * .1f, 0);
        using var arrays = new global::Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _mesh.SurfaceSetMaterial(0, _material);
    }

    public GodotVegetationChunk? Prepare(ClientWorldSemanticCache cache,
        ClientSemanticChunkHandle handle, in ClientSemanticChunkView semantic, int yAnchor)
    {
        int count = ClientVegetationScatter.Prepare(cache, handle, WorldSeed, _scratch);
        Array.Clear(_coveredByRoof, 0, count);
        // Multi fragments have one home chunk even when their exact bounds cover neighboring
        // terrain chunks. Check the finite 3x3 halo, not just this chunk's fragment list;
        // otherwise grass appears through a floor/roof on the other side of a chunk seam.
        SpatialChunkKey home = semantic.Info.Key;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            long nx = (long)home.Coord.X + dx, nz = (long)home.Coord.Z + dz;
            if (nx is < int.MinValue or > int.MaxValue ||
                nz is < int.MinValue or > int.MaxValue) continue;
            var neighbor = new SpatialChunkKey(home.Space, (int)nx, (int)nz);
            bool found = cache.PreparedComposition is { } composition
                ? composition.TryGetMultiFragments(neighbor, out WorldMultiFragmentChunkView fragments)
                : cache.PreparedWorld.TryGetMultiFragments(neighbor, out fragments);
            if (!found) continue;
            for (int pieceIndex = 0; pieceIndex < fragments.Count; pieceIndex++)
            {
                WorldMultiFragmentRecord piece = fragments.GetRecord(pieceIndex);
                if (piece.DefinitionId.Value != GameWorldStaticDefinitionIds.CabinRoof) continue;
                WorldStaticBounds bounds = piece.Bounds;
                for (int patchIndex = 0; patchIndex < count; patchIndex++)
                {
                    ClientVegetationPatch patch = _scratch[patchIndex];
                    long globalX = (long)home.Coord.X * GameWorldSpatialProfile.WorldChunkEdgeUnits +
                        patch.LocalXMillimeters;
                    long globalZ = (long)home.Coord.Z * GameWorldSpatialProfile.WorldChunkEdgeUnits +
                        patch.LocalZMillimeters;
                    if (globalX >= bounds.MinimumXMillimeters - PatchEnvelopeMillimeters &&
                        globalX < bounds.MaximumXExclusiveMillimeters + PatchEnvelopeMillimeters &&
                        globalZ >= bounds.MinimumZMillimeters - PatchEnvelopeMillimeters &&
                        globalZ < bounds.MaximumZExclusiveMillimeters + PatchEnvelopeMillimeters)
                        _coveredByRoof[patchIndex] = true;
                }
            }
        }
        int visible = 0;
        for (int index = 0; index < count; index++)
        {
            if (!_coveredByRoof[index]) _scratch[visible++] = _scratch[index];
        }
        count = visible;
        if (count == 0) return null;
        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = _mesh,
            InstanceCount = count
        };
        float minimum = float.MaxValue, maximum = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            var p = _scratch[i]; float y = checked((long)p.AbsoluteYMillimeters - yAnchor) * .001f;
            multi.SetInstanceTransform(i, new(Basis.Identity, new(p.LocalXMillimeters * .001f, y, p.LocalZMillimeters * .001f)));
            float vigor = 0.80f + (p.Seed & 31) / 100f;
            float spread = 0.72f + ((p.Seed >> 5) & 31) / 100f;
            multi.SetInstanceCustomData(i, new(p.Seed / 65535f, 0, vigor, spread));
            minimum = Math.Min(minimum, y); maximum = Math.Max(maximum, y);
        }
        // Covers the full shader envelope, including wind/lean and every sampled sloped root.
        multi.CustomAabb = new(new Vector3(-.45f, minimum - .01f, -.45f),
            new Vector3(16.9f, maximum - minimum + .58f, 16.9f));
        return new GodotVegetationChunk(multi, count);
    }

    public void Dispose() { _mesh.Dispose(); _material.Dispose(); _shader.Dispose(); }
}

/// <summary>Exactly one patch batch owned by one renderer chunk. No autonomous streaming/process loop.</summary>
public sealed class GodotVegetationChunk : IDisposable
{
    public GodotVegetationChunk(MultiMesh multi, int count)
    {
        MultiMesh = multi; Count = count;
        Node = new MultiMeshInstance3D
        {
            Name = "PresentationGrass",
            Multimesh = multi,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
    }
    public MultiMeshInstance3D Node { get; }
    public MultiMesh MultiMesh { get; }
    public int Count { get; }
    public int CollapsedBlades { get; private set; }
    public ulong CutWrites { get; private set; }

    public bool TrySetCut(int patch, float amount)
    {
        if (patch < 0 || patch >= Count || !float.IsFinite(amount) || amount < 0 || amount > 1) return false;
        Color old = MultiMesh.GetInstanceCustomData(patch);
        if (old.G == amount) return true;
        CollapsedBlades += (int)MathF.Ceiling(amount * GodotVegetation.BladesPerPatch) -
            (int)MathF.Ceiling(old.G * GodotVegetation.BladesPerPatch);
        MultiMesh.SetInstanceCustomData(patch, new Color(old.R, amount, old.B, old.A)); CutWrites++;
        return true;
    }
    public void Dispose() { MultiMesh.Dispose(); }
}
