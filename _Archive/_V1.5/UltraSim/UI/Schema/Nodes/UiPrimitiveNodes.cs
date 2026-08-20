namespace UltraSim.UI.Schema
{
    /// <summary>
    /// Static text label. Content is baked into the definition and cached.
    /// Never travels the wire again after the initial definition send.
    /// </summary>
    public sealed class UiTextNode : UiSchemaNode
    {
        public override UiNodeType NodeType => UiNodeType.Text;
        public string Text { get; }
        public string? Style { get; }

        public UiTextNode(string nodeId, string text, string? style = null) : base(nodeId)
        {
            Text  = text;
            Style = style;
        }
    }

    /// <summary>
    /// Dynamic value slot. The slot index maps this node to a position in the payload.
    /// The actual value travels in the payload (full or delta), not the definition.
    /// </summary>
    public sealed class UiDynamicNode : UiSchemaNode
    {
        public override UiNodeType NodeType => UiNodeType.Dynamic;

        /// <summary>Index into the panel's ordered slot manifest.</summary>
        public byte SlotIndex { get; }

        /// <summary>Wire type of the value stored in this slot.</summary>
        public UiSlotType SlotType { get; }

        public string? Style  { get; }
        public string? Format { get; }

        public UiDynamicNode(string nodeId, byte slotIndex, UiSlotType slotType,
            string? style = null, string? format = null) : base(nodeId)
        {
            SlotIndex = slotIndex;
            SlotType  = slotType;
            Style     = style;
            Format    = format;
        }
    }

    /// <summary>
    /// Action button. Fires a <see cref="UltraSim.UI.Contracts.UiPanelResponse"/> with the
    /// auto-computed action ID when clicked.
    /// </summary>
    public sealed class UiButtonNode : UiSchemaNode
    {
        public override UiNodeType NodeType => UiNodeType.Button;

        /// <summary>
        /// Stable action ID — FNV-1a hash of the node key.
        /// Use <c>UiPanel.ActionId(key)</c> to reference this in a handler.
        /// </summary>
        public uint ActionId { get; }

        public string? Label { get; }
        public string? Style { get; }

        public UiButtonNode(string nodeId, uint actionId, string? label = null, string? style = null)
            : base(nodeId)
        {
            ActionId = actionId;
            Label    = label;
            Style    = style;
        }
    }

    /// <summary>
    /// Visual separator line between content sections.
    /// </summary>
    public sealed class UiSeparatorNode : UiSchemaNode
    {
        public override UiNodeType NodeType => UiNodeType.Separator;
        public string? Style { get; }

        public UiSeparatorNode(string nodeId, string? style = null) : base(nodeId)
        {
            Style = style;
        }
    }

    /// <summary>
    /// Editable text input field. The user's entered text is packed into the
    /// response payload at the <see cref="FieldIndex"/> position when any button fires.
    /// FieldIndex is separate from display slot indices — it identifies which
    /// text field this is within the response payload (0-based, allocation order).
    /// </summary>
    public sealed class UiTextInputNode : UiSchemaNode
    {
        public override UiNodeType NodeType => UiNodeType.TextInput;

        /// <summary>Field index in the response payload (0-based).</summary>
        public byte FieldIndex { get; }

        /// <summary>If true, input characters are masked (password field).</summary>
        public bool IsPassword { get; }

        /// <summary>Placeholder hint shown when the field is empty.</summary>
        public string? Placeholder { get; }

        /// <summary>Maximum character count (0 = unlimited).</summary>
        public ushort MaxLength { get; }

        public UiTextInputNode(string nodeId, byte fieldIndex,
            bool isPassword = false, string? placeholder = null, ushort maxLength = 0)
            : base(nodeId)
        {
            FieldIndex  = fieldIndex;
            IsPassword  = isPassword;
            Placeholder = placeholder;
            MaxLength   = maxLength;
        }
    }

    /// <summary>
    /// Background decoration layer rendered behind content (panel body, border trim, etc.).
    /// Layers are ordered back-to-front in the definition's node list.
    /// </summary>
    public sealed class UiLayerNode : UiSchemaNode
    {
        public override UiNodeType NodeType => UiNodeType.Layer;
        public string? Style { get; internal set; }
        public byte    Inset { get; internal set; }

        public UiLayerNode(string nodeId) : base(nodeId) { }
    }
}
