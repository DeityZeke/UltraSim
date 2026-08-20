using System.Collections.Generic;
using UltraSim.UI.Schema;

namespace UltraSim.UI.Authoring
{
    /// <summary>
    /// Shared builder base for container nodes (Row, Header, Footer).
    /// Methods chain on <typeparamref name="TSelf"/> so each subclass returns its own type.
    /// </summary>
    public abstract class UiContainerBuilder<TSelf> where TSelf : UiContainerBuilder<TSelf>
    {
        protected readonly UiContainerNode _node;
        protected readonly UiPanel         _panel;
        private   int                      _childCounter;

        protected UiContainerBuilder(UiContainerNode node, UiPanel panel)
        {
            _node  = node;
            _panel = panel;
        }

        protected string NextChildId(string hint) => $"{_node.NodeId}.{hint}{_childCounter++}";

        // ---- Shared builder methods ------------------------------------------

        /// <summary>Adds a static text label. Baked into the definition — never resent.</summary>
        public TSelf Text(string text, string? style = null)
        {
            _node.AddChild(new UiTextNode(NextChildId("txt"), text, style));
            return (TSelf)this;
        }

        /// <summary>
        /// Adds a dynamic value slot. The value is inferred from <typeparamref name="T"/>
        /// and written into the payload at the slot index allocated by the panel.
        /// </summary>
        public TSelf Dynamic<T>(T value, string? style = null, string? format = null)
        {
            byte slotIndex = _panel.AllocateSlot(value);
            var slotType   = UiPanel.SlotTypeOf<T>();
            _node.AddChild(new UiDynamicNode(NextChildId("dyn"), slotIndex, slotType, style, format));
            return (TSelf)this;
        }

        /// <summary>Adds an action button with an explicit numeric ID.</summary>
        public TSelf Button(uint id, string? label = null, string? style = null)
        {
            _node.AddChild(new UiButtonNode($"btn{id}", id, label, style));
            return (TSelf)this;
        }

        /// <summary>Adds a text input field inside this container.</summary>
        public TSelf TextInput(string? placeholder = null, bool isPassword = false, ushort maxLength = 0)
        {
            byte fieldIndex = _panel.AllocateTextField();
            _node.AddChild(new UiTextInputNode(NextChildId("input"), fieldIndex, isPassword, placeholder, maxLength));
            return (TSelf)this;
        }

        /// <summary>
        /// Binds visibility of this container to a bool payload slot.
        /// The bool slot must be allocated before calling this (e.g. via a sibling Dynamic call).
        /// </summary>
        public TSelf VisibleWhen(byte slotIndex)
        {
            _node.VisibilitySlot = slotIndex;
            return (TSelf)this;
        }

        /// <summary>Sets the style reference on this container node.</summary>
        public TSelf Style(string style)
        {
            _node.Style = style;
            return (TSelf)this;
        }
    }

    // -------------------------------------------------------------------------
    // Concrete builders
    // -------------------------------------------------------------------------

    /// <summary>Builder for <see cref="UiRowNode"/>.</summary>
    public sealed class UiRowBuilder : UiContainerBuilder<UiRowBuilder>
    {
        internal UiRowBuilder(UiRowNode node, UiPanel panel) : base(node, panel) { }
    }

    /// <summary>Builder for <see cref="UiHeaderNode"/>.</summary>
    public sealed class UiHeaderBuilder : UiContainerBuilder<UiHeaderBuilder>
    {
        internal UiHeaderBuilder(UiHeaderNode node, UiPanel panel) : base(node, panel) { }
    }

    /// <summary>Builder for <see cref="UiFooterNode"/>.</summary>
    public sealed class UiFooterBuilder : UiContainerBuilder<UiFooterBuilder>
    {
        internal UiFooterBuilder(UiFooterNode node, UiPanel panel) : base(node, panel) { }
    }

    /// <summary>
    /// Builder for <see cref="UiLayerNode"/>.
    /// Layers are background decoration only — no children, no payload slots.
    /// </summary>
    public sealed class UiLayerBuilder
    {
        private readonly UiLayerNode _node;

        internal UiLayerBuilder(UiLayerNode node) { _node = node; }

        public UiLayerBuilder Style(string style) { _node.Style = style; return this; }

        /// <summary>Inset in pixels (0–255) from the panel edge.</summary>
        public UiLayerBuilder Inset(byte pixels) { _node.Inset = pixels; return this; }
    }

    public sealed class UiRepeaterBuilder
    {
        private readonly UiRepeaterNode _node;
        private readonly UiPanel _panel;
        private int _childCounter;

        internal UiRepeaterBuilder(UiRepeaterNode node, UiPanel panel)
        {
            _node = node;
            _panel = panel;
        }

        private string NextChildId(string hint) => $"{_node.NodeId}.{hint}{_childCounter++}";

        public UiRepeaterBuilder Text(string text, string? style = null)
        {
            _node.AddChild(new UiTextNode(NextChildId("txt"), text, style));
            return this;
        }

        public UiRepeaterBuilder Dynamic<T>(string? style = null, string? format = null)
        {
            UiSlotType slotType = UiPanel.SlotTypeOf<T>();
            byte slotIndex = _node.AllocateSlot(slotType);
            _node.AddChild(new UiDynamicNode(NextChildId("dyn"), slotIndex, slotType, style, format));
            return this;
        }

        public UiRepeaterBuilder Button(uint id, string? label = null, string? style = null)
        {
            _node.AddChild(new UiButtonNode($"btn{id}", id, label, style));
            return this;
        }

        public UiRepeaterBuilder Style(string style)
        {
            _node.Style = style;
            return this;
        }

        public UiRepeaterBuilder RowStyle(string style)
        {
            _node.RowStyle = style;
            return this;
        }

        public UiRepeaterBuilder AddItem(ulong rowKey, params object?[] values)
        {
            _panel.AddRepeaterItem(_node.RepeaterId, new UiRepeaterItemData(rowKey, values));
            return this;
        }
    }

}
