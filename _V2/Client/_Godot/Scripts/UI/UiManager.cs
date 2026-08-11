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
        World = ResolveNode<WorldCanvasManager>(WorldCanvasPath, "World");
        Hud = ResolveNode<HudCanvasManager>(HudCanvasPath, "HUD");
        Menus = ResolveNode<MenuCanvasManager>(MenuCanvasPath, "Menus");
        Debug = ResolveNode<DebugCanvasManager>(DebugCanvasPath, "Debug");
        Menus?.SetWorldCanvas(World);
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

    private TNode? ResolveNode<TNode>(NodePath? path, string fallbackName)
        where TNode : Node
    {
        if (path is not null && !path.IsEmpty)
        {
            TNode? node = GetNodeOrNull<TNode>(path);
            if (node is not null)
            {
                return node;
            }
        }

        return GetNodeOrNull<TNode>(fallbackName);
    }
}
