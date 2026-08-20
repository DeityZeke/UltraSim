using System;
using System.Collections.Generic;
using UltraSim.UI.Contracts;

namespace UltraSim.UI.Schema
{
    /// <summary>
    /// Where the panel appears on screen when opened.
    /// </summary>
    public enum UiPanelPosition : byte
    {
        Center      = 0,
        TopLeft     = 1,
        TopCenter   = 2,
        TopRight    = 3,
        BottomLeft  = 4,
        BottomCenter = 5,
        BottomRight = 6,
        Custom      = 255,  // use explicit x/y from open command
    }

    /// <summary>
    /// Compiled, immutable panel definition produced by <see cref="UltraSim.UI.Authoring.UiPanel"/>.
    ///
    /// Contains:
    ///   - Panel root properties (size, position, style, flags)
    ///   - Ordered list of top-level <see cref="UiSchemaNode"/> objects (the AST)
    ///   - Ordered slot manifest describing each dynamic payload slot
    ///   - Pre-serialized <see cref="DefinitionBytes"/> for the wire
    ///
    /// Identity is (TypeId, Revision). Both must match for a client to use its cached copy.
    /// </summary>
    public sealed class UiSchemaDefinition
    {
        // ---- Identity --------------------------------------------------------

        public UiPanelTypeId TypeId   { get; }
        public ushort        Revision { get; }

        // ---- Root properties -------------------------------------------------

        public ushort          Width    { get; }
        public ushort          Height   { get; }
        public UiPanelPosition Position { get; }
        public string?         Style    { get; }

        /// <summary>bit0=Draggable, bit1=Closable</summary>
        public byte PanelFlags { get; }

        public bool Draggable => (PanelFlags & 0x01) != 0;
        public bool Closable  => (PanelFlags & 0x02) != 0;

        // ---- AST + manifest --------------------------------------------------

        public IReadOnlyList<UiSchemaNode>    Nodes { get; }
        public IReadOnlyList<UiSlotDescriptor> Slots { get; }

        // ---- Wire bytes (pre-serialized) -------------------------------------

        /// <summary>
        /// Binary-encoded definition ready to embed in a
        /// <see cref="UltraSim.UI.Contracts.UiPanelDefinition"/> datagram.
        /// Computed once at construction; never reallocated.
        /// </summary>
        public ReadOnlyMemory<byte> DefinitionBytes { get; }

        // ---- Constructor -----------------------------------------------------

        internal UiSchemaDefinition(
            UiPanelTypeId                typeId,
            ushort                       revision,
            ushort                       width,
            ushort                       height,
            UiPanelPosition              position,
            string?                      style,
            byte                         panelFlags,
            IReadOnlyList<UiSchemaNode>    nodes,
            IReadOnlyList<UiSlotDescriptor> slots,
            ReadOnlyMemory<byte>         definitionBytes)
        {
            TypeId          = typeId;
            Revision        = revision;
            Width           = width;
            Height          = height;
            Position        = position;
            Style           = style;
            PanelFlags      = panelFlags;
            Nodes           = nodes;
            Slots           = slots;
            DefinitionBytes = definitionBytes;
        }

        /// <summary>
        /// Wraps this definition in the <see cref="UiPanelDefinition"/> datagram record
        /// for passing to the network codec.
        /// </summary>
        public UiPanelDefinition ToWireRecord() =>
            new(TypeId, Revision, DefinitionBytes);

        /// <summary>
        /// Returns true if <paramref name="actionId"/> corresponds to a button node
        /// declared anywhere in this panel's schema tree.
        /// Used by the server to reject client responses with unrecognised action IDs.
        /// </summary>
        public bool IsKnownActionId(uint actionId)
            => ContainsActionId(Nodes, actionId);

        /// <summary>
        /// Returns true if <paramref name="fieldIndex"/> corresponds to a text input
        /// node declared anywhere in this panel's schema tree.
        /// Used by the server to reject client-supplied field indices that were never authored.
        /// </summary>
        public bool IsKnownTextField(byte fieldIndex)
            => ContainsTextField(Nodes, fieldIndex);

        private static bool ContainsActionId(IReadOnlyList<UiSchemaNode> nodes, uint actionId)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is UiButtonNode btn && btn.ActionId == actionId)
                    return true;
                if (nodes[i] is UiContainerNode container && ContainsActionId(container.Children, actionId))
                    return true;
                if (nodes[i] is UiRepeaterNode repeater && ContainsActionId(repeater.Children, actionId))
                    return true;
            }
            return false;
        }

        private static bool ContainsTextField(IReadOnlyList<UiSchemaNode> nodes, byte fieldIndex)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is UiTextInputNode input && input.FieldIndex == fieldIndex)
                    return true;
                if (nodes[i] is UiContainerNode container && ContainsTextField(container.Children, fieldIndex))
                    return true;
                if (nodes[i] is UiRepeaterNode repeater && ContainsTextField(repeater.Children, fieldIndex))
                    return true;
            }
            return false;
        }
    }
}
