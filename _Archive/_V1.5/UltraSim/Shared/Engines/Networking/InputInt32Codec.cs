using System;
using System.Buffers.Binary;

namespace UltraSim.Shared.Engines.Networking
{
    public static class InputInt32Codec
    {
        public const int PayloadBytes = 14;

        public static bool TryEncode(
            Span<byte> destination,
            ushort sequence,
            int value,
            long clientTick,
            out int writtenBytes)
        {
            if (destination.Length < PayloadBytes)
            {
                writtenBytes = 0;
                return false;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(destination, sequence);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(2), value);
            BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(6), clientTick);
            writtenBytes = PayloadBytes;
            return true;
        }

        public static bool TryDecode(
            ReadOnlySpan<byte> payload,
            out ushort sequence,
            out int value,
            out long clientTick)
        {
            if (payload.Length < PayloadBytes)
            {
                sequence = 0;
                value = 0;
                clientTick = 0;
                return false;
            }

            sequence = BinaryPrimitives.ReadUInt16LittleEndian(payload);
            value = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(2));
            clientTick = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(6));
            return true;
        }
    }
}
