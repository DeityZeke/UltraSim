using System;
using System.Collections.Generic;
using System.Text;
using UltraSim.UI.Contracts;
using UltraSim.UI.Schema;

namespace UltraSim.UI.Codec
{
    /// <summary>
    /// Binary codec for <see cref="UiSchemaDefinition"/>.
    ///
    /// Wire format (little-endian throughout):
    ///
    ///   Header:
    ///     [1]  format_version  = 1
    ///     [2]  width           (ushort)
    ///     [2]  height          (ushort)
    ///     [1]  position        (byte enum)
    ///     [1]  panel_flags     (byte: bit0=Draggable, bit1=Closable)
    ///     [BS] panel_style     (byte-string: 1-byte length + UTF-8)
    ///
    ///   Slot manifest:
    ///     [1]  slot_count      (byte)
    ///     For each slot:
    ///       [1] slot_type      (byte enum)
    ///
    ///   Node list:
    ///     [2]  node_count      (ushort)
    ///     For each node: [node bytes]
    ///
    ///   Node bytes:
    ///     [1]  node_type       (byte enum)
    ///     [BS] node_id         (byte-string)
    ///     [1]  visibility_slot (byte, 0xFF = none)
    ///     ... type-specific fields ...
    ///
    ///   byte-string: [1] length + [N] UTF-8 bytes (max 255 bytes).
    ///   Text content uses ushort-string: [2] length + [N] UTF-8 bytes (max 65535 bytes).
    /// </summary>
    public static class UiSchemaCodec
    {
        private const byte FormatVersion = 1;

        // -------------------------------------------------------------------------
        // Write
        // -------------------------------------------------------------------------

        /// <summary>
        /// Serializes a <see cref="UiSchemaDefinition"/> into a new byte array.
        /// Called once at definition build time; result is cached on the definition.
        /// </summary>
        public static byte[] Write(
            ushort                         width,
            ushort                         height,
            UiPanelPosition                position,
            string?                        style,
            byte                           panelFlags,
            IReadOnlyList<UiSchemaNode>    nodes,
            IReadOnlyList<UiSlotDescriptor> slots)
        {
            var buf = new List<byte>(256);

            // Header
            buf.Add(FormatVersion);
            WriteUShort(buf, width);
            WriteUShort(buf, height);
            buf.Add((byte)position);
            buf.Add(panelFlags);
            WriteByteString(buf, style);

            // Slot manifest
            buf.Add((byte)slots.Count);
            foreach (var slot in slots)
                buf.Add((byte)slot.SlotType);

            // Node list
            WriteUShort(buf, (ushort)nodes.Count);
            foreach (var node in nodes)
                WriteNode(buf, node);

            return buf.ToArray();
        }

        // -------------------------------------------------------------------------
        // Read
        // -------------------------------------------------------------------------

        /// <summary>
        /// Deserializes a <see cref="UiSchemaDefinition"/> from raw bytes received from the wire.
        /// Returns null if the data is malformed or has an unsupported format version.
        /// </summary>
        public static UiSchemaDefinition? Read(UiPanelTypeId typeId, ushort revision, ReadOnlyMemory<byte> data)
        {
            var span = data.Span;
            int offset = 0;

            if (!TryReadByte(span, ref offset, out byte version) || version != FormatVersion)
                return null;

            if (!TryReadUShort(span, ref offset, out ushort width))   return null;
            if (!TryReadUShort(span, ref offset, out ushort height))  return null;
            if (!TryReadByte(span,   ref offset, out byte pos))       return null;
            if (!TryReadByte(span,   ref offset, out byte flags))     return null;
            if (!TryReadByteString(span, ref offset, out string? style)) return null;

            // Slot manifest
            if (!TryReadByte(span, ref offset, out byte slotCount)) return null;
            var slots = new List<UiSlotDescriptor>(slotCount);
            for (byte i = 0; i < slotCount; i++)
            {
                if (!TryReadByte(span, ref offset, out byte slotTypeByte)) return null;
                slots.Add(new UiSlotDescriptor(i, (UiSlotType)slotTypeByte));
            }

            // Node list
            if (!TryReadUShort(span, ref offset, out ushort nodeCount)) return null;
            var nodes = new List<UiSchemaNode>(nodeCount);
            for (int i = 0; i < nodeCount; i++)
            {
                if (!TryReadNode(span, ref offset, out UiSchemaNode? node) || node is null)
                    return null;
                nodes.Add(node);
            }

            return new UiSchemaDefinition(
                typeId, revision,
                width, height,
                (UiPanelPosition)pos, style, flags,
                nodes, slots,
                data);
        }

        // -------------------------------------------------------------------------
        // Node write helpers
        // -------------------------------------------------------------------------

