using Godot;
using UltraSim.Game.Inspection;

namespace UltraSim.Client.Godot;

/// <summary>
/// Small always-local hover realization. Typed facts come from bounded semantic presentation state;
/// this wrapper owns only formatting, native layout, styling, cursor placement, and viewport clamp.
/// </summary>
internal sealed class GodotWorldHoverTooltip : IDisposable
{
    private const float HorizontalPadding = 24f;
    private const float ViewportMargin = 8f;
    private const float MaximumContentWidth = 480f;
    private readonly PanelContainer _panel;
    private readonly StyleBoxFlat _background;
    private readonly VBoxContainer _stack;
    private readonly Label _title;
    private readonly Label[] _rows = new Label[InspectionPropertySet.MaximumProperties];
    private string? _contentTitle;
    private InspectionPropertySet? _contentProperties;
    private float _layoutViewportWidth;
    private string _details = string.Empty;
    private int _visibleRows;
    private ulong _layoutGeneration;
    private bool _disposed;

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
        _background = new StyleBoxFlat
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
        _panel.AddThemeStyleboxOverride("panel", _background);
        _stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddChild(_stack);
        _title = CreateLabel("Title");
        _title.ThemeTypeVariation = "HeaderSmall";
        _title.Modulate = new Color(1f, 0.82f, 0.36f);
        _stack.AddChild(_title);
        for (int index = 0; index < _rows.Length; index++)
        {
            Label row = CreateLabel($"Property_{index}");
            row.Visible = false;
            _rows[index] = row;
            _stack.AddChild(row);
        }
        parent.AddChild(_panel);
    }

    internal bool Visible => _panel.Visible;
    internal string Title => _title.Text;
    internal string Details => _details;
    internal Vector2 PanelSize => _panel.Size;
    internal Vector2 BoundMinimumSize => _panel.GetBoundMinimumSize();
    internal int VisibleRowCount => _visibleRows;
    internal ulong LayoutGeneration => _layoutGeneration;
    internal Node? NativeParent => _panel.GetParent();
    internal StyleBoxFlat DiagnosticBackground => _background;

    internal void Hide()
    {
        if (!_disposed) _panel.Visible = false;
    }

    internal void Show(
        string title,
        InspectionPropertySet? properties,
        in Vector2 cursor,
        in Vector2 viewportSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        // A top-level Control is not owned by a sizing Container. Reassert its current bound
        // minimum even when content came from the cache so a prior native wrap/reflow can never
        // leave stale surplus height behind.
        FitPanelToContent(queueSort: false);
        Place(cursor, viewportSize);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (GodotObject.IsInstanceValid(_panel))
        {
            _panel.GetParent()?.RemoveChild(_panel);
            _panel.QueueFree();
        }
        _background.Dispose();
        _disposed = true;
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
            _rows[index].CustomMaximumSize = new Vector2(-1, -1);
            _rows[index].AutowrapMode = TextServer.AutowrapMode.Off;
        }
        _visibleRows = rows.Count;

        float maximumPanelWidth = Math.Max(48f, viewportWidth - ViewportMargin * 2f);
        float maximumContentWidth = Math.Max(
            24f, Math.Min(MaximumContentWidth, maximumPanelWidth - HorizontalPadding));
        float titleWidth = MeasureWidth(_title, title);
        float naturalContentWidth = titleWidth;
        Span<float> rowWidths = stackalloc float[InspectionPropertySet.MaximumProperties];
        for (int index = 0; index < rows.Count; index++)
        {
            rowWidths[index] = MeasureWidth(_rows[index], rows[index]);
            naturalContentWidth = Math.Max(naturalContentWidth, rowWidths[index]);
        }
        float contentWidth = Math.Clamp(naturalContentWidth, 1f, maximumContentWidth);
        ConstrainLabel(_title, contentWidth, titleWidth);
        for (int index = 0; index < rows.Count; index++)
            ConstrainLabel(_rows[index], contentWidth, rowWidths[index]);
        float panelWidth = Math.Min(maximumPanelWidth, contentWidth + HorizontalPadding);
        _panel.CustomMinimumSize = new Vector2(panelWidth, 0);
        _panel.CustomMaximumSize = new Vector2(panelWidth, -1);
        FitPanelToContent(queueSort: true);

        _contentTitle = title;
        _contentProperties = properties;
        _layoutViewportWidth = viewportWidth;
        if (_layoutGeneration != ulong.MaxValue) _layoutGeneration++;
        return true;
    }

    private void Place(in Vector2 cursor, in Vector2 viewportSize)
    {
        Vector2 required = _panel.Size;
        if (required.X <= 0 || required.Y <= 0) required = _panel.GetBoundMinimumSize();
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
        AutowrapMode = TextServer.AutowrapMode.Off,
    };

    private void FitPanelToContent(bool queueSort)
    {
        if (queueSort) _stack.QueueSort();
        Vector2 required = _panel.GetBoundMinimumSize();
        if (!Mathf.IsEqualApprox(_panel.Size.X, required.X) ||
            !Mathf.IsEqualApprox(_panel.Size.Y, required.Y))
            _panel.SetSize(required);
    }

    private static void ConstrainLabel(Label label, float width, float naturalWidth)
    {
        // Godot 4.7 requires a maximum width for auto-wrapped Labels. A minimum width alone can
        // make the Label report a transient multi-line height based on an older native width,
        // causing its top-level panel to grow after our synchronous fit and never shrink again.
        label.AutowrapMode = naturalWidth > width
            ? TextServer.AutowrapMode.WordSmart
            : TextServer.AutowrapMode.Off;
        label.CustomMinimumSize = new Vector2(width, 0);
        label.CustomMaximumSize = new Vector2(width, -1);
        label.ResetSize();
    }

    private static float MeasureWidth(Label label, string text)
    {
        // The theme retains the native font, but GetThemeFont manufactures a managed
        // RefCounted wrapper. Do not leave that temporary wrapper to finalization.
        using Font font = label.GetThemeFont("font");
        int fontSize = label.GetThemeFontSize("font_size");
        return MathF.Ceiling(font.GetStringSize(
            text, HorizontalAlignment.Left, -1, fontSize).X);
    }
}
