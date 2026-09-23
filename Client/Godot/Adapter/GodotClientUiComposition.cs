using Godot;
using UltraSim.Client.Core;

namespace UltraSim.Client.Godot;

public readonly record struct GodotApplicationWorkspaceSnapshot(
    ClientPixelSize ApplicationSize,
    ClientPixelSize SelectedOutputShape,
    ClientPixelRect GameplayFrame,
    int UnusedLeft,
    int UnusedTop,
    int UnusedRight,
    int UnusedBottom,
    long UnusedArea,
    bool TransitionalMaskingActive);

public readonly record struct GodotGumpWindowManagerSnapshot(
    int OpenWindows,
    int NativeChildren,
    bool PointerCaptured,
    ulong FrontSequence,
    int CascadeIndex);

public readonly record struct GodotContextMenuSnapshot(
    bool Visible,
    string? Target,
    int ActionCount,
    Vector2 Position,
    Vector2 Size);

public enum GodotClientDebugPage : byte
{
    Off = 0,
    Client = 1,
    Presentation = 2,
    Content = 3,
}

public readonly record struct GodotClientUiCompositionSnapshot(
    GodotApplicationWorkspaceSnapshot Workspace,
    int WorldHostChildren,
    int HudHostChildren,
    int GameplayGumpHostChildren,
    int ShellMenuHostChildren,
    int DebugHostChildren,
    int ContextHostChildren,
    GodotGumpWindowManagerSnapshot Gumps,
    GodotClientDebugPage DebugPage,
    bool SessionActive,
    bool ShellModalVisible);

/// <summary>
/// Persistent F1.7 application workspace and category composition. The dark frame masks are a
/// transitional main-viewport realization; F1.9 may replace only that realization with a
/// SubViewport without changing the layout/input/category contract.
/// </summary>
public sealed class GodotClientUiComposition : IDisposable
{
    private static readonly Color WorkspaceColor = new(0.008f, 0.01f, 0.012f, 1f);
    private readonly Node _owner;
    private readonly CanvasLayer _workspaceLayer;
    private readonly Control _workspaceRoot;
    private readonly ColorRect[] _masks = new ColorRect[4];
    private readonly CanvasLayer _worldLayer;
    private readonly CanvasLayer _hudLayer;
    private readonly CanvasLayer _menuLayer;
    private readonly CanvasLayer _debugLayer;
    private readonly CanvasLayer _contextLayer;
    private readonly GodotClientDebugPresenter _debug;
    private ClientPixelSize _selectedOutput;
    private GameplayFrameLayout _layout;
    private GodotSessionUiLease? _session;
    private bool _shellModalVisible;
    private bool _disposed;

    public GodotClientUiComposition(Node owner, ClientPixelSize selectedOutput)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        if (!selectedOutput.IsValid)
            throw new ArgumentException("A finite selected output shape is required.", nameof(selectedOutput));
        _selectedOutput = selectedOutput;

        var layers = new Node { Name = "UILayers" };
        owner.AddChild(layers);
        _workspaceLayer = Layer("Workspace", 5);
        _worldLayer = Layer("World", 10);
        _hudLayer = Layer("HUD", 20);
        _menuLayer = Layer("Menus", 30);
        _debugLayer = Layer("Debug", 40);
        _contextLayer = Layer("ContextTooltip", 50);
        layers.AddChild(_workspaceLayer);
        layers.AddChild(_worldLayer);
        layers.AddChild(_hudLayer);
        layers.AddChild(_menuLayer);
        layers.AddChild(_debugLayer);
        layers.AddChild(_contextLayer);

        _workspaceRoot = FullRect("ApplicationWorkspace");
        _workspaceRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
        _workspaceLayer.AddChild(_workspaceRoot);
        string[] names = ["WorkspaceLeft", "WorkspaceTop", "WorkspaceRight", "WorkspaceBottom"];
        for (int index = 0; index < _masks.Length; index++)
        {
            _masks[index] = new ColorRect
            {
                Name = names[index],
                Color = WorkspaceColor,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _workspaceRoot.AddChild(_masks[index]);
        }

        WorldOverlayHost = FullRect("WorldOverlayHost");
        WorldOverlayHost.ClipContents = true;
        _worldLayer.AddChild(WorldOverlayHost);
        HudHost = FullRect("HudHost");
        _hudLayer.AddChild(HudHost);
        GameplayGumpHost = FullRect("GameplayGumpHost");
        ShellMenuHost = FullRect("ShellMenuHost");
        _menuLayer.AddChild(GameplayGumpHost);
        _menuLayer.AddChild(ShellMenuHost);
        DebugHost = FullRect("DebugHost");
        _debugLayer.AddChild(DebugHost);
        ContextHost = FullRect("ContextTooltipHost");
        _contextLayer.AddChild(ContextHost);

        Gumps = new GodotGumpWindowManager(GameplayGumpHost);
        _debug = new GodotClientDebugPresenter(DebugHost);
        UpdateApplicationSize(CurrentApplicationSize());
    }

