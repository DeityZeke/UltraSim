using System.Buffers.Binary;

namespace UltraSim.Client.Content;

/// <summary>
/// Small, passive, triangle-only visual mesh. It conveys no collision, support, interaction,
/// WorldStatic identity or other Game truth. Coordinates are metres in the native presentation
/// frame; material slots are resolved independently by the presentation adapter.
/// </summary>
public static class WorldMeshArtifact
{
    public const int HeaderBytes = 16;
    public const int VertexBytes = 52;
    public const int MaximumVertices = 96_000;
    public const int MaximumMaterialSlots = 8;
    public const int MaximumBytes = HeaderBytes + MaximumVertices * VertexBytes;

    private static ReadOnlySpan<byte> Magic => "USWM"u8;

    public static bool TryInspect(ReadOnlySpan<byte> bytes, out WorldMeshArtifactInfo info)
    {
        info = default;
        if (bytes.Length < HeaderBytes || bytes.Length > MaximumBytes ||
            !bytes[..4].SequenceEqual(Magic) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != 1)
            return false;
        uint rawCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        uint rawSlots = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        if (rawCount is < 3 or > MaximumVertices || rawCount % 3 != 0 ||
            rawSlots is < 1 or > MaximumMaterialSlots ||
            bytes.Length != HeaderBytes + (long)rawCount * VertexBytes)
            return false;

        int vertices = (int)rawCount;
        int slots = (int)rawSlots;
        for (int index = 0; index < vertices; index += 3)
        {
            if (!TryReadVertex(bytes, index, out WorldMeshVertex a) ||
                !TryReadVertex(bytes, index + 1, out WorldMeshVertex b) ||
                !TryReadVertex(bytes, index + 2, out WorldMeshVertex c) ||
                a.MaterialSlot >= slots || a.MaterialSlot != b.MaterialSlot ||
                a.MaterialSlot != c.MaterialSlot)
                return false;
            float ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
            float vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
            float cx = uy * vz - uz * vy;
            float cy = uz * vx - ux * vz;
            float cz = ux * vy - uy * vx;
            if (cx * cx + cy * cy + cz * cz < 0.0000000001f) return false;
        }
        info = new WorldMeshArtifactInfo(vertices, slots);
        return true;
    }

    public static bool TryReadVertex(ReadOnlySpan<byte> bytes, int index, out WorldMeshVertex vertex)
    {
        vertex = default;
        if (index < 0 || bytes.Length < HeaderBytes ||
            index >= (bytes.Length - HeaderBytes) / VertexBytes)
            return false;
        ReadOnlySpan<byte> data = bytes.Slice(HeaderBytes + index * VertexBytes, VertexBytes);
        float x = ReadFloat(data), y = ReadFloat(data[4..]), z = ReadFloat(data[8..]);
        float nx = ReadFloat(data[12..]), ny = ReadFloat(data[16..]), nz = ReadFloat(data[20..]);
        float u = ReadFloat(data[24..]), v = ReadFloat(data[28..]);
        float r = ReadFloat(data[32..]), g = ReadFloat(data[36..]);
        float b = ReadFloat(data[40..]), a = ReadFloat(data[44..]);
        int slot = BinaryPrimitives.ReadInt32LittleEndian(data[48..]);
        float normalLength = nx * nx + ny * ny + nz * nz;
        if (!Bounded(x, 64) || !Bounded(y, 64) || !Bounded(z, 64) ||
            !Bounded(u, 32) || !Bounded(v, 32) ||
            !float.IsFinite(normalLength) || normalLength is < 0.25f or > 2.25f ||
            !Unit(r) || !Unit(g) || !Unit(b) || !Unit(a) ||
            slot is < 0 or >= MaximumMaterialSlots)
            return false;
        vertex = new WorldMeshVertex(x, y, z, nx, ny, nz, u, v, r, g, b, a, slot);
        return true;
    }

    private static bool Bounded(float value, float absoluteMax) =>
        float.IsFinite(value) && value >= -absoluteMax && value <= absoluteMax;

    private static bool Unit(float value) => float.IsFinite(value) && value is >= 0f and <= 1f;

    private static float ReadFloat(ReadOnlySpan<byte> data) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data));
}

public readonly record struct WorldMeshArtifactInfo(int Vertices, int MaterialSlots)
{
    public int Triangles => Vertices / 3;
}

public readonly record struct WorldMeshVertex(
    float X, float Y, float Z,
    float NormalX, float NormalY, float NormalZ,
    float U, float V,
    float Red, float Green, float Blue, float Alpha,
    int MaterialSlot);
