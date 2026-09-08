using Godot;

namespace UltraSim.Client.Godot;

/// <summary>
/// Small always-local hover realization. Its text comes from bounded semantic presentation state;
/// this wrapper owns only native layout, styling, cursor placement, and viewport clamping.
/// </summary>
internal sealed class GodotWorldHoverTooltip
{
    private const float MinimumWidth = 300f;
    private readonly PanelContainer _panel;
    private readonly Label _title;
    private readonly Label _details;

    internal GodotWorldHoverTooltip(Control parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _panel = new PanelContainer
        {
            Name = "WorldHoverTooltip",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(MinimumWidth, 0),
            ZIndex = 100,
        };
        var background = new StyleBoxFlat
        {
            BgColor = new Color(0.035f, 0.03f, 0.025f, 0.96f),
            BorderColor = new Color(0.72f, 0.55f, 0.2f, 1f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 12,
            ContentMarginTop = 9,
            ContentMarginRight = 12,
            ContentMarginBottom = 9,
        };
        _panel.AddThemeStyleboxOverride("panel", background);
        var stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddChild(stack);
        _title = new Label
        {
            Name = "Title",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ThemeTypeVariation = "HeaderSmall",
            Modulate = new Color(1f, 0.82f, 0.36f),
        };
        _details = new Label
        {
            Name = "Properties",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        stack.AddChild(_title);
        stack.AddChild(_details);
        parent.AddChild(_panel);
    }

    internal bool Visible => _panel.Visible;
    internal string Title => _title.Text;
    internal string Details => _details.Text;

    internal void Hide() => _panel.Visible = false;

    internal void Show(
        string title,
        string? details,
        in Vector2 cursor,
        in Vector2 viewportSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _title.Text = title;
        _details.Text = string.IsNullOrEmpty(details)
            ? string.Empty
            : details.Replace(" · ", "\n", StringComparison.Ordinal);
        _details.Visible = _details.Text.Length != 0;
        Vector2 required = _panel.GetCombinedMinimumSize();
        float width = Math.Max(MinimumWidth, required.X);
        float height = Math.Max(40f, required.Y);
        _panel.Position = new Vector2(
            Math.Clamp(cursor.X + 14f, 8f, Math.Max(8f, viewportSize.X - width - 8f)),
            Math.Clamp(cursor.Y + 18f, 8f, Math.Max(8f, viewportSize.Y - height - 8f)));
        _panel.Visible = true;
    }
}
