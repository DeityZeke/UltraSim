using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace UltraSim.Network.Protocol
{
    public static class ServerChatCodec
    {
        public const byte ServerChatMessageType = 0xD2;
        public const byte ServerChatBatchMessageType = 0xD5;
        public const byte ChatScopeLocal = 0;
        public const byte ChatScopeSystem = 1;
        public const int ServerChatHeaderBytes = 12;
        public const int ServerChatBatchHeaderBytes = 3;
        public const int ChatMaxUtf8Bytes = 256;

        public static bool TryEncodeChat(
            Span<byte> destination,
            byte scope,
            ulong senderEntityId,
            string text,
            out int written)
        {
            written = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (destination.Length < ServerChatHeaderBytes)
                return false;

            string trimmed = text.Trim();
            int byteCount = Encoding.UTF8.GetByteCount(trimmed);
            if (byteCount <= 0 || byteCount > ChatMaxUtf8Bytes)
                return false;
            if (destination.Length < ServerChatHeaderBytes + byteCount)
                return false;

            destination[0] = ServerChatMessageType;
            destination[1] = scope;
            BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(2, 8), senderEntityId);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(10, 2), (ushort)byteCount);
            Encoding.UTF8.GetBytes(trimmed, destination.Slice(ServerChatHeaderBytes, byteCount));
            written = ServerChatHeaderBytes + byteCount;
            return true;
        }

        public static bool TryDecodeChat(
            ReadOnlySpan<byte> payload,
            out ServerChatMessage chat)
        {
            chat = default;
            if (payload.Length < ServerChatHeaderBytes || payload[0] != ServerChatMessageType)
                return false;

            byte scope = payload[1];
            ulong senderEntityId = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(2, 8));
            ushort byteCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
            if (byteCount == 0 || byteCount > ChatMaxUtf8Bytes)
                return false;
            if (payload.Length < ServerChatHeaderBytes + byteCount)
                return false;

            string text = Encoding.UTF8.GetString(payload.Slice(ServerChatHeaderBytes, byteCount));
            chat = new ServerChatMessage(scope, new NetworkEntityId(senderEntityId), text);
            return true;
        }

        public static bool TryEncodeChatBatch(
            Span<byte> destination,
            List<byte[]> payloads,
            out int written)
        {
            written = 0;
            int count = payloads.Count;
            if (count <= 0 || count > ushort.MaxValue)
                return false;

            int required = ServerChatBatchHeaderBytes;
            for (int i = 0; i < count; i++)
            {
                byte[] payload = payloads[i];
                if (payload.Length == 0 || payload.Length > ushort.MaxValue)
                    return false;
                required += 2 + payload.Length;
            }

            if (destination.Length < required)
                return false;

            destination[0] = ServerChatBatchMessageType;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(1, 2), (ushort)count);
            int offset = ServerChatBatchHeaderBytes;
            for (int i = 0; i < count; i++)
            {
                byte[] payload = payloads[i];
                BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, 2), (ushort)payload.Length);
                offset += 2;
                payload.AsSpan().CopyTo(destination.Slice(offset, payload.Length));
                offset += payload.Length;
            }

            written = offset;
            return true;
        }

        public static bool TryDecodeChatBatch(
            ReadOnlySpan<byte> payload,
            out ReadOnlySpan<byte> batchPayload,
            out ushort count)
        {
            batchPayload = default;
            count = 0;
            if (payload.Length < ServerChatBatchHeaderBytes || payload[0] != ServerChatBatchMessageType)
                return false;

            count = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1, 2));
            if (count == 0)
                return false;

            batchPayload = payload.Slice(ServerChatBatchHeaderBytes);
            return true;
        }
    }
}
