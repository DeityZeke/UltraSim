using System;
using System.Buffers.Binary;
using System.Text;

namespace UltraSim.Network.Protocol
{
    public static class NetworkControlMessageCodec
    {
        public const byte PingProbeMessageType = 0xC1;
        public const byte PongProbeMessageType = 0xC2;
        public const byte ClientChatMessageType = 0xD1;
        public const byte ClientReturnToCharacterSelectMessageType = 0xD3;
        public const byte ServerNameplateMessageType = 0xD7;
        public const int PingProbePayloadBytes = 11;
        public const int PingProbeClientTimestampBytes = 8;
        public const int PingProbeStampedPayloadBytes = PingProbePayloadBytes + PingProbeClientTimestampBytes;
        public const int PongProbePayloadBytes = 11;
        public const int PongProbeServerTimestampBytes = 8;
        public const int PongProbeStampedPayloadBytes = PongProbePayloadBytes + PongProbeServerTimestampBytes;
        public const int ClientChatHeaderBytes = 3;
        public const int ClientReturnToCharacterSelectPayloadBytes = 1;
        public const int ChatMaxUtf8Bytes = 256;
        public const int ServerNameplateHeaderBytes = 11;
        public const int ServerNameplateMaxUtf8Bytes = 128;

        public static bool TryEncodeClientChat(
            Span<byte> destination,
            string text,
            out int written)
        {
            written = 0;
            if (string.IsNullOrWhiteSpace(text) || destination.Length < ClientChatHeaderBytes)
                return false;

            string trimmed = text.Trim();
            int byteCount = Encoding.UTF8.GetByteCount(trimmed);
            if (byteCount <= 0 || byteCount > ChatMaxUtf8Bytes)
                return false;
            if (destination.Length < ClientChatHeaderBytes + byteCount)
                return false;

            destination[0] = ClientChatMessageType;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(1, 2), (ushort)byteCount);
            Encoding.UTF8.GetBytes(trimmed, destination.Slice(ClientChatHeaderBytes, byteCount));
            written = ClientChatHeaderBytes + byteCount;
            return true;
        }

