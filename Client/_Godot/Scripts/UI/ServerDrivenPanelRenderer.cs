using Godot;
using UltraSim.Client.UI;
using UltraSim.UI;

namespace UltraSim.Client.Godot.UI;

internal static class ServerDrivenPanelRenderer
{
    public static bool TryBuild(
        ClientUiPanelInstance panel,
        Action<UiActionDescriptor>? actionPressed,
        out Control root,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(panel);

        var nodeById = new Dictionary<int, UiSchemaNodeDescriptor>(panel.Schema.Nodes.Count);
        for (int i = 0; i < panel.Schema.Nodes.Count; i++)
        {
            UiSchemaNodeDescriptor node = panel.Schema.Nodes[i];
            nodeById[node.NodeId] = node;
        }

        var modelBySlot = new Dictionary<string, UiModelSlotValue>(panel.Model.Values.Count, StringComparer.Ordinal);
        for (int i = 0; i < panel.Model.Values.Count; i++)
        {
            UiModelSlotValue value = panel.Model.Values[i];
            modelBySlot[value.SlotId] = value;
        }

        if (!TryBuildNode(panel.Schema.RootNodeId, panel.CompatibilityContext, nodeById, modelBySlot, actionPressed, out root!, out error))
        {
            root = new Control();
            return false;
        }

        root.Name = MakeNodeName("ServerPanel", panel.PanelInstanceId);
        root.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        root.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        root.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        return true;
    }

    private static bool TryBuildNode(
        int nodeId,
        UiSchemaCompatibilityContext compatibilityContext,
        IReadOnlyDictionary<int, UiSchemaNodeDescriptor> nodeById,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot,
        Action<UiActionDescriptor>? actionPressed,
        out Control control,
        out string error)
    {
        if (!nodeById.TryGetValue(nodeId, out UiSchemaNodeDescriptor? node))
        {
            control = new Control();
            error = $"UI schema node {nodeId} is missing.";
            return false;
        }

        control = node.Kind switch
        {
            UiNodeKind.Panel => CreatePanel(node),
            UiNodeKind.Row => CreateRow(node),
            UiNodeKind.Column => CreateColumn(node),
            UiNodeKind.Group => CreateGroup(node),
            UiNodeKind.Section => CreateSection(node),
            UiNodeKind.Text => CreateText(node),
            UiNodeKind.DynamicText => CreateDynamicText(node, modelBySlot),
            UiNodeKind.Button => CreateButton(node, compatibilityContext, actionPressed),
            UiNodeKind.Separator => CreateSeparator(node),
            UiNodeKind.Spacer => CreateSpacer(node),
            UiNodeKind.TextInput => CreateTextInput(node, modelBySlot),
            UiNodeKind.Toggle => CreateToggle(node, modelBySlot),
            _ => CreateUnsupportedPlaceholder(node),
        };

        if (node.Children.Count > 0 && control is Label or BaseButton or LineEdit or HSeparator)
        {
            error = $"UI node kind '{node.Kind}' cannot host child controls in the minimal Godot renderer.";
            return false;
        }

        Control childHost = control;
        if (control is PanelContainer panel)
        {
            var content = new VBoxContainer
            {
                Name = "Content",
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            };
            panel.AddChild(content);
            childHost = content;
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            if (!TryBuildNode(
                node.Children[i],
                compatibilityContext,
                nodeById,
                modelBySlot,
                actionPressed,
                out Control child,
                out error))
            {
                control.QueueFree();
                return false;
            }

            childHost.AddChild(child);
        }

        error = string.Empty;
        return true;
    }

    private static PanelContainer CreatePanel(UiSchemaNodeDescriptor node)
    {
        var panel = new PanelContainer
        {
            Name = MakeNodeName("Panel", node.NodeId),
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(260.0f, 0.0f),
        };
        panel.AddThemeConstantOverride("margin_left", 10);
        panel.AddThemeConstantOverride("margin_top", 10);
        panel.AddThemeConstantOverride("margin_right", 10);
        panel.AddThemeConstantOverride("margin_bottom", 10);
        return panel;
    }

