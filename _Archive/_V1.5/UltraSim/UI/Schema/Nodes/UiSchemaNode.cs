using System.Collections.Generic;

namespace UltraSim.UI.Schema
{
    /// <summary>
    /// Wire type codes for schema node serialization.
    /// Values are stable — do not renumber.
    /// </summary>
    public enum UiNodeType : byte
    {
        Text      = 1,  // static label
        Dynamic   = 2,  // dynamic value slot
        Button    = 3,  // action button
        Row       = 4,  // horizontal container
        Header    = 5,  // header bar container
        Footer    = 6,  // footer bar container
        Layer     = 7,  // background decoration layer
        Separator = 8,  // visual separator
        TextInput = 9,  // editable text field (client → server)
        Repeater  = 10, // repeated row list with per-row action context
    }

    /// <summary>
    /// Base class for all UI schema AST nodes.
    /// </summary>
    public abstract class UiSchemaNode
    {
        /// <summary>Stable string key for this node (tooling + debug only — not evaluated at runtime).</summary>
        public string NodeId { get; }

        /// <summary>Wire type discriminator.</summary>
        public abstract UiNodeType NodeType { get; }

        /// <summary>
        /// Slot index of a bool payload slot that controls visibility.
        /// 0xFF means always visible (no visibility slot bound).
        /// </summary>
        public byte VisibilitySlot { get; internal set; } = 0xFF;

        protected UiSchemaNode(string nodeId)
        {
            NodeId = nodeId;
        }
    }

    /// <summary>
    /// Base class for nodes that contain child nodes (Row, Header, Footer).
    /// </summary>
    public abstract class UiContainerNode : UiSchemaNode
    {
        public IReadOnlyList<UiSchemaNode> Children => _children;
        private readonly List<UiSchemaNode> _children;

        public string? Style { get; internal set; }

        protected UiContainerNode(string nodeId, List<UiSchemaNode> children) : base(nodeId)
        {
            _children = children;
        }

        internal void AddChild(UiSchemaNode node) => _children.Add(node);
    }
}
