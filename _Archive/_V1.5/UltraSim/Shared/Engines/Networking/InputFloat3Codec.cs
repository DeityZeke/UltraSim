using System;
using System.Buffers.Binary;

namespace UltraSim.Shared.Engines.Networking
{
    public static class InputFloat3Codec
    {
        public const int SequenceOffset = 0;
        public const int MoveXOffset = 2;
        public const int MoveZOffset = 4;
        public const int FlagsOffset = 6;
        public const int ClientTickOffset = 7;
        public const int PayloadBytes = 15;

        public static bool TryEncode(
            Span<byte> destination,
            ushort sequence,
            short moveX,
            short moveZ,
            byte flags,
            long clientTick,
            out int writtenBytes)
        {
            if (destination.Length < PayloadBytes)
            {
                writtenBytes = 0;
                return false;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(SequenceOffset, 2), sequence);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(MoveXOffset, 2), moveX);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(MoveZOffset, 2), moveZ);
            destination[FlagsOffset] = flags;
            BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(ClientTickOffset, 8), clientTick);
            writtenBytes = PayloadBytes;
            return true;
        }

        public static bool TryDecode(
            ReadOnlySpan<byte> payload,
            out ushort sequence,
            out short moveX,
            out short moveZ,
            out byte flags,
            out long clientTick)
        {
            if (payload.Length < PayloadBytes)
            {
                sequence = 0;
                moveX = 0;
                moveZ = 0;
                flags = 0;
                clientTick = 0;
                return false;
            }

            sequence = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(SequenceOffset, 2));
            moveX = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(MoveXOffset, 2));
            moveZ = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(MoveZOffset, 2));
            flags = payload[FlagsOffset];
            clientTick = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(ClientTickOffset, 8));
            return true;
        }
    }
}
