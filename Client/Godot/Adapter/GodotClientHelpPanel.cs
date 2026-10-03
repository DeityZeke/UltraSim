using Godot;

namespace UltraSim.Client.Godot;

/// <summary>Session-owned, optional demo instructions on the Debug layer.</summary>
public sealed class GodotClientHelpPanel : IDisposable
{
    private readonly Button _toggle;
    private readonly PanelContainer _panel;
    private readonly StyleBoxFlat _background;
    private readonly HBoxContainer _actions;
    private bool _disposed;

    internal GodotClientHelpPanel(Control debugHost, string title, string instructions)
    {
        ArgumentNullException.ThrowIfNull(debugHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        _toggle = new Button { Name = "DemoHelpToggle", Text = "Help", FocusMode = Control.FocusModeEnum.None };
        _toggle.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _toggle.OffsetLeft = -112;
        _toggle.OffsetRight = -16;
        _toggle.OffsetTop = 12;
        _toggle.OffsetBottom = 46;
        _toggle.Pressed += Toggle;
        debugHost.AddChild(_toggle);

        _panel = new PanelContainer
        {
            Name = "DemoHelpPanel",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _panel.OffsetLeft = -620;
        _panel.OffsetRight = -16;
        _panel.OffsetTop = 56;
        _background = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.075f, 0.09f, 1f),
            BorderColor = new Color(0.35f, 0.6f, 0.7f, 1f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 14,
            ContentMarginTop = 12,
            ContentMarginRight = 14,
            ContentMarginBottom = 12,
        };
        _panel.AddThemeStyleboxOverride("panel", _background);
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(stack);
        stack.AddChild(new Label { Text = title, ThemeTypeVariation = "HeaderMedium" });
        _actions = new HBoxContainer();
        stack.AddChild(_actions);
        stack.AddChild(new Label
        {
            Text = instructions,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(570, 0),
        });
        debugHost.AddChild(_panel);
    }

    public bool IsOpen => !_disposed && _panel.Visible;
    internal Node? NativeParent => _panel.GetParent();
    internal StyleBoxFlat DiagnosticBackground => _background;

    public void AddAction(string label, Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(action);
        var button = new Button
        {
            Name = $"DemoHelpAction_{_actions.GetChildCount()}",
            Text = label,
            FocusMode = Control.FocusModeEnum.None,
        };
        button.Pressed += action;
        _actions.AddChild(button);
    }

    public void Toggle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _panel.Visible = !_panel.Visible;
        _toggle.Text = _panel.Visible ? "Close Help" : "Help";
    }

    public void Close()
    {
        if (_disposed) return;
        _panel.Visible = false;
        _toggle.Text = "Help";
    }

    /// <summary>A modal shell surface owns input above session-only Help.</summary>
    internal void SetAvailable(bool available)
    {
        if (_disposed) return;
        if (!available) Close();
        _toggle.Visible = available;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Retire(_toggle);
        Retire(_panel);
        _background.Dispose();
    }

    private static void Retire(Control control)
    {
        if (!GodotObject.IsInstanceValid(control)) return;
        control.GetParent()?.RemoveChild(control);
        control.QueueFree();
    }
}
