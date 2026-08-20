using System;
using System.Collections.Generic;
using System.Text;
using UltraSim.UI.Schema;

namespace UltraSim.UI.Codec
{
    /// <summary>
    /// Reads a binary payload produced by <see cref="UltraSim.UI.Authoring.UiPanel.ExtractPayload"/>
    /// back into an ordered array of boxed slot values.
    ///
    /// The slot manifest from the cached <see cref="UiSchemaDefinition"/> tells the reader
    /// what type each slot holds and in what order. Both sides agreed on this order at
    /// authoring time — the wire carries no per-field tags.
    ///
    /// Output: <c>object?[slotCount]</c> where each element is:
    ///   string  for <see cref="UiSlotType.String"/>
    ///   int     for <see cref="UiSlotType.Int32"/>
    ///   float   for <see cref="UiSlotType.Float32"/>
    ///   bool    for <see cref="UiSlotType.Bool"/>
    ///   null    if the payload was truncated (safe fallback)
    /// </summary>
    public static class UiPayloadReader
    {
        public readonly record struct DecodedPayload(
            object?[] SlotValues,
            IReadOnlyDictionary<byte, UiRepeaterPayloadData> Repeaters);

        /// <summary>
        /// Decodes a full payload into slot values using the definition's slot manifest.
        /// Safe: returns partial results on truncated input rather than throwing.
        /// </summary>
        public static object?[] ReadFull(ReadOnlyMemory<byte> payload, IReadOnlyList<UiSlotDescriptor> slots)
        {
            if (slots.Count == 0)
                return Array.Empty<object?>();

            var values = new object?[slots.Count];
            ReadOnlySpan<byte> span = payload.Span;
            int offset = 0;

            for (int i = 0; i < slots.Count; i++)
            {
                switch (slots[i].SlotType)
                {
                    case UiSlotType.String:
                        values[i] = TryReadString(span, ref offset, out string? s) ? s : null;
                        break;

                    case UiSlotType.Int32:
                        values[i] = TryReadInt32(span, ref offset, out int iv) ? iv : (object?)null;
                        break;

                    case UiSlotType.Float32:
                        values[i] = TryReadFloat32(span, ref offset, out float fv) ? fv : (object?)null;
                        break;

                    case UiSlotType.Bool:
                        values[i] = TryReadBool(span, ref offset, out bool bv) ? bv : (object?)null;
                        break;
                }
            }

            return values;
        }

        public static DecodedPayload ReadPayload(ReadOnlyMemory<byte> payload, UiSchemaDefinition definition)
        {
            object?[] slotValues = ReadFullWithOffset(payload, definition.Slots, out int offset);
            Dictionary<byte, UiRepeaterPayloadData> repeaters = ReadRepeaters(payload.Span, offset, definition.Nodes);
            return new DecodedPayload(slotValues, repeaters);
        }

        /// <summary>
        /// Maximum number of slots supported by the delta update wire format.
        /// The format uses a single uint32 presence bitmask, limiting coverage to 32 slots.
        /// Panels with more than 32 slots must use <see cref="UiPanelUpdateMode.Full"/>.
        /// </summary>
        public const int MaxDeltaSlots = 32;

