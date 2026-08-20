using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using UltraSim.Network;
using UltraSim.Network.Protocol;
using UltraSim.UI.Contracts;

namespace UltraSim.Client.Engines.Networking
{
    public static class EntityFloat3InboundMessageCodec
    {
        public const byte ChatScopeLocal = 0;
        public const byte ChatScopeSystem = 1;

        private const byte ServerDespawnBatchMessageType = 0xD4;
        private const int ServerDespawnBatchHeaderBytes = 3;

        public static void DecodeDatagram(
            long sessionId,
            in InboundDatagram datagram,
            NetworkInboundCommandQueue<EntityFloat3InboundMessage> queue)
        {
            ReadOnlySpan<byte> payload = datagram.Payload.Span;
            if (payload.IsEmpty)
                return;

            switch (datagram.Channel)
            {
                case ChannelType.UnreliableSequenced:
                    if (TryDecodeProbePong(payload, out ProbePongMessage unreliablePong))
                    {
                        queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForProbePong(in unreliablePong));
                        return;
                    }
                    DecodeCorrectionDatagram(sessionId, payload, queue);
                    break;
                case ChannelType.ReliableOrdered:
                    if (TryDecodeProbePong(payload, out ProbePongMessage reliablePong))
                    {
                        queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForProbePong(in reliablePong));
                        return;
                    }
                    DecodeReliableDatagram(sessionId, payload, queue);
                    break;
            }
        }

        private static void DecodeCorrectionDatagram(
            long sessionId,
            ReadOnlySpan<byte> payload,
            NetworkInboundCommandQueue<EntityFloat3InboundMessage> queue)
        {
            if (TryDecodeCorrectionBatch(sessionId, payload, queue))
                return;

            if (!EntityFloat3CorrectionCodec.TryDecodeSingle(
                    payload,
                    out EntityFloat3Correction correction,
                    out long serverTick,
                    out ushort authoritativeSequence))
            {
                return;
            }

            var inbound = new ClientInboundCorrection<EntityFloat3Correction>(
                authoritativeSequence,
                serverTick,
                in correction);
            queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForCorrection(in inbound));
        }

        private static bool TryDecodeCorrectionBatch(
            long sessionId,
            ReadOnlySpan<byte> payload,
            NetworkInboundCommandQueue<EntityFloat3InboundMessage> queue)
        {
            if (payload.Length <= EntityFloat3CorrectionCodec.OwnerPayloadBytes ||
                payload.Length == EntityFloat3CorrectionCodec.ObserverPayloadBytes ||
                payload[0] != EntityFloat3CorrectionCodec.BatchMessageType ||
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

                if (!EntityFloat3CorrectionCodec.TryDecodeSingle(
                        payload.Slice(offset, entryLength),
                        out EntityFloat3Correction correction,
                        out long serverTick,
                        out ushort authoritativeSequence))
                {
                    return false;
                }

                var inbound = new ClientInboundCorrection<EntityFloat3Correction>(
                    authoritativeSequence,
                    serverTick,
                    in correction);
                queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForCorrection(in inbound));
                offset += entryLength;
            }

            return offset == payload.Length;
        }

        private static void DecodeReliableDatagram(
            long sessionId,
            ReadOnlySpan<byte> payload,
            NetworkInboundCommandQueue<EntityFloat3InboundMessage> queue)
        {
            if (UiPanelNetworkCodec.TryDecodePanelOpen(payload, out UiPanelOpenCommand open))
            {
                queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForUiPanelOpen(in open));
                return;
            }

            if (UiPanelNetworkCodec.TryDecodePanelUpdate(payload, out UiPanelUpdateCommand update))
            {
                queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForUiPanelUpdate(in update));
                return;
            }

            if (UiPanelNetworkCodec.TryDecodePanelClose(payload, out UiPanelCloseCommand close))
            {
                queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForUiPanelClose(in close));
                return;
            }

            if (UiPanelNetworkCodec.TryDecodePanelDefinition(payload, out UiPanelDefinition definition))
            {
                queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForUiPanelDefinition(in definition));
                return;
            }

            if (TryDecodeChatBatch(sessionId, payload, queue))
                return;

            if (TryDecodeDespawnBatch(sessionId, payload, queue))
                return;

            if (TryDecodeChat(payload, out ServerChatMessage chat))
            {
                queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForChat(in chat));
                return;
            }

            if (NetworkControlMessageCodec.TryDecodeServerNameplate(payload, out ServerNameplateMessage nameplate))
            {
                queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForNameplate(in nameplate));
                return;
            }

            if (TryDecodeDespawn(payload, out NetworkEntityId entityId))
                queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForDespawn(entityId));

            if (UiPanelNetworkCodec.TryDecodeSessionEnterGame(payload, out string enterGameDetail))
            {
                queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForEnterGame(enterGameDetail));
                return;
            }

            if (LoginPacketCodec.TryDecodeLoginResponse(
                    payload,
                    out UiPanelInstanceId loginInstanceId,
                    out LoginPacketCodec.LoginResponseResult loginResult,
                    out ReadOnlySpan<byte> newChallengeSpan,
                    out string loginDetail))
            {
                // Copy the challenge span to a heap array so it survives beyond this stack frame.
                byte[] newChallengeBytes = newChallengeSpan.ToArray();
                var loginResponse = new LoginResponsePayload(
                    loginInstanceId, loginResult,
                    new System.ReadOnlyMemory<byte>(newChallengeBytes),
                    loginDetail);
                queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForLoginResponse(in loginResponse));
            }
        }

        private static bool TryDecodeChat(
            ReadOnlySpan<byte> payload,
            out ServerChatMessage chat)
            => ServerChatCodec.TryDecodeChat(payload, out chat);

        private static bool TryDecodeChatBatch(
            long sessionId,
            ReadOnlySpan<byte> payload,
            NetworkInboundCommandQueue<EntityFloat3InboundMessage> queue)
        {
            if (!ServerChatCodec.TryDecodeChatBatch(payload, out ReadOnlySpan<byte> batchPayload, out ushort count))
                return false;
            int decodedCount = 0;
            while (batchPayload.Length >= 2 && decodedCount < count)
            {
                ushort itemBytes = BinaryPrimitives.ReadUInt16LittleEndian(batchPayload.Slice(0, 2));
                batchPayload = batchPayload.Slice(2);
                if (itemBytes == 0 || batchPayload.Length < itemBytes)
                    return false;

                if (!TryDecodeChat(batchPayload.Slice(0, itemBytes), out ServerChatMessage chat))
                    return false;

                queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForChat(in chat));
                batchPayload = batchPayload.Slice(itemBytes);
                decodedCount++;
            }

            return decodedCount == count && batchPayload.IsEmpty;
        }

        private static bool TryDecodeDespawn(
            ReadOnlySpan<byte> payload,
            out NetworkEntityId entityId) =>
            ServerDespawnCodec.TryDecode(payload, out entityId);

        private static bool TryDecodeDespawnBatch(
            long sessionId,
            ReadOnlySpan<byte> payload,
            NetworkInboundCommandQueue<EntityFloat3InboundMessage> queue)
        {
            if (payload.Length < ServerDespawnBatchHeaderBytes || payload[0] != ServerDespawnBatchMessageType)
                return false;

            ushort count = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1, 2));
            if (count == 0)
                return false;

            int bodyBytes = count * sizeof(ulong);
            if (payload.Length < ServerDespawnBatchHeaderBytes + bodyBytes)
                return false;

            ReadOnlySpan<ulong> entityIds = MemoryMarshal.Cast<byte, ulong>(
                payload.Slice(ServerDespawnBatchHeaderBytes, bodyBytes));
            if (entityIds.Length != count)
                return false;

            for (int i = 0; i < entityIds.Length; i++)
            {
                ulong rawEntityId = entityIds[i];
                if (rawEntityId == 0)
                    return false;

                queue.Enqueue(sessionId, EntityFloat3InboundMessage.ForDespawn(new NetworkEntityId(rawEntityId)));
            }

            return true;
        }

        private static bool TryDecodeProbePong(ReadOnlySpan<byte> payload, out ProbePongMessage pong) =>
            NetworkControlMessageCodec.TryDecodePongProbe(payload, out pong);
    }
}
