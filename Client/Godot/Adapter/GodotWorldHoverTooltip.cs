using Godot;
using UltraSim.Game.Inspection;

namespace UltraSim.Client.Godot;

/// <summary>
/// Small always-local hover realization. Typed facts come from bounded semantic presentation state;
/// this wrapper owns only formatting, native layout, styling, cursor placement, and viewport clamp.
/// </summary>
internal sealed class GodotWorldHoverTooltip
{
    private const float HorizontalPadding = 24f;
    private const float ViewportMargin = 8f;
    private const float MaximumContentWidth = 480f;
    private readonly PanelContainer _panel;
    private readonly Label _title;
    private readonly Label[] _rows = new Label[InspectionPropertySet.MaximumProperties];
    private string? _contentTitle;
    private InspectionPropertySet? _contentProperties;
    private float _layoutViewportWidth;
    private string _details = string.Empty;
    private int _visibleRows;
    private ulong _layoutGeneration;

    internal GodotWorldHoverTooltip(Control parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _panel = new PanelContainer
        {
            Name = "WorldHoverTooltip",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
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
        _title = CreateLabel("Title");
        _title.ThemeTypeVariation = "HeaderSmall";
        _title.Modulate = new Color(1f, 0.82f, 0.36f);
        stack.AddChild(_title);
        for (int index = 0; index < _rows.Length; index++)
        {
            Label row = CreateLabel($"Property_{index}");
            row.Visible = false;
            _rows[index] = row;
            stack.AddChild(row);
        }
        parent.AddChild(_panel);
    }

    internal bool Visible => _panel.Visible;
    internal string Title => _title.Text;
    internal string Details => _details;
    internal Vector2 PanelSize => _panel.Size;
    internal int VisibleRowCount => _visibleRows;
    internal ulong LayoutGeneration => _layoutGeneration;

    internal void Hide() => _panel.Visible = false;

    internal void Show(
        string title,
        InspectionPropertySet? properties,
        in Vector2 cursor,
        in Vector2 viewportSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (!string.Equals(_contentTitle, title, StringComparison.Ordinal) ||
            !Equals(_contentProperties, properties) ||
            !Mathf.IsEqualApprox(_layoutViewportWidth, viewportSize.X))
        {
            if (!Rebuild(title, properties, viewportSize.X))
            {
                Hide();
                return;
            }
        }
        _panel.Visible = true;
        Place(cursor, viewportSize);
    }

    private bool Rebuild(string title, InspectionPropertySet? properties, float viewportWidth)
    {
        IReadOnlyList<string> rows;
        if (properties is null)
        {
            rows = Array.Empty<string>();
            _details = string.Empty;
        }
        else
        {
            if (!InspectionPresentationDefaults.TryFormat(
                    title, properties, out InspectionPresentation? presentation))
                return false;
            rows = presentation!.Rows;
            _details = presentation.DetailText;
        }

        _title.Text = title;
        for (int index = 0; index < _rows.Length; index++)
        {
            bool visible = index < rows.Count;
            _rows[index].Text = visible ? rows[index] : string.Empty;
            _rows[index].Visible = visible;
            _rows[index].CustomMinimumSize = Vector2.Zero;
        }
        _visibleRows = rows.Count;

        float maximumPanelWidth = Math.Max(48f, viewportWidth - ViewportMargin * 2f);
        float maximumContentWidth = Math.Max(
            24f, Math.Min(MaximumContentWidth, maximumPanelWidth - HorizontalPadding));
        float naturalContentWidth = MeasureWidth(_title, title);
        for (int index = 0; index < rows.Count; index++)
            naturalContentWidth = Math.Max(
                naturalContentWidth, MeasureWidth(_rows[index], rows[index]));
        float contentWidth = Math.Clamp(naturalContentWidth, 1f, maximumContentWidth);
        _title.CustomMinimumSize = new Vector2(contentWidth, 0);
        for (int index = 0; index < rows.Count; index++)
            _rows[index].CustomMinimumSize = new Vector2(contentWidth, 0);
        _panel.CustomMinimumSize = new Vector2(
            Math.Min(maximumPanelWidth, contentWidth + HorizontalPadding), 0);
        _panel.Size = _panel.GetCombinedMinimumSize();

        _contentTitle = title;
        _contentProperties = properties;
        _layoutViewportWidth = viewportWidth;
        if (_layoutGeneration != ulong.MaxValue) _layoutGeneration++;
        return true;
    }

    private void Place(in Vector2 cursor, in Vector2 viewportSize)
    {
        Vector2 required = _panel.Size;
        if (required.X <= 0 || required.Y <= 0) required = _panel.GetCombinedMinimumSize();
        _panel.Position = new Vector2(
            Math.Clamp(
                cursor.X + 14f,
                ViewportMargin,
                Math.Max(ViewportMargin, viewportSize.X - required.X - ViewportMargin)),
            Math.Clamp(
                cursor.Y + 18f,
                ViewportMargin,
                Math.Max(ViewportMargin, viewportSize.Y - required.Y - ViewportMargin)));
    }

    private static Label CreateLabel(string name) => new()
    {
        Name = name,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };

    private static float MeasureWidth(Label label, string text)
    {
        Font font = label.GetThemeFont("font");
        int fontSize = label.GetThemeFontSize("font_size");
        return MathF.Ceiling(font.GetStringSize(
            text, HorizontalAlignment.Left, -1, fontSize).X);
    }
}
