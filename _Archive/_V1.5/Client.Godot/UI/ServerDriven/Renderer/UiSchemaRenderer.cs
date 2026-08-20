using System;
using System.Collections.Generic;
using Godot;
using UltraSim.UI.Schema;

namespace Client.Godot.UI.ServerDriven.Renderer
{
    /// <summary>
    /// Builds a Godot control tree from a <see cref="UiSchemaDefinition"/> and an initial
    /// slot value array. Returns a <see cref="RenderedPanel"/> that can be updated cheaply
    /// on subsequent payload updates without rebuilding the tree.
    ///
    /// Style strings (e.g. "text.title.centered", "button.confirm") are resolved to basic
    /// Godot properties. Unknown styles fall back to defaults with a logged warning.
    ///
    /// Phase 3: minimal node set (Text, Dynamic, Button, Row, Header, Footer, Separator, Layer).
    /// Layer nodes are skipped — visual theming is handled by the host PanelContainer style.
    /// </summary>
    public sealed class UiSchemaRenderer
    {
        private readonly Action<string>? _styleWarning;

        public UiSchemaRenderer(Action<string>? styleWarning = null)
        {
            _styleWarning = styleWarning;
        }

        // -------------------------------------------------------------------------
        // Build
        // -------------------------------------------------------------------------

        public RenderedPanel Build(
            UiSchemaDefinition definition,
            object?[]          slotValues,
            Action<uint, ulong?> onAction)
        {
            // Root: VBoxContainer inside a MarginContainer for padding
            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left",   12);
            margin.AddThemeConstantOverride("margin_top",    8);
            margin.AddThemeConstantOverride("margin_right",  12);
            margin.AddThemeConstantOverride("margin_bottom", 8);
            margin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            margin.SizeFlagsVertical   = Control.SizeFlags.ShrinkCenter;

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 4);
            margin.AddChild(vbox);

            var rendered = new RenderedPanel(margin);

            foreach (var node in definition.Nodes)
                BuildNode(node, vbox, rendered, slotValues, onAction);

            rendered.ApplySlots(slotValues);
            return rendered;
        }

        // -------------------------------------------------------------------------
        // Node dispatch
        // -------------------------------------------------------------------------

        private void BuildNode(
            UiSchemaNode node,
            Control      parent,
            RenderedPanel rendered,
            object?[]    slotValues,
            Action<uint, ulong?> onAction)
        {
            // Bind visibility before adding to parent so initial state is correct
            Control? built = node switch
            {
                UiLayerNode       => null,                          // Phase 3: skip layers
                UiSeparatorNode s => BuildSeparator(s),
                UiTextNode      t => BuildText(t),
                UiDynamicNode   d => BuildDynamic(d, rendered),
                UiButtonNode    b => BuildButton(b, onAction),
                UiTextInputNode i => BuildTextInput(i, rendered),
                UiRepeaterNode  r => BuildRepeater(r, rendered, onAction),
                UiContainerNode c => BuildContainer(c, rendered, slotValues, onAction),
                _                 => null,
            };

            if (built is null) return;

            if (node.VisibilitySlot != 0xFF)
                rendered.VisibilityControls[node.VisibilitySlot] = built;

            parent.AddChild(built);
        }

        // -------------------------------------------------------------------------
        // Primitive node builders
        // -------------------------------------------------------------------------

        private static HSeparator BuildSeparator(UiSeparatorNode node)
        {
            return new HSeparator { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        }

        private Label BuildText(UiTextNode node)
        {
            var label = new Label { Text = node.Text };
            ApplyLabelStyle(label, node.Style);
            return label;
        }

        private Label BuildDynamic(UiDynamicNode node, RenderedPanel rendered)
        {
            var label = new Label();
            ApplyLabelStyle(label, node.Style);
            rendered.DynamicLabels[node.SlotIndex] = label;
            return label;
        }

        private static LineEdit BuildTextInput(UiTextInputNode node, RenderedPanel rendered)
        {
            var lineEdit = new LineEdit
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                PlaceholderText     = node.Placeholder ?? string.Empty,
                Secret              = node.IsPassword,
            };
            if (node.MaxLength > 0)
                lineEdit.MaxLength = node.MaxLength;
            rendered.TextInputs[node.FieldIndex] = lineEdit;
            return lineEdit;
        }

        private static Button BuildButton(UiButtonNode node, Action<uint, ulong?> onAction)
        {
            var btn = new Button
            {
                Text = node.Label ?? string.Empty,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            };
            uint actionId = node.ActionId;
            btn.Pressed += () => onAction(actionId, null);
            return btn;
        }

        // -------------------------------------------------------------------------
        // Container node builders
        // -------------------------------------------------------------------------

        private Control BuildContainer(
            UiContainerNode container,
            RenderedPanel   rendered,
            object?[]       slotValues,
            Action<uint, ulong?> onAction)
        {
            bool isFooter  = container is UiFooterNode;
            bool isHeader  = container is UiHeaderNode;

            var hbox = new HBoxContainer();
            hbox.AddThemeConstantOverride("separation", 8);
            hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            if (isFooter)
            {
                hbox.Alignment = BoxContainer.AlignmentMode.Center;
                // Small top margin for footer separation
                var margin = new MarginContainer();
                margin.AddThemeConstantOverride("margin_top", 4);
                margin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                margin.AddChild(hbox);

                foreach (var child in container.Children)
                    BuildNode(child, hbox, rendered, slotValues, onAction);

                return margin;
            }

            if (isHeader)
            {
                // Header spans full width, contents centered
                hbox.Alignment = BoxContainer.AlignmentMode.Center;
            }

            foreach (var child in container.Children)
                BuildNode(child, hbox, rendered, slotValues, onAction);

            return hbox;
        }



        private static VBoxContainer BuildRepeater(
            UiRepeaterNode node,
            RenderedPanel rendered,
            Action<uint, ulong?> onAction)
        {
            var vbox = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            vbox.AddThemeConstantOverride("separation", 4);
            rendered.Repeaters[node.RepeaterId] = new RenderedPanel.RepeaterBinding(node, vbox, onAction);
            return vbox;
        }

        // -------------------------------------------------------------------------
        // Style helpers
        // -------------------------------------------------------------------------

        private void ApplyLabelStyle(Label label, string? style)
        {
            if (string.IsNullOrEmpty(style)) return;

            if (style.Contains("centered", StringComparison.OrdinalIgnoreCase))
            {
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            }

            if (style.Contains("title", StringComparison.OrdinalIgnoreCase))
            {
                // Slightly larger — theme override only if a font_size theme item exists
                label.AddThemeFontSizeOverride("font_size", 16);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            }

            if (style.Contains("warning", StringComparison.OrdinalIgnoreCase))
            {
                label.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
            }

            // Unknown style tokens are silently ignored for Phase 3.
            // A full theme/style system is a Phase 4+ concern.
        }
    }
}