        private static void WriteNode(List<byte> buf, UiSchemaNode node)
        {
            buf.Add((byte)node.NodeType);
            WriteByteString(buf, node.NodeId);
            buf.Add(node.VisibilitySlot);

            switch (node)
            {
                case UiTextNode t:
                    WriteUShortString(buf, t.Text);
                    WriteByteString(buf, t.Style);
                    break;

                case UiDynamicNode d:
                    buf.Add(d.SlotIndex);
                    buf.Add((byte)d.SlotType);
                    WriteByteString(buf, d.Style);
                    WriteByteString(buf, d.Format);
                    break;

                case UiButtonNode b:
                    WriteUInt(buf, b.ActionId);
                    WriteByteString(buf, b.Label);
                    WriteByteString(buf, b.Style);
                    break;

                case UiContainerNode c:
                    WriteByteString(buf, c.Style);
                    buf.Add((byte)c.Children.Count);
                    foreach (var child in c.Children)
                        WriteNode(buf, child);
                    break;

                case UiRepeaterNode r:
                    buf.Add(r.RepeaterId);
                    WriteByteString(buf, r.Style);
                    WriteByteString(buf, r.RowStyle);
                    buf.Add((byte)r.Slots.Count);
                    foreach (UiSlotDescriptor slot in r.Slots)
                        buf.Add((byte)slot.SlotType);
                    buf.Add((byte)r.Children.Count);
                    foreach (var child in r.Children)
                        WriteNode(buf, child);
                    break;

                case UiLayerNode l:
                    WriteByteString(buf, l.Style);
                    buf.Add(l.Inset);
                    break;

                case UiSeparatorNode s:
                    WriteByteString(buf, s.Style);
                    break;

                case UiTextInputNode ti:
                    buf.Add(ti.FieldIndex);
                    buf.Add(ti.IsPassword ? (byte)1 : (byte)0);
                    WriteByteString(buf, ti.Placeholder);
                    WriteUShort(buf, ti.MaxLength);
                    break;
            }
        }

        // -------------------------------------------------------------------------
        // Node read helpers
        // -------------------------------------------------------------------------

        private static bool TryReadNode(ReadOnlySpan<byte> span, ref int offset, out UiSchemaNode? node)
        {
            node = null;

            if (!TryReadByte(span, ref offset, out byte typeByte))        return false;
            if (!TryReadByteString(span, ref offset, out string? nodeId)) return false;
            if (!TryReadByte(span, ref offset, out byte visSlot))         return false;

            nodeId ??= string.Empty;

            switch ((UiNodeType)typeByte)
            {
                case UiNodeType.Text:
                {
                    if (!TryReadUShortString(span, ref offset, out string? text)) return false;
                    if (!TryReadByteString(span, ref offset, out string? style))  return false;
                    node = new UiTextNode(nodeId, text ?? string.Empty, style) { VisibilitySlot = visSlot };
                    return true;
                }

                case UiNodeType.Dynamic:
                {
                    if (!TryReadByte(span, ref offset, out byte slotIndex))      return false;
                    if (!TryReadByte(span, ref offset, out byte slotTypeByte))   return false;
                    if (!TryReadByteString(span, ref offset, out string? style)) return false;
                    if (!TryReadByteString(span, ref offset, out string? format)) return false;
                    node = new UiDynamicNode(nodeId, slotIndex, (UiSlotType)slotTypeByte, style, format) { VisibilitySlot = visSlot };
                    return true;
                }

                case UiNodeType.Button:
                {
                    if (!TryReadUInt(span, ref offset, out uint actionId))        return false;
                    if (!TryReadByteString(span, ref offset, out string? label))  return false;
                    if (!TryReadByteString(span, ref offset, out string? style))  return false;
                    node = new UiButtonNode(nodeId, actionId, label, style) { VisibilitySlot = visSlot };
                    return true;
                }

                case UiNodeType.Row:
                case UiNodeType.Header:
                case UiNodeType.Footer:
                {
                    if (!TryReadByteString(span, ref offset, out string? style)) return false;
                    if (!TryReadByte(span, ref offset, out byte childCount))     return false;
                    var children = new List<UiSchemaNode>(childCount);
                    for (int i = 0; i < childCount; i++)
                    {
                        if (!TryReadNode(span, ref offset, out UiSchemaNode? child) || child is null)
                            return false;
                        children.Add(child);
                    }
                    node = (UiNodeType)typeByte switch
                    {
                        UiNodeType.Row    => new UiRowNode(nodeId, children, style)    { VisibilitySlot = visSlot },
                        UiNodeType.Header => new UiHeaderNode(nodeId, children, style) { VisibilitySlot = visSlot },
                        UiNodeType.Footer => new UiFooterNode(nodeId, children, style) { VisibilitySlot = visSlot },
                        _                 => null,
                    };
                    return node is not null;
                }

                case UiNodeType.Repeater:
                {
                    if (!TryReadByte(span, ref offset, out byte repeaterId)) return false;
                    if (!TryReadByteString(span, ref offset, out string? style)) return false;
                    if (!TryReadByteString(span, ref offset, out string? rowStyle)) return false;
                    if (!TryReadByte(span, ref offset, out byte slotCount)) return false;
                    var slots = new List<UiSlotDescriptor>(slotCount);
                    for (byte i = 0; i < slotCount; i++)
                    {
                        if (!TryReadByte(span, ref offset, out byte slotTypeByte)) return false;
                        slots.Add(new UiSlotDescriptor(i, (UiSlotType)slotTypeByte));
                    }

                    if (!TryReadByte(span, ref offset, out byte childCount)) return false;
                    var children = new List<UiSchemaNode>(childCount);
                    for (int i = 0; i < childCount; i++)
                    {
                        if (!TryReadNode(span, ref offset, out UiSchemaNode? child) || child is null)
                            return false;
                        children.Add(child);
                    }

                    node = new UiRepeaterNode(nodeId, repeaterId, children, slots, style, rowStyle)
                    {
                        VisibilitySlot = visSlot
                    };
                    return true;
                }

                case UiNodeType.Layer:
                {
                    if (!TryReadByteString(span, ref offset, out string? style)) return false;
                    if (!TryReadByte(span, ref offset, out byte inset))          return false;
                    node = new UiLayerNode(nodeId) { Style = style, Inset = inset, VisibilitySlot = visSlot };
                    return true;
                }

                case UiNodeType.Separator:
                {
                    if (!TryReadByteString(span, ref offset, out string? style)) return false;
                    node = new UiSeparatorNode(nodeId, style) { VisibilitySlot = visSlot };
                    return true;
                }

                case UiNodeType.TextInput:
                {
                    if (!TryReadByte(span, ref offset, out byte fieldIndex))           return false;
                    if (!TryReadByte(span, ref offset, out byte isPasswordByte))       return false;
                    if (!TryReadByteString(span, ref offset, out string? placeholder)) return false;
                    if (!TryReadUShort(span, ref offset, out ushort maxLength))        return false;
                    node = new UiTextInputNode(nodeId, fieldIndex,
                        isPassword: isPasswordByte != 0, placeholder, maxLength)
                        { VisibilitySlot = visSlot };
                    return true;
                }

                default:
                    return false; // unknown node type — malformed
            }
        }

