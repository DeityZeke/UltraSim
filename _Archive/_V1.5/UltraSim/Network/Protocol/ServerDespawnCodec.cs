using System;
using System.Buffers.Binary;

namespace UltraSim.Network.Protocol
{
    public static class ServerDespawnCodec
    {
        public const byte MessageType = 0xD3;
        public const int PayloadBytes = 9;

        public static bool TryEncode(Span<byte> destination, NetworkEntityId entityId, out int written)
        {
            if (destination.Length < PayloadBytes || entityId.Value == 0)
            {
                written = 0;
                return false;
            }

            destination[0] = MessageType;
            BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(1, 8), entityId.Value);
            written = PayloadBytes;
            return true;
        }

        public static bool TryDecode(ReadOnlySpan<byte> payload, out NetworkEntityId entityId)
        {
            entityId = default;
            if (payload.Length < PayloadBytes || payload[0] != MessageType)
                return false;

            ulong rawEntityId = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(1, 8));
            if (rawEntityId == 0)
                return false;

            entityId = new NetworkEntityId(rawEntityId);
            return true;
        }
    }
}