    public CanvasLayer WorldLayer => _worldLayer;
    public CanvasLayer HudLayer => _hudLayer;
    public CanvasLayer MenuLayer => _menuLayer;
    public CanvasLayer DebugLayer => _debugLayer;
    public CanvasLayer ContextLayer => _contextLayer;
    public Control WorldOverlayHost { get; }
    public Control HudHost { get; }
    public Control GameplayGumpHost { get; }
    public Control ShellMenuHost { get; }
    public Control DebugHost { get; }
    public Control ContextHost { get; }
    public GodotGumpWindowManager Gumps { get; }
    public GameplayFrameLayout Layout => _layout;
    public GodotClientDebugPage DebugPage => _debug.Page;

    public GodotSessionUiLease BeginSession(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_session is not null)
            throw new InvalidOperationException("A client presentation session is already active.");
        if (Gumps.GetSnapshot().OpenWindows != 0)
            throw new InvalidOperationException("The persistent gump host was not clean at session start.");

        var world = FullRect($"{name}World");
        var hud = FullRect($"{name}Hud");
        var context = FullRect($"{name}Context");
        WorldOverlayHost.AddChild(world);
        HudHost.AddChild(hud);
        ContextHost.AddChild(context);
        _session = new GodotSessionUiLease(this, world, hud, context, Gumps);
        _session.SetContextVisible(!_shellModalVisible);
        return _session;
    }

    public void SetShellModalVisible(bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _shellModalVisible = visible;
        ShellMenuHost.Visible = visible;
        if (visible) _session?.SuppressTransientContext();
        _session?.SetContextVisible(!visible);
    }

    public bool ContainsWorldPointer(Vector2 applicationPoint) =>
        _layout.ContainsApplicationPoint(new ClientPixelPoint(
            (int)MathF.Floor(applicationPoint.X),
            (int)MathF.Floor(applicationPoint.Y)));

    public bool TryToFrameLocal(Vector2 applicationPoint, out Vector2 frameLocalPoint)
    {
        if (_layout.TryToFrameLocal(
                new ClientPixelPoint(
                    (int)MathF.Floor(applicationPoint.X),
                    (int)MathF.Floor(applicationPoint.Y)),
                out ClientPixelPoint local))
        {
            frameLocalPoint = new Vector2(local.X, local.Y);
            return true;
        }
        frameLocalPoint = default;
        return false;
    }

    public void UpdateLayoutIfNeeded()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ClientPixelSize current = CurrentApplicationSize();
        if (current != _layout.Application)
            UpdateApplicationSize(current);
    }

    internal void UpdateApplicationSizeForAcceptance(ClientPixelSize application) =>
        UpdateApplicationSize(application);

    public void SetSelectedOutputShape(ClientPixelSize selectedOutput)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!selectedOutput.IsValid)
            throw new ArgumentException("A finite selected output shape is required.", nameof(selectedOutput));
        _selectedOutput = selectedOutput;
        UpdateApplicationSize(CurrentApplicationSize());
    }

    public void CycleDebug(Func<GodotClientDebugPage, string> content)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _debug.Cycle(content ?? throw new ArgumentNullException(nameof(content)));
    }

    public void ResetDebug() => _debug.Reset();

    public GodotClientUiCompositionSnapshot GetSnapshot() => new(
        new GodotApplicationWorkspaceSnapshot(
            _layout.Application,
            _layout.SelectedOutput,
            _layout.Frame,
            _layout.UnusedLeft,
            _layout.UnusedTop,
            _layout.UnusedRight,
            _layout.UnusedBottom,
            _layout.UnusedArea,
            _layout.UnusedArea != 0),
        WorldOverlayHost.GetChildCount(),
        HudHost.GetChildCount(),
        GameplayGumpHost.GetChildCount(),
        ShellMenuHost.GetChildCount(),
        DebugHost.GetChildCount(),
        ContextHost.GetChildCount(),
        Gumps.GetSnapshot(),
        _debug.Page,
        _session is not null,
        _shellModalVisible);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session?.DisposeFromOwner();
        _session = null;
        Gumps.Dispose();
        _debug.Dispose();
    }

    internal void RetireSession(GodotSessionUiLease session)
    {
        if (!ReferenceEquals(_session, session)) return;
        Gumps.RetireSession();
        _session = null;
    }

    private void UpdateApplicationSize(ClientPixelSize application)
    {
        _layout = GameplayFrameLayout.MaxFit(application, _selectedOutput);
        ClientPixelRect frame = _layout.Frame;
        SetRect(WorldOverlayHost, frame.X, frame.Y, frame.Width, frame.Height);
        SetRect(_masks[0], 0, 0, frame.X, application.Height);
        SetRect(_masks[1], frame.X, 0, frame.Width, frame.Y);
        SetRect(_masks[2], frame.Right, 0, application.Width - frame.Right, application.Height);
        SetRect(_masks[3], frame.X, frame.Bottom, frame.Width, application.Height - frame.Bottom);
        Gumps.ClampAll(new Vector2(application.Width, application.Height));
    }

    private ClientPixelSize CurrentApplicationSize()
    {
        Vector2 viewport = _owner.GetViewport().GetVisibleRect().Size;
        int width = Math.Clamp((int)MathF.Round(viewport.X), 1, ClientPixelSize.MaximumDimension);
        int height = Math.Clamp((int)MathF.Round(viewport.Y), 1, ClientPixelSize.MaximumDimension);
        return ClientPixelSize.Create(width, height);
    }

    private static CanvasLayer Layer(string name, int layer) => new()
    {
        Name = name,
        Layer = layer,
    };

    private static Control FullRect(string name)
    {
        var result = new Control
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        result.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return result;
    }

    private static void SetRect(Control control, float x, float y, float width, float height)
    {
        control.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        control.Position = new Vector2(x, y);
        control.Size = new Vector2(Math.Max(0, width), Math.Max(0, height));
    }
}