        /// <summary>
        /// Applies a delta payload on top of an existing slot value array.
        /// Reads a uint bitmask (up to <see cref="MaxDeltaSlots"/> slots), then only the
        /// present slots in order. Slots absent from the delta keep their previous values.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <paramref name="slots"/> has more than <see cref="MaxDeltaSlots"/> entries.
        /// Use <see cref="UiPanelUpdateMode.Full"/> for panels with more than 32 slots.
        /// </exception>
        public static void ApplyDelta(ReadOnlyMemory<byte> payload, IReadOnlyList<UiSlotDescriptor> slots, object?[] current)
        {
            if (slots.Count > MaxDeltaSlots)
                throw new InvalidOperationException(
                    $"Delta updates support at most {MaxDeltaSlots} slots; this panel has {slots.Count}. " +
                    "Use Full update mode for panels with more than 32 slots.");

            if (slots.Count == 0 || payload.Length < 4)
                return;

            ReadOnlySpan<byte> span = payload.Span;
            int offset = 0;

            if (!TryReadUInt32(span, ref offset, out uint mask))
                return;

            for (int i = 0; i < slots.Count; i++)
            {
                if ((mask & (1u << i)) == 0)
                    continue;

                switch (slots[i].SlotType)
                {
                    case UiSlotType.String:
                        if (TryReadString(span, ref offset, out string? s)) current[i] = s;
                        break;

                    case UiSlotType.Int32:
                        if (TryReadInt32(span, ref offset, out int iv)) current[i] = iv;
                        break;

                    case UiSlotType.Float32:
                        if (TryReadFloat32(span, ref offset, out float fv)) current[i] = fv;
                        break;

                    case UiSlotType.Bool:
                        if (TryReadBool(span, ref offset, out bool bv)) current[i] = bv;
                        break;
                }
            }
        }

        // -------------------------------------------------------------------------
        // Primitive readers
        // -------------------------------------------------------------------------

        private static bool TryReadString(ReadOnlySpan<byte> span, ref int offset, out string? value)
        {
            value = null;
            if (offset + 2 > span.Length) return false;

            int len = span[offset] | (span[offset + 1] << 8);
            offset += 2;

            if (len == 0) { value = string.Empty; return true; }
            if (offset + len > span.Length) return false;

            value = Encoding.UTF8.GetString(span.Slice(offset, len));
            offset += len;
            return true;
        }

        private static bool TryReadInt32(ReadOnlySpan<byte> span, ref int offset, out int value)
        {
            value = 0;
            if (offset + 4 > span.Length) return false;
            value = span[offset] | (span[offset+1] << 8) | (span[offset+2] << 16) | (span[offset+3] << 24);
            offset += 4;
            return true;
        }

        private static bool TryReadFloat32(ReadOnlySpan<byte> span, ref int offset, out float value)
        {
            value = 0f;
            if (!TryReadInt32(span, ref offset, out int bits)) return false;
            value = BitConverter.Int32BitsToSingle(bits);
            return true;
        }

        private static bool TryReadBool(ReadOnlySpan<byte> span, ref int offset, out bool value)
        {
            value = false;
            if (offset >= span.Length) return false;
            value = span[offset++] != 0;
            return true;
        }

        private static bool TryReadUInt32(ReadOnlySpan<byte> span, ref int offset, out uint value)
        {
            value = 0;
            if (offset + 4 > span.Length) return false;
            value = (uint)(span[offset] | (span[offset+1] << 8) | (span[offset+2] << 16) | (span[offset+3] << 24));
            offset += 4;
            return true;
        }

        private static bool TryReadByte(ReadOnlySpan<byte> span, ref int offset, out byte value)
        {
            value = 0;
            if (offset >= span.Length)
                return false;

            value = span[offset++];
            return true;
        }

        private static object?[] ReadFullWithOffset(ReadOnlyMemory<byte> payload, IReadOnlyList<UiSlotDescriptor> slots, out int offset)
        {
            if (slots.Count == 0)
            {
                offset = 0;
                return Array.Empty<object?>();
            }

            var values = new object?[slots.Count];
            ReadOnlySpan<byte> span = payload.Span;
            offset = 0;

            for (int i = 0; i < slots.Count; i++)
            {
                switch (slots[i].SlotType)
                {
                    case UiSlotType.String:
                        values[i] = TryReadString(span, ref offset, out string? s) ? s : null;
                        break;
                    case UiSlotType.Int32:
                        values[i] = TryReadInt32(span, ref offset, out int iv) ? iv : (object?)null;
                        break;
                    case UiSlotType.Float32:
                        values[i] = TryReadFloat32(span, ref offset, out float fv) ? fv : (object?)null;
                        break;
                    case UiSlotType.Bool:
                        values[i] = TryReadBool(span, ref offset, out bool bv) ? bv : (object?)null;
                        break;
                }
            }