        // -------------------------------------------------------------------------
        // Primitive write helpers
        // -------------------------------------------------------------------------

        private static void WriteUShort(List<byte> buf, ushort v)
        {
            buf.Add((byte)v);
            buf.Add((byte)(v >> 8));
        }

        private static void WriteUInt(List<byte> buf, uint v)
        {
            buf.Add((byte)v);
            buf.Add((byte)(v >> 8));
            buf.Add((byte)(v >> 16));
            buf.Add((byte)(v >> 24));
        }

        /// <summary>byte-string: 1-byte length prefix + UTF-8 (max 255 encoded bytes; truncated if longer).</summary>
        private static void WriteByteString(List<byte> buf, string? s)
        {
            if (string.IsNullOrEmpty(s)) { buf.Add(0); return; }
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            int len = Math.Min(bytes.Length, 255);
            buf.Add((byte)len);
            buf.AddRange(new ReadOnlySpan<byte>(bytes, 0, len).ToArray());
        }

        /// <summary>ushort-string: 2-byte length prefix + UTF-8 (max 65535 encoded bytes).</summary>
        private static void WriteUShortString(List<byte> buf, string? s)
        {
            if (string.IsNullOrEmpty(s)) { WriteUShort(buf, 0); return; }
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            int len = Math.Min(bytes.Length, ushort.MaxValue);
            WriteUShort(buf, (ushort)len);
            buf.AddRange(new ReadOnlySpan<byte>(bytes, 0, len).ToArray());
        }

        // -------------------------------------------------------------------------
        // Primitive read helpers
        // -------------------------------------------------------------------------

        private static bool TryReadByte(ReadOnlySpan<byte> span, ref int offset, out byte value)
        {
            if (offset >= span.Length) { value = 0; return false; }
            value = span[offset++];
            return true;
        }

        private static bool TryReadUShort(ReadOnlySpan<byte> span, ref int offset, out ushort value)
        {
            value = 0;
            if (offset + 2 > span.Length) return false;
            value = (ushort)(span[offset] | (span[offset + 1] << 8));
            offset += 2;
            return true;
        }

        private static bool TryReadUInt(ReadOnlySpan<byte> span, ref int offset, out uint value)
        {
            value = 0;
            if (offset + 4 > span.Length) return false;
            value = (uint)(span[offset] | (span[offset+1] << 8) | (span[offset+2] << 16) | (span[offset+3] << 24));
            offset += 4;
            return true;
        }

        private static bool TryReadByteString(ReadOnlySpan<byte> span, ref int offset, out string? value)
        {
            value = null;
            if (!TryReadByte(span, ref offset, out byte len)) return false;
            if (len == 0) { value = string.Empty; return true; }
            if (offset + len > span.Length) return false;
            value = Encoding.UTF8.GetString(span.Slice(offset, len));
            offset += len;
            return true;
        }

        private static bool TryReadUShortString(ReadOnlySpan<byte> span, ref int offset, out string? value)
        {
            value = null;
            if (!TryReadUShort(span, ref offset, out ushort len)) return false;
            if (len == 0) { value = string.Empty; return true; }
            if (offset + len > span.Length) return false;
            value = Encoding.UTF8.GetString(span.Slice(offset, len));
            offset += len;
            return true;
        }
    }
}
