using System;
using System.Buffers.Binary;
using System.Text;

namespace UltraSim.UI.Contracts
{
    public static class UiPanelNetworkCodec
    {
        public const byte UiPanelAckMessageType = 0xE1;
        public const byte UiPanelResponseMessageType = 0xE2;
        public const byte UiPanelDefinitionMessageType = 0xE3;
        public const byte UiPanelOpenMessageType = 0xE4;
        public const byte UiPanelUpdateMessageType = 0xE5;
        public const byte UiPanelCloseMessageType = 0xE6;
        /// <summary>Server → client: you are now in game. Wire: [1] msgType [1] detailLen [N] detail UTF-8.</summary>
        public const byte SessionEnterGameMessageType = 0xE7;

        public const int UiPanelAckPayloadBytes = 10;
        public const int UiPanelDefinitionHeaderBytes = 7;
        public const int UiPanelResponseHeaderBytes = 11;
        public const int UiPanelOpenHeaderBytes = 15;
        public const int UiPanelUpdateHeaderBytes = 8;
        public const int UiPanelClosePayloadBytes = 6;

        public static bool TryEncodePanelAck(Span<byte> destination, in UiPanelAck ack)
        {
            if (destination.Length < UiPanelAckPayloadBytes)
                return false;

            destination[0] = UiPanelAckMessageType;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(1, 4), ack.PanelInstanceId.Value);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(5, 2), ack.PanelTypeId.Value);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(7, 2), ack.DefinitionRevision);
            destination[9] = (byte)ack.Status;
            return true;
        }

        public static bool TryDecodePanelAck(ReadOnlySpan<byte> payload, out UiPanelAck ack)
        {
            ack = default;
            if (payload.Length < UiPanelAckPayloadBytes || payload[0] != UiPanelAckMessageType)
                return false;

            ack = new UiPanelAck(
                new UiPanelInstanceId(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4))),
                new UiPanelTypeId(BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(5, 2))),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(7, 2)),
                (UiAckStatus)payload[9]);
            return true;
        }

        public static bool TryEncodePanelDefinition(Span<byte> destination, in UiPanelDefinition definition, out int written)
        {
            written = 0;
            int payloadLength = definition.DefinitionPayload.Length;
            if (payloadLength > ushort.MaxValue)
                return false;
            if (destination.Length < UiPanelDefinitionHeaderBytes + payloadLength)
                return false;

            destination[0] = UiPanelDefinitionMessageType;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(1, 2), definition.PanelTypeId.Value);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(3, 2), definition.DefinitionRevision);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(5, 2), (ushort)payloadLength);
            definition.DefinitionPayload.Span.CopyTo(destination.Slice(UiPanelDefinitionHeaderBytes, payloadLength));
            written = UiPanelDefinitionHeaderBytes + payloadLength;
            return true;
        }

        public static bool TryDecodePanelDefinition(ReadOnlySpan<byte> payload, out UiPanelDefinition definition)
        {
            definition = default;
            if (payload.Length < UiPanelDefinitionHeaderBytes || payload[0] != UiPanelDefinitionMessageType)
                return false;

            ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(5, 2));
            if (payload.Length < UiPanelDefinitionHeaderBytes + payloadLength)
                return false;

            byte[] definitionPayload = new byte[payloadLength];
            payload.Slice(UiPanelDefinitionHeaderBytes, payloadLength).CopyTo(definitionPayload);

            definition = new UiPanelDefinition(
                new UiPanelTypeId(BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1, 2))),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(3, 2)),
                definitionPayload);
            return true;
        }

        public static bool TryEncodePanelResponse(Span<byte> destination, in UiPanelResponse response, out int written)
        {
            written = 0;
            int payloadLength = response.Payload.Length;
            if (payloadLength > ushort.MaxValue)
                return false;
            if (destination.Length < UiPanelResponseHeaderBytes + payloadLength)
                return false;

            destination[0] = UiPanelResponseMessageType;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(1, 4), response.PanelInstanceId.Value);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(5, 4), response.ActionId);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(9, 2), (ushort)payloadLength);
            response.Payload.Span.CopyTo(destination.Slice(UiPanelResponseHeaderBytes, payloadLength));
            written = UiPanelResponseHeaderBytes + payloadLength;
            return true;
        }

        public static bool TryDecodePanelResponse(ReadOnlySpan<byte> payload, out UiPanelResponse response)
        {
            response = default;
            if (payload.Length < UiPanelResponseHeaderBytes || payload[0] != UiPanelResponseMessageType)
                return false;

            ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(9, 2));
            if (payload.Length < UiPanelResponseHeaderBytes + payloadLength)
                return false;

            byte[] responsePayload = new byte[payloadLength];
            payload.Slice(UiPanelResponseHeaderBytes, payloadLength).CopyTo(responsePayload);
            response = new UiPanelResponse(
                new UiPanelInstanceId(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4))),
                BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(5, 4)),
                responsePayload);
            return true;
        }

        public static bool TryEncodePanelOpen(Span<byte> destination, in UiPanelOpenCommand command, out int written)
        {
            written = 0;
            int payloadLength = command.Payload.Length;
            if (payloadLength > ushort.MaxValue)
                return false;
            if (destination.Length < UiPanelOpenHeaderBytes + payloadLength)
                return false;

            destination[0] = UiPanelOpenMessageType;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(1, 4), command.PanelInstanceId.Value);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(5, 2), command.PanelTypeId.Value);
            destination[7] = (byte)command.Category;
            destination[8] = (byte)command.PresentationMode;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(9, 2), command.DefinitionRevision);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(11, 2), (ushort)command.Flags);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(13, 2), (ushort)payloadLength);
            command.Payload.Span.CopyTo(destination.Slice(UiPanelOpenHeaderBytes, payloadLength));
            written = UiPanelOpenHeaderBytes + payloadLength;
            return true;
        }

        public static bool TryDecodePanelOpen(ReadOnlySpan<byte> payload, out UiPanelOpenCommand command)
        {
            command = default;
            if (payload.Length < UiPanelOpenHeaderBytes || payload[0] != UiPanelOpenMessageType)
                return false;

            ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(13, 2));
            if (payload.Length < UiPanelOpenHeaderBytes + payloadLength)
                return false;

            byte[] openPayload = new byte[payloadLength];
            payload.Slice(UiPanelOpenHeaderBytes, payloadLength).CopyTo(openPayload);
            command = new UiPanelOpenCommand(
                new UiPanelInstanceId(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4))),
                new UiPanelTypeId(BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(5, 2))),
                (UiPanelCategory)payload[7],
                (UiPanelPresentationMode)payload[8],
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(9, 2)),
                (UiPanelFlags)BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(11, 2)),
                openPayload);
            return true;
        }

        public static bool TryEncodePanelUpdate(Span<byte> destination, in UiPanelUpdateCommand command, out int written)
        {
            written = 0;
            int payloadLength = command.Payload.Length;
            if (payloadLength > ushort.MaxValue)
                return false;
            if (destination.Length < UiPanelUpdateHeaderBytes + payloadLength)
                return false;

            destination[0] = UiPanelUpdateMessageType;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(1, 4), command.PanelInstanceId.Value);
            destination[5] = (byte)command.UpdateMode;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), (ushort)payloadLength);
            command.Payload.Span.CopyTo(destination.Slice(UiPanelUpdateHeaderBytes, payloadLength));
            written = UiPanelUpdateHeaderBytes + payloadLength;
            return true;
        }

        public static bool TryDecodePanelUpdate(ReadOnlySpan<byte> payload, out UiPanelUpdateCommand command)
        {
            command = default;
            if (payload.Length < UiPanelUpdateHeaderBytes || payload[0] != UiPanelUpdateMessageType)
                return false;

            ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
            if (payload.Length < UiPanelUpdateHeaderBytes + payloadLength)
                return false;

            byte[] updatePayload = new byte[payloadLength];
            payload.Slice(UiPanelUpdateHeaderBytes, payloadLength).CopyTo(updatePayload);
            command = new UiPanelUpdateCommand(
                new UiPanelInstanceId(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4))),
                updatePayload,
                (UiPanelUpdateMode)payload[5]);
            return true;
        }

        public static bool TryEncodePanelClose(Span<byte> destination, in UiPanelCloseCommand command)
        {
            if (destination.Length < UiPanelClosePayloadBytes)
                return false;

            destination[0] = UiPanelCloseMessageType;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(1, 4), command.PanelInstanceId.Value);
            destination[5] = (byte)command.CloseReason;
            return true;
        }

        public static bool TryDecodePanelClose(ReadOnlySpan<byte> payload, out UiPanelCloseCommand command)
        {
            command = default;
            if (payload.Length < UiPanelClosePayloadBytes || payload[0] != UiPanelCloseMessageType)
                return false;

            command = new UiPanelCloseCommand(
                new UiPanelInstanceId(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4))),
                (UiPanelCloseReason)payload[5]);
            return true;
        }

        // -------------------------------------------------------------------------
        // EnterGame (server → client)
        // -------------------------------------------------------------------------

        public static byte[] EncodeSessionEnterGame(string? detail)
        {
            byte[] utf8 = string.IsNullOrEmpty(detail) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(detail);
            int len = Math.Min(utf8.Length, 255);
            byte[] buf = new byte[2 + len];
            buf[0] = SessionEnterGameMessageType;
            buf[1] = (byte)len;
            if (len > 0)
                utf8.AsSpan(0, len).CopyTo(buf.AsSpan(2));
            return buf;
        }

        public static bool TryDecodeSessionEnterGame(ReadOnlySpan<byte> payload, out string detail)
        {
            detail = string.Empty;
            if (payload.Length < 2 || payload[0] != SessionEnterGameMessageType)
                return false;

            int len = payload[1];
            if (payload.Length < 2 + len)
                return false;

            detail = len > 0 ? Encoding.UTF8.GetString(payload.Slice(2, len)) : string.Empty;
            return true;
        }
    }
}
