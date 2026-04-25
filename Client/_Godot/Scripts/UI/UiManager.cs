using Godot;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class UiManager : Node
{
    [Export] public NodePath? HudCanvasPath { get; set; }
    [Export] public NodePath? MenuCanvasPath { get; set; }
    [Export] public NodePath? DebugCanvasPath { get; set; }

    public HudCanvasManager? Hud { get; private set; }
    public MenuCanvasManager? Menus { get; private set; }
    public DebugCanvasManager? Debug { get; private set; }

    public override void _Ready()
    {
        Hud = ResolveNode<HudCanvasManager>(HudCanvasPath);
        Menus = ResolveNode<MenuCanvasManager>(MenuCanvasPath);
        Debug = ResolveNode<DebugCanvasManager>(DebugCanvasPath);
        SetStatusText("Booting client shell");
    }

    public void SetStatusText(string text)
    {
        Hud?.SetStatusText(text);
        Debug?.SetStatusText(text);
    }

    private TNode? ResolveNode<TNode>(NodePath? path)
        where TNode : Node
    {
        return path is null || path.IsEmpty
            ? null
            : GetNodeOrNull<TNode>(path);
    }
}
