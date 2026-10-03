using Godot;

namespace UltraSim.Client.Godot;

/// <summary>Cold Debug pages share an opaque, movable, owner-retired surface.</summary>
internal sealed partial class GodotClientDiagnosticPanel : PanelContainer
{
    private readonly StyleBoxFlat _background;
    private readonly Label _handle;
    private bool _dragging;
    private Vector2 _dragOffset;

    internal GodotClientDiagnosticPanel(GodotClientDebugPage page, string content)
    {
        Name = $"Debug{page}";
        Position = new Vector2(24, 72);
        CustomMinimumSize = new Vector2(600, 0);
        MouseFilter = MouseFilterEnum.Stop;
        _background = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.075f, 0.09f, 1f),
            BorderColor = new Color(0.35f, 0.6f, 0.7f, 1f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 16,
            ContentMarginTop = 12,
            ContentMarginRight = 16,
            ContentMarginBottom = 12,
        };
        AddThemeStyleboxOverride("panel", _background);
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 8);
        AddChild(stack);
        _handle = new Label
        {
            Text = $"{page} diagnostics  ·  drag here to move  ·  F11 changes page",
            MouseFilter = MouseFilterEnum.Stop,
        };
        _handle.GuiInput += OnHandleInput;
        stack.AddChild(_handle);
        stack.AddChild(new Label
        {
            Text = content,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        });
    }

    internal StyleBoxFlat DiagnosticBackground => _background;
    internal void RetireNativeResources() => _background.Dispose();
    internal void DragForAcceptance(Vector2 requested) => Place(requested);

    private void OnHandleInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _dragging = button.Pressed;
                if (_dragging) _dragOffset = button.GlobalPosition - Position;
                _handle.AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                Place(motion.GlobalPosition - _dragOffset);
                _handle.AcceptEvent();
                break;
        }
    }

    private void Place(Vector2 requested)
    {
        Vector2 viewport = GetViewportRect().Size;
        Vector2 size = GetCombinedMinimumSize();
        Position = new Vector2(
            Math.Clamp(requested.X, 12f, Math.Max(12f, viewport.X - size.X - 12f)),
            Math.Clamp(requested.Y, 12f, Math.Max(12f, viewport.Y - size.Y - 12f)));
    }
}
