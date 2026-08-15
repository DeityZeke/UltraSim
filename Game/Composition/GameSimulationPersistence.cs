using System.Buffers.Binary;
using UltraSim.ECS;

namespace UltraSim.Game.Composition;

/// <summary>Direct reader for the pre-N0.6 tick-marker-only simulation payload.</summary>
internal sealed class GameSimulationStateV1Reader : IPersistenceComponentReader<GameSimulationState>
{
    internal static readonly PersistenceSchemaFingerprint Fingerprint =
        new(0xd73d4f0c721df04fUL, 0xb58fc0d39daa8237UL);

    public int EncodedSize => sizeof(int);

    public void Decode(
        ReadOnlySpan<byte> source,
        ref GameSimulationState value,
        PersistenceReadContext context)
    {
        if (source.Length != EncodedSize)
            throw new ArgumentException("Simulation-state v1 payload size mismatch.", nameof(source));
        value.TickMarker = BinaryPrimitives.ReadInt32LittleEndian(source);
        value.InputVelocity = 0;
    }
}
