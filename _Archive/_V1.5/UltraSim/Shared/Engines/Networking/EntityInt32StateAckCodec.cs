using System;
using System.Buffers.Binary;
using UltraSim.Network;

namespace UltraSim.Shared.Engines.Networking
{
    public static class EntityInt32StateAckCodec
    {
        public const int PayloadBytes = 22;

        public static bool TryEncode(
            Span<byte> destination,
            NetworkEntityId entityId,
            int value,
            long serverTick,
            ushort authoritativeSequence,
            out int writtenBytes)
        {
            if (destination.Length < PayloadBytes)
            {
                writtenBytes = 0;
                return false;
            }

            BinaryPrimitives.WriteUInt64LittleEndian(destination, entityId.Value);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8), value);
            BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(12), serverTick);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(20), authoritativeSequence);
            writtenBytes = PayloadBytes;
            return true;
        }

        public static bool TryDecode(
            ReadOnlySpan<byte> payload,
            out NetworkEntityId entityId,
            out int value,
            out long serverTick,
            out ushort authoritativeSequence)
        {
            if (payload.Length < PayloadBytes)
            {
                entityId = default;
                value = 0;
                serverTick = 0;
                authoritativeSequence = 0;
                return false;
            }

            entityId = new NetworkEntityId(BinaryPrimitives.ReadUInt64LittleEndian(payload));
            value = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8));
            serverTick = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(12));
            authoritativeSequence = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(20));
            return true;
        }
    }
}
