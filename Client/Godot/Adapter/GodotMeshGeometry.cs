using Godot;
using UltraSim.Client.Content;

namespace UltraSim.Client.Godot;

/// <summary>
/// Godot-native geometry conversion/build helpers. This is adapter implementation surface, not a
/// Core geometry convention, renderer ABI, Game.Contracts API, or compatibility promise.
/// </summary>
public static class GodotMeshGeometry
{
    /// <summary>Realizes one admitted passive mesh; the caller owns the returned native wrapper.</summary>
    public static ArrayMesh CreateWorldPresentationMesh(
        ReadOnlySpan<byte> bytes, IReadOnlyList<Material> materials)
    {
        if (!WorldMeshArtifact.TryInspect(bytes, out WorldMeshArtifactInfo info) ||
            materials.Count < info.MaterialSlots)
            throw new InvalidDataException("The optional world mesh or material set is invalid.");
        ArrayMesh? result = null;
        try
        {
            for (int slot = 0; slot < info.MaterialSlots; slot++)
            {
                using var surface = new SurfaceTool();
                surface.Begin(Mesh.PrimitiveType.Triangles);
                surface.SetMaterial(materials[slot]);
                int emitted = 0;
                for (int index = 0; index < info.Vertices; index += 3)
                {
                    if (!WorldMeshArtifact.TryReadVertex(bytes, index, out WorldMeshVertex first))
                        throw new InvalidDataException("The admitted world mesh changed during realization.");
                    if (first.MaterialSlot != slot) continue;
                    for (int vertex = index; vertex < index + 3; vertex++)
                    {
                        if (!WorldMeshArtifact.TryReadVertex(bytes, vertex, out WorldMeshVertex point))
                            throw new InvalidDataException("The admitted world mesh changed during realization.");
                        surface.SetNormal(new Vector3(point.NormalX, point.NormalY, point.NormalZ));
                        surface.SetUV(new Vector2(point.U, point.V));
                        surface.SetColor(new Color(point.Red, point.Green, point.Blue, point.Alpha));
                        surface.AddVertex(new Vector3(point.X, point.Y, point.Z));
                        emitted++;
                    }
                }
                if (emitted != 0) result = surface.Commit(result);
            }
            return result ?? throw new InvalidDataException("The world mesh contains no surfaces.");
        }
        catch
        {
            result?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Converts one canonical UltraSim triangle to Godot's native front-face order. A future
    /// engine adapter must make its own coordinate/handedness-aware decision rather than copy this.
    /// </summary>
    public static void ConvertCanonicalTriangle(
        int first,
        int second,
        int third,
        out int nativeFirst,
        out int nativeSecond,
        out int nativeThird)
    {
        nativeFirst = first;
        nativeSecond = third;
        nativeThird = second;
    }

    /// <summary>Builds a bottom-anchored box directly in Godot coordinates and winding.</summary>
    public static ArrayMesh CreateGroundedBox(Vector3 size, Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (!size.IsFinite() || size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
            throw new ArgumentOutOfRangeException(nameof(size), "A finite positive box size is required.");

        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetMaterial(material);
        float x = size.X * 0.5f;
        float z = size.Z * 0.5f;
        Vector3 nbl = new(-x, 0f, -z);
        Vector3 nbr = new(x, 0f, -z);
        Vector3 nfl = new(-x, 0f, z);
        Vector3 nfr = new(x, 0f, z);
        Vector3 tbl = new(-x, size.Y, -z);
        Vector3 tbr = new(x, size.Y, -z);
        Vector3 tfl = new(-x, size.Y, z);
        Vector3 tfr = new(x, size.Y, z);
        AddGodotQuad(nbl, nbr, nfr, nfl);
        AddGodotQuad(tbl, tfl, tfr, tbr);
        AddGodotQuad(nbl, tbl, tbr, nbr);
        AddGodotQuad(nbr, tbr, tfr, nfr);
        AddGodotQuad(nfr, tfr, tfl, nfl);
        AddGodotQuad(nfl, tfl, tbl, nbl);
        return surface.Commit();

        void AddGodotQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) =>
            AppendCanonicalQuad(surface, a, b, c, d);
    }

    /// <summary>
    /// Builds a bottom-anchored triangular prism whose point faces native +Z. This is the current
    /// launchable-client character canary, not a durable character-mesh or asset schema.
    /// </summary>
    public static ArrayMesh CreateGroundedPointerWedge(
        float halfWidth,
        float backExtent,
        float frontExtent,
        float height,
        Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (!float.IsFinite(halfWidth) || !float.IsFinite(backExtent) ||
            !float.IsFinite(frontExtent) || !float.IsFinite(height) ||
            halfWidth <= 0f || backExtent <= 0f || frontExtent <= 0f || height <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(halfWidth), "Finite positive wedge dimensions are required.");
        }

        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetMaterial(material);
        Vector3 backLeftBottom = new(-halfWidth, 0f, -backExtent);
        Vector3 backRightBottom = new(halfWidth, 0f, -backExtent);
        Vector3 frontBottom = new(0f, 0f, frontExtent);
        Vector3 backLeftTop = new(-halfWidth, height, -backExtent);
        Vector3 backRightTop = new(halfWidth, height, -backExtent);
        Vector3 frontTop = new(0f, height, frontExtent);

        // Every supplied face uses UltraSim's semantic outward cross-product order. The shared
        // native append helper owns the clockwise Godot conversion and preserves that normal.
        AppendCanonicalTriangle(surface, backLeftBottom, backRightBottom, frontBottom);
        AppendCanonicalTriangle(surface, backLeftTop, frontTop, backRightTop);
        AppendCanonicalQuad(surface,
            backLeftBottom, backLeftTop, backRightTop, backRightBottom);
        AppendCanonicalQuad(surface,
            backRightBottom, backRightTop, frontTop, frontBottom);
        AppendCanonicalQuad(surface,
            frontBottom, frontTop, backLeftTop, backLeftBottom);
        return surface.Commit();
    }

    private static void AppendCanonicalQuad(
        SurfaceTool surface,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d)
    {
        Vector3 outwardNormal = (b - a).Cross(c - a).Normalized();
        AppendCanonicalTriangle(surface, a, b, c, outwardNormal);
        AppendCanonicalTriangle(surface, a, c, d, outwardNormal);
    }

    private static void AppendCanonicalTriangle(
        SurfaceTool surface,
        Vector3 a,
        Vector3 b,
        Vector3 c) =>
        AppendCanonicalTriangle(surface, a, b, c, (b - a).Cross(c - a).Normalized());

    private static void AppendCanonicalTriangle(
        SurfaceTool surface,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 outwardNormal)
    {
        // Convert semantic outward order to native Godot clockwise order. Preserve the semantic
        // normal explicitly: deriving it from native order points in the opposite mathematical
        // direction.
        surface.SetNormal(outwardNormal);
        surface.AddVertex(a);
        surface.SetNormal(outwardNormal);
        surface.AddVertex(c);
        surface.SetNormal(outwardNormal);
        surface.AddVertex(b);
    }
}
