using System.Collections.Generic;
using Godot;
using UltraSim.UI.Schema;

namespace Client.Godot.UI.ServerDriven.Renderer
{
    /// <summary>
    /// Holds a built Godot control tree for a schema-driven panel instance,
    /// along with the slot → label/control mappings needed for payload updates.
    /// </summary>
    public sealed class RenderedPanel
    {
        /// <summary>Root control to add as a child of the panel host.</summary>
        public Control Root { get; }

        /// <summary>
        /// Labels whose text is driven by a dynamic payload slot.
        /// Key = slot index, Value = the Label to update.
        /// </summary>
        public Dictionary<byte, Label> DynamicLabels { get; } = new();

        /// <summary>
        /// Controls whose visibility is driven by a bool payload slot.
        /// Key = slot index, Value = the Control to show/hide.
        /// </summary>
        public Dictionary<byte, Control> VisibilityControls { get; } = new();

        /// <summary>
        /// Text input fields whose values are packed into the response payload on submit.
        /// Key = field index (from UiTextInputNode.FieldIndex), Value = the LineEdit control.
        /// </summary>
        public Dictionary<byte, LineEdit> TextInputs { get; } = new();

        public Dictionary<byte, RepeaterBinding> Repeaters { get; } = new();

        public RenderedPanel(Control root)
        {
            Root = root;
        }

        /// <summary>
        /// Applies slot values to all tracked dynamic labels and visibility controls.
        /// </summary>
        public void ApplySlots(object?[] slotValues, IReadOnlyDictionary<byte, UiRepeaterPayloadData>? repeaters = null)
        {
            foreach ((byte slotIndex, Label label) in DynamicLabels)
            {
                if (slotIndex >= slotValues.Length) continue;
                label.Text = slotValues[slotIndex]?.ToString() ?? string.Empty;
            }

            foreach ((byte slotIndex, Control ctrl) in VisibilityControls)
            {
                if (slotIndex >= slotValues.Length) continue;
                ctrl.Visible = slotValues[slotIndex] is bool b && b;
            }

            foreach ((byte repeaterId, RepeaterBinding binding) in Repeaters)
            {
                UiRepeaterPayloadData? repeaterData = null;
                repeaters?.TryGetValue(repeaterId, out repeaterData);
                binding.Apply(repeaterData);
            }
        }

        public sealed class RepeaterBinding
        {
            private readonly UiRepeaterNode _node;
            private readonly VBoxContainer _container;
            private readonly System.Action<uint, ulong?> _onAction;

            public RepeaterBinding(UiRepeaterNode node, VBoxContainer container, System.Action<uint, ulong?> onAction)
            {
                _node = node;
                _container = container;
                _onAction = onAction;
            }

            public void Apply(UiRepeaterPayloadData? data)
            {
                for (int i = _container.GetChildCount() - 1; i >= 0; i--)
                    _container.GetChild(i).QueueFree();

                if (data is null)
                    return;

                for (int itemIndex = 0; itemIndex < data.Items.Count; itemIndex++)
                {
                    UiRepeaterItemData item = data.Items[itemIndex];
                    var row = new HBoxContainer
                    {
                        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                    };
                    row.AddThemeConstantOverride("separation", 8);

                    for (int childIndex = 0; childIndex < _node.Children.Count; childIndex++)
                    {
                        Control? child = BuildChild(_node.Children[childIndex], item);
                        if (child is not null)
                            row.AddChild(child);
                    }

                    _container.AddChild(row);
                }
            }

            private Control? BuildChild(UiSchemaNode node, UiRepeaterItemData item)
            {
                switch (node)
                {
                    case UiTextNode textNode:
                        return new Label { Text = textNode.Text };
                    case UiDynamicNode dynamicNode:
                    {
                        string text = dynamicNode.SlotIndex < item.Values.Length
                            ? item.Values[dynamicNode.SlotIndex]?.ToString() ?? string.Empty
                            : string.Empty;
                        return new Label { Text = text };
                    }
                    case UiButtonNode buttonNode:
                    {
                        var button = new Button
                        {
                            Text = buttonNode.Label ?? string.Empty,
                            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                        };
                        uint actionId = buttonNode.ActionId;
                        ulong rowKey = item.RowKey;
                        button.Pressed += () => _onAction(actionId, rowKey);
                        return button;
                    }
                    default:
                        return null;
                }
            }
        }
    }
}
