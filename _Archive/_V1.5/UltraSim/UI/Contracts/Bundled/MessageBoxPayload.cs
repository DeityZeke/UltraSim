using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UltraSim.UI.Contracts.Bundled
{
    public readonly record struct MessageBoxButtonDefinition(
        uint ActionId,
        string Label,
        bool IsDefault = false,
        bool IsCancel = false);

    public sealed record MessageBoxPayload(
        string Title,
        string Body,
        IReadOnlyList<MessageBoxButtonDefinition> Buttons);

    public static class MessageBoxPayloadCodec
    {
        public static ReadOnlyMemory<byte> Encode(MessageBoxPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);

            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(payload.Title ?? string.Empty);
            writer.Write(payload.Body ?? string.Empty);

            IReadOnlyList<MessageBoxButtonDefinition> buttons = payload.Buttons ?? Array.Empty<MessageBoxButtonDefinition>();
            int count = buttons.Count;
            writer.Write((byte)Math.Clamp(count, 0, byte.MaxValue));
            for (int i = 0; i < count; i++)
            {
                MessageBoxButtonDefinition button = buttons[i];
                writer.Write(button.ActionId);

                byte flags = 0;
                if (button.IsDefault)
                    flags |= 1 << 0;
                if (button.IsCancel)
                    flags |= 1 << 1;
                writer.Write(flags);
                writer.Write(button.Label ?? string.Empty);
            }

            writer.Flush();
            return stream.ToArray();
        }

        public static MessageBoxPayload Decode(ReadOnlyMemory<byte> payload)
        {
            if (payload.IsEmpty)
                return new MessageBoxPayload(string.Empty, string.Empty, Array.Empty<MessageBoxButtonDefinition>());

            using MemoryStream stream = new(payload.ToArray(), writable: false);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);

            string title = reader.ReadString();
            string body = reader.ReadString();
            int count = reader.ReadByte();
            MessageBoxButtonDefinition[] buttons = new MessageBoxButtonDefinition[count];
            for (int i = 0; i < count; i++)
            {
                uint actionId = reader.ReadUInt32();
                byte flags = reader.ReadByte();
                string label = reader.ReadString();
                buttons[i] = new MessageBoxButtonDefinition(
                    actionId,
                    label,
                    IsDefault: (flags & (1 << 0)) != 0,
                    IsCancel: (flags & (1 << 1)) != 0);
            }

            return new MessageBoxPayload(title, body, buttons);
        }
    }
}
