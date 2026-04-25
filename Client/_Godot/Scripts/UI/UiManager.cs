using Godot;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class UiManager : Node
{
    [Export] public NodePath? HudCanvasPath { get; set; }
    [Export] public NodePath? WorldCanvasPath { get; set; }
    [Export] public NodePath? MenuCanvasPath { get; set; }
    [Export] public NodePath? DebugCanvasPath { get; set; }

    public WorldCanvasManager? World { get; private set; }
    public HudCanvasManager? Hud { get; private set; }
    public MenuCanvasManager? Menus { get; private set; }
    public DebugCanvasManager? Debug { get; private set; }
    public bool BlocksGameplayInput => Menus?.BlocksGameplayInput == true;

    public override void _Ready()
    {
        World = ResolveNode<WorldCanvasManager>(WorldCanvasPath);
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

    public void SetNetworkSummaryText(string text)
    {
        Hud?.SetNetworkSummaryText(text);
    }

    private TNode? ResolveNode<TNode>(NodePath? path)
        where TNode : Node
    {
        return path is null || path.IsEmpty
            ? null
            : GetNodeOrNull<TNode>(path);
    }
}