/// <summary>One session's transient contribution to the persistent F1.7 category hosts.</summary>
public sealed class GodotSessionUiLease : IDisposable
{
    private const int MaximumTransientSuppressors = 8;
    private readonly GodotClientUiComposition _owner;
    private readonly List<Action> _transientSuppressors = [];
    private bool _disposed;

    internal GodotSessionUiLease(
        GodotClientUiComposition owner,
        Control world,
        Control hud,
        Control context,
        GodotGumpWindowManager gumps)
    {
        _owner = owner;
        WorldHost = world;
        HudHost = hud;
        ContextHost = context;
        Gumps = gumps;
        ContextMenu = new GodotContextMenuPresenter(context);
        RegisterTransientSuppressor(ContextMenu.Close);
    }

    public Control WorldHost { get; }
    public Control HudHost { get; }
    public Control ContextHost { get; }
    public GodotGumpWindowManager Gumps { get; }
    public GodotContextMenuPresenter ContextMenu { get; }

    public GameplayFrameLayout GameplayFrame => _owner.Layout;

    public bool ContainsWorldPointer(Vector2 applicationPoint) =>
        _owner.ContainsWorldPointer(applicationPoint);

    public bool TryToFrameLocal(Vector2 applicationPoint, out Vector2 frameLocalPoint) =>
        _owner.TryToFrameLocal(applicationPoint, out frameLocalPoint);

    public void RegisterTransientSuppressor(Action suppress)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(suppress);
        if (_transientSuppressors.Count >= MaximumTransientSuppressors)
            throw new InvalidOperationException("The bounded transient-context registry is full.");
        _transientSuppressors.Add(suppress);
    }

    public void SuppressTransientContext()
    {
        if (_disposed) return;
        foreach (Action suppress in _transientSuppressors) suppress();
    }

    internal void SetContextVisible(bool visible)
    {
        if (!_disposed && GodotObject.IsInstanceValid(ContextHost)) ContextHost.Visible = visible;
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeCore();
        _owner.RetireSession(this);
    }

    internal void DisposeFromOwner()
    {
        if (_disposed) return;
        DisposeCore();
    }

    private void DisposeCore()
    {
        _disposed = true;
        SuppressTransientContextCore();
        Gumps.RetireSession();
        Retire(WorldHost);
        Retire(HudHost);
        Retire(ContextHost);
        _transientSuppressors.Clear();
    }

    private void SuppressTransientContextCore()
    {
        foreach (Action suppress in _transientSuppressors) suppress();
    }

    private static void Retire(Control control)
    {
        if (!GodotObject.IsInstanceValid(control)) return;
        control.GetParent()?.RemoveChild(control);
        control.QueueFree();
    }
}

