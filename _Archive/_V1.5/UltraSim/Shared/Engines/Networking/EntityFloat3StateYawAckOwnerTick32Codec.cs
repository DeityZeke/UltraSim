using System;
using System.Buffers.Binary;
using UltraSim.Network;

namespace UltraSim.Shared.Engines.Networking
{
    public static class EntityFloat3StateYawAckOwnerTick32Codec
    {
        public const int EntityIdOffset = 0;
        public const int QuantizedXOffset = 4;
        public const int QuantizedZOffset = 8;
        public const int QuantizedYawOffset = 12;
        public const int ActorDefinitionIdOffset = 14;
        public const int ServerTickOffset = 16;
        public const int AuthoritativeSequenceOffset = 20;
        public const int OwnerConnectionIdOffset = 22;
        public const int PayloadBytes = 24;

        public static bool TryEncode(
            Span<byte> destination,
            NetworkEntityId entityId,
            int quantizedX,
            int quantizedZ,
            ushort quantizedYaw,
            ushort actorDefinitionId,
            long serverTick,
            ushort authoritativeSequence,
            int ownerConnectionId,
            out int writtenBytes)
        {
            if (destination.Length < PayloadBytes ||
                entityId.Value > uint.MaxValue ||
                serverTick < 0 ||
                (ulong)serverTick > uint.MaxValue ||
                (uint)ownerConnectionId > ushort.MaxValue)
            {
                writtenBytes = 0;
                return false;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(EntityIdOffset, 4), (uint)entityId.Value);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(QuantizedXOffset, 4), quantizedX);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(QuantizedZOffset, 4), quantizedZ);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(QuantizedYawOffset, 2), quantizedYaw);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(ActorDefinitionIdOffset, 2), actorDefinitionId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(ServerTickOffset, 4), (uint)serverTick);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(AuthoritativeSequenceOffset, 2), authoritativeSequence);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(OwnerConnectionIdOffset, 2), (ushort)ownerConnectionId);
            writtenBytes = PayloadBytes;
            return true;
        }

        public static bool TryDecode(
            ReadOnlySpan<byte> payload,
            out NetworkEntityId entityId,
            out int quantizedX,
            out int quantizedZ,
            out ushort quantizedYaw,
            out ushort actorDefinitionId,
            out long serverTick,
            out ushort authoritativeSequence,
            out int ownerConnectionId)
        {
            if (payload.Length < PayloadBytes)
            {
                entityId = default;
                quantizedX = 0;
                quantizedZ = 0;
                quantizedYaw = 0;
                actorDefinitionId = 0;
                serverTick = 0;
                authoritativeSequence = 0;
                ownerConnectionId = 0;
                return false;
            }

            entityId = new NetworkEntityId(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(EntityIdOffset, 4)));
            quantizedX = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(QuantizedXOffset, 4));
            quantizedZ = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(QuantizedZOffset, 4));
            quantizedYaw = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(QuantizedYawOffset, 2));
            actorDefinitionId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(ActorDefinitionIdOffset, 2));
            serverTick = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(ServerTickOffset, 4));
            authoritativeSequence = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(AuthoritativeSequenceOffset, 2));
            ownerConnectionId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(OwnerConnectionIdOffset, 2));
            return true;
        }
    }
}
