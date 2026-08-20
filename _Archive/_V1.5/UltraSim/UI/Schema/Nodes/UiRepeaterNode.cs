using System.Collections.Generic;

namespace UltraSim.UI.Schema
{
    public sealed class UiRepeaterNode : UiSchemaNode
    {
        private readonly List<UiSchemaNode> _children;
        private readonly List<UiSlotDescriptor> _slots;
        private byte _nextSlotIndex;

        public UiRepeaterNode(string nodeId, byte repeaterId, List<UiSchemaNode> children, List<UiSlotDescriptor> slots, string? style = null, string? rowStyle = null)
            : base(nodeId)
        {
            RepeaterId = repeaterId;
            _children = children;
            _slots = slots;
            Style = style;
            RowStyle = rowStyle;
        }

        public override UiNodeType NodeType => UiNodeType.Repeater;

        public byte RepeaterId { get; }
        public IReadOnlyList<UiSchemaNode> Children => _children;
        public IReadOnlyList<UiSlotDescriptor> Slots => _slots;
        public string? Style { get; internal set; }
        public string? RowStyle { get; internal set; }

        internal void AddChild(UiSchemaNode node) => _children.Add(node);

        internal byte AllocateSlot(UiSlotType slotType)
        {
            byte slotIndex = _nextSlotIndex++;
            _slots.Add(new UiSlotDescriptor(slotIndex, slotType));
            return slotIndex;
        }
    }
}