/// <summary>Bounded, presentation-only right-click menu over already disclosed target data.</summary>
public sealed class GodotContextMenuPresenter
{
    private const float Margin = 8f;
    private readonly Control _host;
    private PanelContainer? _panel;
    private string? _target;
    private Action? _select;
    private Action? _interact;
    private Func<bool>? _isValid;

    internal GodotContextMenuPresenter(Control host) =>
        _host = host ?? throw new ArgumentNullException(nameof(host));

    public bool Visible => _panel?.Visible == true;

    public void Show(
        string target,
        Vector2 applicationPoint,
        Action select,
        Action interact,
        Func<bool>? isValid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(interact);
        Close();
        _target = target;
        _select = select;
        _interact = interact;
        _isValid = isValid;
        _panel = new PanelContainer
        {
            Name = "WorldContextMenu",
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(180, 0),
        };
        var stack = new VBoxContainer();
        stack.AddChild(new Label { Text = target, MouseFilter = Control.MouseFilterEnum.Ignore });
        stack.AddChild(ActionButton("ContextSelect", "Select", () => Invoke(_select)));
        stack.AddChild(ActionButton("ContextInteract", "Interact / Use", () => Invoke(_interact)));
        stack.AddChild(ActionButton("ContextCancel", "Cancel", Close));
        _panel.AddChild(stack);
        _host.AddChild(_panel);
        _panel.ResetSize();
        Place(applicationPoint);
    }

    public bool ResolveInput(InputEvent inputEvent)
    {
        if (_panel is null || !_panel.Visible) return false;
        if (inputEvent is not InputEventMouseButton { Pressed: true } mouse) return false;
        if (_panel.GetGlobalRect().HasPoint(mouse.Position)) return true;
        Close();
        return true;
    }

    public void ClampToApplication() => Place(_panel?.Position ?? Vector2.Zero);

    public void UpdateValidity()
    {
        if (Visible && _isValid?.Invoke() == false) Close();
    }

    public void Close()
    {
        if (_panel is not null && GodotObject.IsInstanceValid(_panel))
        {
            _panel.GetParent()?.RemoveChild(_panel);
            _panel.QueueFree();
        }
        _panel = null;
        _target = null;
        _select = null;
        _interact = null;
        _isValid = null;
    }

    public GodotContextMenuSnapshot GetSnapshot() => new(
        Visible,
        _target,
        Visible ? 3 : 0,
        _panel?.Position ?? Vector2.Zero,
        _panel?.Size ?? Vector2.Zero);

    private Button ActionButton(string name, string label, Action action)
    {
        var button = new Button
        {
            Name = name,
            Text = label,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        button.Pressed += action;
        return button;
    }

    private void Invoke(Action? action)
    {
        action?.Invoke();
        Close();
    }

    private void Place(Vector2 requested)
    {
        if (_panel is null) return;
        Vector2 application = _host.GetViewportRect().Size;
        Vector2 required = _panel.GetCombinedMinimumSize();
        if (required.X <= 0 || required.Y <= 0) required = new Vector2(180, 150);
        _panel.Position = new Vector2(
            Math.Clamp(requested.X, Margin, Math.Max(Margin, application.X - required.X - Margin)),
            Math.Clamp(requested.Y, Margin, Math.Max(Margin, application.Y - required.Y - Margin)));
    }
}

internal sealed class GodotClientDebugPresenter : IDisposable
{
    private readonly Control _host;
    private PanelContainer? _panel;

    internal GodotClientDebugPresenter(Control host) => _host = host;

    internal GodotClientDebugPage Page { get; private set; }

    internal void Cycle(Func<GodotClientDebugPage, string> content)
    {
        Page = Page == GodotClientDebugPage.Content
            ? GodotClientDebugPage.Off
            : Page + 1;
        Replace(Page == GodotClientDebugPage.Off ? null : content(Page));
    }

    internal void Reset()
    {
        Page = GodotClientDebugPage.Off;
        Replace(null);
    }

    public void Dispose() => Reset();

    private void Replace(string? content)
    {
        if (_panel is not null && GodotObject.IsInstanceValid(_panel))
        {
            _panel.GetParent()?.RemoveChild(_panel);
            _panel.QueueFree();
        }
        _panel = null;
        if (content is null) return;
        _panel = new PanelContainer
        {
            Name = $"Debug{Page}",
            OffsetLeft = 12,
            OffsetTop = 48,
            OffsetRight = 500,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _panel.AddChild(new Label
        {
            Text = content,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _host.AddChild(_panel);
    }
}