            return values;
        }

        private static Dictionary<byte, UiRepeaterPayloadData> ReadRepeaters(ReadOnlySpan<byte> span, int offset, IReadOnlyList<UiSchemaNode> nodes)
        {
            var result = new Dictionary<byte, UiRepeaterPayloadData>();
            List<UiRepeaterNode> repeaters = CollectRepeaters(nodes);
            if (repeaters.Count == 0 || offset >= span.Length)
                return result;

            if (!TryReadByte(span, ref offset, out byte repeaterCount))
                return result;

            for (int repeaterIndex = 0; repeaterIndex < repeaterCount; repeaterIndex++)
            {
                if (!TryReadByte(span, ref offset, out byte repeaterId))
                    break;
                if (!TryReadUInt16(span, ref offset, out ushort itemCount))
                    break;

                UiRepeaterNode? repeaterNode = null;
                for (int i = 0; i < repeaters.Count; i++)
                {
                    if (repeaters[i].RepeaterId == repeaterId)
                    {
                        repeaterNode = repeaters[i];
                        break;
                    }
                }

                if (repeaterNode is null)
                    break;

                var items = new List<UiRepeaterItemData>(itemCount);
                for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
                {
                    if (!TryReadUInt64(span, ref offset, out ulong rowKey))
                        return result;

                    object?[] values = new object?[repeaterNode.Slots.Count];
                    for (int slotIndex = 0; slotIndex < repeaterNode.Slots.Count; slotIndex++)
                    {
                        switch (repeaterNode.Slots[slotIndex].SlotType)
                        {
                            case UiSlotType.String:
                                values[slotIndex] = TryReadString(span, ref offset, out string? s) ? s : null;
                                break;
                            case UiSlotType.Int32:
                                values[slotIndex] = TryReadInt32(span, ref offset, out int iv) ? iv : (object?)null;
                                break;
                            case UiSlotType.Float32:
                                values[slotIndex] = TryReadFloat32(span, ref offset, out float fv) ? fv : (object?)null;
                                break;
                            case UiSlotType.Bool:
                                values[slotIndex] = TryReadBool(span, ref offset, out bool bv) ? bv : (object?)null;
                                break;
                        }
                    }

                    items.Add(new UiRepeaterItemData(rowKey, values));
                }

                result[repeaterId] = new UiRepeaterPayloadData(repeaterId, items);
            }

            return result;
        }

        private static List<UiRepeaterNode> CollectRepeaters(IReadOnlyList<UiSchemaNode> nodes)
        {
            var result = new List<UiRepeaterNode>();
            for (int i = 0; i < nodes.Count; i++)
            {
                switch (nodes[i])
                {
                    case UiRepeaterNode repeater:
                        result.Add(repeater);
                        break;
                    case UiContainerNode container:
                        result.AddRange(CollectRepeaters(container.Children));
                        break;
                }
            }

            return result;
        }

        private static bool TryReadUInt16(ReadOnlySpan<byte> span, ref int offset, out ushort value)
        {
            value = 0;
            if (offset + 2 > span.Length)
                return false;

            value = (ushort)(span[offset] | (span[offset + 1] << 8));
            offset += 2;
            return true;
        }

        private static bool TryReadUInt64(ReadOnlySpan<byte> span, ref int offset, out ulong value)
        {
            value = 0;
            if (offset + 8 > span.Length)
                return false;

            value =
                span[offset] |
                ((ulong)span[offset + 1] << 8) |
                ((ulong)span[offset + 2] << 16) |
                ((ulong)span[offset + 3] << 24) |
                ((ulong)span[offset + 4] << 32) |
                ((ulong)span[offset + 5] << 40) |
                ((ulong)span[offset + 6] << 48) |
                ((ulong)span[offset + 7] << 56);
            offset += 8;
            return true;
        }
    }
}
