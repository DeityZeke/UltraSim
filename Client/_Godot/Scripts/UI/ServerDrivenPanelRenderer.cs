using Godot;
using UltraSim.Client.UI;
using UltraSim.UI;

namespace UltraSim.Client.Godot.UI;

internal static class ServerDrivenPanelRenderer
{
    public static bool TryBuild(ClientUiPanelInstance panel, out Control root, out string error)
    {
        ArgumentNullException.ThrowIfNull(panel);

        var nodeById = new Dictionary<int, UiSchemaNodeDescriptor>(panel.Schema.Nodes.Count);
        for (int i = 0; i < panel.Schema.Nodes.Count; i++)
        {
            UiSchemaNodeDescriptor node = panel.Schema.Nodes[i];
            nodeById[node.NodeId] = node;
        }

        if (!TryBuildNode(panel.Schema.RootNodeId, nodeById, out root!, out error))
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
        IReadOnlyDictionary<int, UiSchemaNodeDescriptor> nodeById,
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
            UiNodeKind.Text => CreateText(node),
            UiNodeKind.Button => CreateButton(node),
            _ => new Control(),
        };

        if (node.Kind is not (UiNodeKind.Panel or UiNodeKind.Row or UiNodeKind.Column or UiNodeKind.Text or UiNodeKind.Button))
        {
            error = $"UI node kind '{node.Kind}' is not supported by the minimal Godot renderer.";
            return false;
        }

        if (node.Children.Count > 0 && control is Label or Button)
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
            if (!TryBuildNode(node.Children[i], nodeById, out Control child, out error))
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

    private static Button CreateButton(UiSchemaNodeDescriptor node)
    {
        return new Button
        {
            Name = MakeNodeName("Button", node.NodeId),
            Text = node.Text ?? "Button",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
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