    private static HBoxContainer CreateRow(UiSchemaNodeDescriptor node)
    {
        return new HBoxContainer
        {
            Name = MakeNodeName("Row", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private static VBoxContainer CreateColumn(UiSchemaNodeDescriptor node)
    {
        return new VBoxContainer
        {
            Name = MakeNodeName("Column", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private static VBoxContainer CreateGroup(UiSchemaNodeDescriptor node)
    {
        var group = new VBoxContainer
        {
            Name = MakeNodeName("Group", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        AddTitleLabelIfPresent(group, node, "GroupTitle");
        return group;
    }

    private static VBoxContainer CreateSection(UiSchemaNodeDescriptor node)
    {
        var section = new VBoxContainer
        {
            Name = MakeNodeName("Section", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        AddTitleLabelIfPresent(section, node, "SectionTitle");
        section.AddChild(new HSeparator
        {
            Name = MakeNodeName("SectionSeparator", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
        return section;
    }

    private static Label CreateText(UiSchemaNodeDescriptor node)
    {
        return new Label
        {
            Name = MakeNodeName("Text", node.NodeId),
            Text = node.Text ?? string.Empty,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private static Label CreateDynamicText(
        UiSchemaNodeDescriptor node,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        string text = node.Text ?? string.Empty;
        string? slotId = node.SlotId;
        if (!string.IsNullOrWhiteSpace(slotId)
            && modelBySlot.TryGetValue(slotId, out UiModelSlotValue? value)
            && value is not null)
        {
            text = FormatModelValue(value);
        }

        return new Label
        {
            Name = MakeNodeName("DynamicText", node.NodeId),
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private static LineEdit CreateTextInput(
        UiSchemaNodeDescriptor node,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        var input = new LineEdit
        {
            Name = MakeNodeName("TextInput", node.NodeId),
            Text = GetStringModelValue(node, modelBySlot),
            PlaceholderText = node.Text ?? string.Empty,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        SetSlotMeta(input, node);
        return input;
    }

    private static CheckBox CreateToggle(
        UiSchemaNodeDescriptor node,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        var toggle = new CheckBox
        {
            Name = MakeNodeName("Toggle", node.NodeId),
            Text = node.Text ?? node.SlotId ?? "Toggle",
            ButtonPressed = GetBooleanModelValue(node, modelBySlot),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        SetSlotMeta(toggle, node);
        return toggle;
    }

    private static Button CreateButton(
        UiSchemaNodeDescriptor node,
        UiSchemaCompatibilityContext compatibilityContext,
        Action<UiActionDescriptor>? actionPressed)
    {
        UiActionDescriptor? action = null;
        if (!string.IsNullOrWhiteSpace(node.ActionId))
        {
            _ = compatibilityContext.TryGetAction(node.ActionId, out action);
        }

        var button = new Button
        {
            Name = MakeNodeName("Button", node.NodeId),
            Text = node.Text ?? action?.Label ?? "Button",
            Disabled = action is null || action.Availability != UiActionAvailability.Available,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        if (action is not null && actionPressed is not null)
        {
            button.SetMeta("UiActionId", action.ActionId);
            button.Pressed += () => actionPressed(action);
        }

        return button;
    }

    private static HSeparator CreateSeparator(UiSchemaNodeDescriptor node)
    {
        return new HSeparator
        {
            Name = MakeNodeName("Separator", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
    }

    private static Control CreateSpacer(UiSchemaNodeDescriptor node)
    {
        return new Control
        {
            Name = MakeNodeName("Spacer", node.NodeId),
            CustomMinimumSize = new Vector2(0.0f, 8.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private static Control CreateUnsupportedPlaceholder(UiSchemaNodeDescriptor node)
    {
        return new VBoxContainer
        {
            Name = MakeNodeName($"Unsupported_{node.Kind}", node.NodeId),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private static void AddTitleLabelIfPresent(VBoxContainer parent, UiSchemaNodeDescriptor node, string prefix)
    {
        if (string.IsNullOrWhiteSpace(node.Text))
        {
            return;
        }

        parent.AddChild(new Label
        {
            Name = MakeNodeName(prefix, node.NodeId),
            Text = node.Text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        });
    }

    private static void SetSlotMeta(Control control, UiSchemaNodeDescriptor node)
    {
        if (!string.IsNullOrWhiteSpace(node.SlotId))
        {
            control.SetMeta("UiSlotId", node.SlotId);
        }
    }

    private static string GetStringModelValue(
        UiSchemaNodeDescriptor node,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        if (!string.IsNullOrWhiteSpace(node.SlotId)
            && modelBySlot.TryGetValue(node.SlotId, out UiModelSlotValue? value)
            && value is not null
            && value.Kind == UiModelValueKind.String)
        {
            return value.StringValue ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool GetBooleanModelValue(
        UiSchemaNodeDescriptor node,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        return !string.IsNullOrWhiteSpace(node.SlotId)
            && modelBySlot.TryGetValue(node.SlotId, out UiModelSlotValue? value)
            && value is not null
            && value.Kind == UiModelValueKind.Boolean
            && value.BooleanValue;
    }

    private static string FormatModelValue(UiModelSlotValue value)
    {
        return value.Kind switch
        {
            UiModelValueKind.String => value.StringValue ?? string.Empty,
            UiModelValueKind.Boolean => value.BooleanValue ? "True" : "False",
            UiModelValueKind.Integer => value.IntegerValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            UiModelValueKind.Number => value.NumberValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            _ => string.Empty,
        };
    }

    private static string MakeNodeName(string prefix, int nodeId)
    {
        return $"{prefix}_{nodeId}";
    }

    private static string MakeNodeName(string prefix, string value)
    {
        Span<char> buffer = stackalloc char[Math.Min(value.Length, 64)];
        int written = 0;
        for (int i = 0; i < value.Length && written < buffer.Length; i++)
        {
            char c = value[i];
            buffer[written++] = char.IsLetterOrDigit(c) ? c : '_';
        }

        return written == 0
            ? prefix
            : $"{prefix}_{new string(buffer[..written])}";
    }
}
