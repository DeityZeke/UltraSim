using System;
using System.Buffers.Binary;
using UltraSim.Network;
using UltraSim.Shared.Engines.Networking;

namespace UltraSim.Client.Engines.Networking
{
    public static class EntityFloat3CorrectionCodec
    {
        public const byte BatchMessageType = 0xD6;
        public const int OwnerPayloadBytes = EntityFloat3StateYawAckOwnerTick32Codec.PayloadBytes;
        public const int ObserverPayloadBytes = EntityFloat3StateYawTick32Codec.PayloadBytes;

        public static void DecodeDatagram(
            long sessionId,
            in InboundDatagram datagram,
            NetworkInboundCommandQueue<ClientInboundCorrection<EntityFloat3Correction>> queue)
        {
            if (datagram.Channel != ChannelType.UnreliableSequenced)
                return;

            ReadOnlySpan<byte> payload = datagram.Payload.Span;
            if (TryDecodeBatch(sessionId, payload, queue))
                return;

            if (!TryDecodeSingle(payload, out EntityFloat3Correction correction, out long serverTick, out ushort authoritativeSequence))
                return;

            queue.Enqueue(sessionId, new ClientInboundCorrection<EntityFloat3Correction>(
                authoritativeSequence,
                serverTick,
                in correction));
        }

        public static bool TryDecodeSingle(
            ReadOnlySpan<byte> payload,
            out EntityFloat3Correction correction,
            out long serverTick,
            out ushort authoritativeSequence)
        {
            if (payload.Length >= OwnerPayloadBytes &&
                EntityFloat3StateYawAckOwnerTick32Codec.TryDecode(
                    payload,
                    out NetworkEntityId entityId,
                    out int quantizedX,
                    out int quantizedZ,
                    out ushort quantizedYaw,
                    out ushort actorDefinitionId,
                    out serverTick,
                    out authoritativeSequence,
                    out int ownerConnectionId))
            {
                correction = new EntityFloat3Correction(
                    entityId,
                    MovementQuantization.DequantizePosition(quantizedX),
                    0.0f,
                    MovementQuantization.DequantizePosition(quantizedZ),
                    MovementQuantization.DequantizeYaw(quantizedYaw),
                    actorDefinitionId,
                    ownerConnectionId);
                return true;
            }

            if (payload.Length >= ObserverPayloadBytes &&
                EntityFloat3StateYawTick32Codec.TryDecode(
                    payload,
                    out NetworkEntityId observerEntityId,
                    out int observerQuantizedX,
                    out int observerQuantizedZ,
                    out ushort observerQuantizedYaw,
                    out ushort observerActorDefinitionId,
                    out serverTick))
            {
                authoritativeSequence = 0;
                correction = new EntityFloat3Correction(
                    observerEntityId,
                    MovementQuantization.DequantizePosition(observerQuantizedX),
                    0.0f,
                    MovementQuantization.DequantizePosition(observerQuantizedZ),
                    MovementQuantization.DequantizeYaw(observerQuantizedYaw),
                    observerActorDefinitionId,
                    0);
                return true;
            }

            correction = default;
            serverTick = 0;
            authoritativeSequence = 0;
            return false;
        }

        public static bool TryDecodeBatch(
            long sessionId,
            ReadOnlySpan<byte> payload,
            NetworkInboundCommandQueue<ClientInboundCorrection<EntityFloat3Correction>> queue)
        {
            if (payload.Length <= OwnerPayloadBytes ||
                payload.Length == ObserverPayloadBytes ||
                payload[0] != BatchMessageType ||
                payload.Length < 3)
            {
                return false;
            }

            int count = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1, 2));
            int offset = 3;
            for (int i = 0; i < count; i++)
            {
                if (offset + 2 > payload.Length)
                    return false;

                int entryLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset, 2));
                offset += 2;
                if (entryLength <= 0 || offset + entryLength > payload.Length)
                    return false;

                if (!TryDecodeSingle(
                    payload.Slice(offset, entryLength),
                    out EntityFloat3Correction correction,
                    out long serverTick,
                    out ushort authoritativeSequence))
                {
                    return false;
                }

                queue.Enqueue(sessionId, new ClientInboundCorrection<EntityFloat3Correction>(
                    authoritativeSequence,
                    serverTick,
                    in correction));
                offset += entryLength;
            }

            return offset == payload.Length;
        }
    }
}
