using Godot;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class MenuCanvasManager : CanvasLayer
{
    private PanelContainer? _panel;

    public bool IsMenuOpen => _panel?.Visible == true;

    public override void _Ready()
    {
        _panel = GetNodeOrNull<PanelContainer>("PreviewPanel");
        if (_panel is null)
        {
            _panel = new PanelContainer
            {
                Name = "PreviewPanel",
                Visible = false
            };
            AddChild(_panel);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            TogglePreviewPanel();
            GetViewport().SetInputAsHandled();
        }
    }

    public void TogglePreviewPanel()
    {
        if (_panel is not null)
        {
            _panel.Visible = !_panel.Visible;
        }
    }
}
