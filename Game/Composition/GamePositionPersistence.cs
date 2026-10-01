using System.Buffers.Binary;
using UltraSim.ECS;
using UltraSim.Game.World;

namespace UltraSim.Game.Composition;

/// <summary>Pre-spatial-space position payloads belonged to DefaultSpace.</summary>
internal sealed class GamePositionV1Reader : IPersistenceComponentReader<WorldPositionComponent>
{
    internal static readonly PersistenceSchemaFingerprint Fingerprint =
        new(0xdfa7bae24098a4acUL, 0xbb42ffd49d0afb04UL);

    public int EncodedSize => 16;

    public void Decode(ReadOnlySpan<byte> source, ref WorldPositionComponent value,
        PersistenceReadContext context)
    {
        if (source.Length != EncodedSize)
            throw new ArgumentException("Position v1 payload size mismatch.", nameof(source));
        value.ChunkX = BinaryPrimitives.ReadInt32LittleEndian(source);
        value.ChunkZ = BinaryPrimitives.ReadInt32LittleEndian(source[4..]);
        value.LocalXMillimeters = BinaryPrimitives.ReadUInt16LittleEndian(source[8..]);
        value.LocalZMillimeters = BinaryPrimitives.ReadUInt16LittleEndian(source[10..]);
        value.YMillimeters = BinaryPrimitives.ReadInt32LittleEndian(source[12..]);
        value.SpatialSpaceValue = GameWorldState.DefaultSpace.Value;
    }
}
