using System.Collections.Generic;

namespace UltraSim.UI.Schema
{
    /// <summary>
    /// Horizontal row — the primary layout primitive.
    /// Children are arranged left-to-right.
    /// </summary>
    public sealed class UiRowNode : UiContainerNode
    {
        public override UiNodeType NodeType => UiNodeType.Row;

        public UiRowNode(string nodeId, List<UiSchemaNode> children, string? style = null)
            : base(nodeId, children)
        {
            Style = style;
        }
    }

    /// <summary>
    /// Header bar — rendered at the top of the panel.
    /// Typically contains a title Text or Dynamic node.
    /// </summary>
    public sealed class UiHeaderNode : UiContainerNode
    {
        public override UiNodeType NodeType => UiNodeType.Header;

        public UiHeaderNode(string nodeId, List<UiSchemaNode> children, string? style = null)
            : base(nodeId, children)
        {
            Style = style;
        }
    }

    /// <summary>
    /// Footer bar — rendered at the bottom of the panel.
    /// Typically contains action buttons.
    /// </summary>
    public sealed class UiFooterNode : UiContainerNode
    {
        public override UiNodeType NodeType => UiNodeType.Footer;

        public UiFooterNode(string nodeId, List<UiSchemaNode> children, string? style = null)
            : base(nodeId, children)
        {
            Style = style;
        }
    }
}