        public static bool TryDecodeClientChat(
            ReadOnlySpan<byte> payload,
            out string text)
        {
            text = string.Empty;
            if (payload.Length < ClientChatHeaderBytes || payload[0] != ClientChatMessageType)
                return false;

            ushort byteCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1, 2));
            if (byteCount == 0 || byteCount > ChatMaxUtf8Bytes)
                return false;
            if (payload.Length < ClientChatHeaderBytes + byteCount)
                return false;

            text = Encoding.UTF8.GetString(payload.Slice(ClientChatHeaderBytes, byteCount));
            return true;
        }

        public static bool TryEncodeReturnToCharacterSelect(Span<byte> destination)
        {
            if (destination.Length < ClientReturnToCharacterSelectPayloadBytes)
                return false;

            destination[0] = ClientReturnToCharacterSelectMessageType;
            return true;
        }

        public static bool TryDecodeReturnToCharacterSelect(ReadOnlySpan<byte> payload)
        {
            return payload.Length >= ClientReturnToCharacterSelectPayloadBytes &&
                   payload[0] == ClientReturnToCharacterSelectMessageType;
        }

        public static bool TryEncodeServerNameplate(
            Span<byte> destination,
            NetworkEntityId entityId,
            string primaryText,
            out int written)
        {
            written = 0;
            if (entityId.Value == 0 || string.IsNullOrWhiteSpace(primaryText))
                return false;

            string trimmed = primaryText.Trim();
            int byteCount = Encoding.UTF8.GetByteCount(trimmed);
            if (byteCount <= 0 || byteCount > ServerNameplateMaxUtf8Bytes)
                return false;
            if (destination.Length < ServerNameplateHeaderBytes + byteCount)
                return false;

            destination[0] = ServerNameplateMessageType;
            BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(1, 8), entityId.Value);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(9, 2), (ushort)byteCount);
            Encoding.UTF8.GetBytes(trimmed, destination.Slice(ServerNameplateHeaderBytes, byteCount));
            written = ServerNameplateHeaderBytes + byteCount;
            return true;
        }

        public static bool TryDecodeServerNameplate(
            ReadOnlySpan<byte> payload,
            out ServerNameplateMessage nameplate)
        {
            nameplate = default;
            if (payload.Length < ServerNameplateHeaderBytes || payload[0] != ServerNameplateMessageType)
                return false;

            ulong rawEntityId = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(1, 8));
            ushort byteCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(9, 2));
            if (rawEntityId == 0 || byteCount == 0 || byteCount > ServerNameplateMaxUtf8Bytes)
                return false;
            if (payload.Length < ServerNameplateHeaderBytes + byteCount)
                return false;

            string primaryText = Encoding.UTF8.GetString(payload.Slice(ServerNameplateHeaderBytes, byteCount));
            if (string.IsNullOrWhiteSpace(primaryText))
                return false;

            nameplate = new ServerNameplateMessage(new NetworkEntityId(rawEntityId), primaryText);
            return true;
        }

        public static bool TryEncodePingProbe(
            Span<byte> destination,
            ushort sequence,
            long clientTick)
        {
            if (destination.Length < PingProbePayloadBytes)
                return false;

            destination[0] = PingProbeMessageType;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(1, 2), sequence);
            BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(3, 8), clientTick);
            return true;
        }

        public static bool TryEncodeStampedPingProbe(
            Span<byte> destination,
            ushort sequence,
            long clientTick,
            long clientTimestampTicks)
        {
            if (destination.Length < PingProbeStampedPayloadBytes)
                return false;

            if (!TryEncodePingProbe(destination, sequence, clientTick))
                return false;

            BinaryPrimitives.WriteInt64LittleEndian(
                destination.Slice(PingProbePayloadBytes, PingProbeClientTimestampBytes),
                clientTimestampTicks);
            return true;
        }

        public static bool TryDecodePingProbe(
            ReadOnlySpan<byte> payload,
            out ushort sequence,
            out long clientTick)
        {
            sequence = 0;
            clientTick = 0;
            if (payload.Length < PingProbePayloadBytes || payload[0] != PingProbeMessageType)
                return false;

            sequence = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1, 2));
            clientTick = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(3, 8));
            return true;
        }

        public static bool TryReadStampedPingProbeClientTimestamp(
            ReadOnlySpan<byte> payload,
            out long clientTimestampTicks)
        {
            clientTimestampTicks = 0;
            if (payload.Length < PingProbeStampedPayloadBytes || payload[0] != PingProbeMessageType)
                return false;

            clientTimestampTicks = BinaryPrimitives.ReadInt64LittleEndian(
                payload.Slice(PingProbePayloadBytes, PingProbeClientTimestampBytes));
            return true;
        }

        public static bool TryEncodePongProbe(
            Span<byte> destination,
            ushort sequence,
            long clientTick)
        {
            if (destination.Length < PongProbePayloadBytes)
                return false;

            destination[0] = PongProbeMessageType;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(1, 2), sequence);
            BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(3, 8), clientTick);
            return true;
        }

        public static bool TryEncodeStampedPongProbe(
            Span<byte> destination,
            ushort sequence,
            long clientTick,
            long serverTimestampTicks)
        {
            if (destination.Length < PongProbeStampedPayloadBytes)
                return false;

            if (!TryEncodePongProbe(destination, sequence, clientTick))
                return false;

            BinaryPrimitives.WriteInt64LittleEndian(
                destination.Slice(PongProbePayloadBytes, PongProbeServerTimestampBytes),
                serverTimestampTicks);
            return true;
        }

        public static bool TryDecodePongProbe(
            ReadOnlySpan<byte> payload,
            out ProbePongMessage pong)
        {
            pong = default;
            if (payload.Length < PongProbePayloadBytes || payload[0] != PongProbeMessageType)
                return false;

            pong = new ProbePongMessage(
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1, 2)),
                BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(3, 8)));
            return true;
        }
    }
}
