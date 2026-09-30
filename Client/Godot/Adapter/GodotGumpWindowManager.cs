using Godot;
using UltraSim.UI;

namespace UltraSim.Client.Godot;

/// <summary>
/// Engine-thread-bound native owner for modeless gameplay gumps. Semantic models and actions stay
/// in UiClientRuntime; this manager owns only placement, z order, drag capture, and native lifetime.
/// </summary>
public sealed class GodotGumpWindowManager : IDisposable
{
    private const int MaximumWindows = 16;
    private const float HeaderHeight = 26f;
    private const float CascadeStep = 32f;
    private const float ApplicationMargin = 8f;
    private const float MinimumRecoverableWidth = 120f;
    private readonly Control _host;
    private readonly List<Entry> _entries = [];
    private Entry? _dragging;
    private ulong _frontSequence;
    private int _cascadeIndex;
    private bool _disposed;

    public GodotGumpWindowManager(Control host) =>
        _host = host ?? throw new ArgumentNullException(nameof(host));

    internal int WindowCount => _entries.Count;

    public Control Host => _host;
    /// <summary>Native UI ownership transition; never an authoritative Game event.</summary>
    public event Action<bool>? WindowChanged;

    internal GodotUiRenderHandle HandleAt(int index) => _entries[index].Handle;

    internal GodotUiRenderHandle HandleForAcceptance(UiViewInstanceId instance) =>
        _entries.Find(entry => entry.Handle.Instance == instance)?.Handle ??
        throw new InvalidOperationException("The requested native gump is not attached.");

    internal void Attach(GodotUiRenderHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(handle);
        if (_entries.Count >= MaximumWindows)
            throw new InvalidOperationException("The bounded gameplay-gump host is full.");
        if (_entries.Exists(entry => ReferenceEquals(entry.Handle, handle)))
            throw new InvalidOperationException("The gameplay gump is already attached.");

        Vector2 contentSize = handle.Root.Size;
        var wrapper = new Control
        {
            Name = $"GumpWindow_{handle.Instance.Value}",
            Size = new Vector2(contentSize.X, contentSize.Y + HeaderHeight),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        var background = new Panel
        {
            Name = "WindowBackground",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        wrapper.AddChild(background);
        var header = new Button
        {
            Name = "WindowDragHandle",
            Text = handle.Title,
            Position = Vector2.Zero,
            Size = new Vector2(contentSize.X, HeaderHeight),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.Move,
        };
        wrapper.AddChild(header);
        handle.Root.Position = new Vector2(0, HeaderHeight);
        wrapper.AddChild(handle.Root);
        _host.AddChild(wrapper);

        var entry = new Entry(handle, wrapper, header);
        _entries.Add(entry);
        WindowChanged?.Invoke(true);
        handle.NativeWindowRoot = wrapper;
        HookBringToFront(handle.Root, entry);
        header.GuiInput += input => HandleHeaderInput(entry, input);
        PlaceInitial(entry);
        BringToFront(entry);
    }

    internal void Detach(GodotUiRenderHandle handle)
    {
        int index = _entries.FindIndex(entry => ReferenceEquals(entry.Handle, handle));
        if (index < 0)
        {
            handle.CloseNative();
            return;
        }
        Entry entry = _entries[index];
        if (ReferenceEquals(_dragging, entry)) _dragging = null;
        _entries.RemoveAt(index);
        WindowChanged?.Invoke(false);
        handle.CloseNative();
    }

    internal bool ContainsWindowSurface(GodotUiRenderHandle handle, Vector2 applicationPoint)
    {
        Entry? entry = _entries.Find(candidate => ReferenceEquals(candidate.Handle, handle));
        return entry is not null && GodotObject.IsInstanceValid(entry.Wrapper) &&
            entry.Wrapper.IsVisibleInTree() && entry.Wrapper.GetGlobalRect().HasPoint(applicationPoint);
    }

    internal bool BringToFront(GodotUiRenderHandle handle)
    {
        Entry? entry = _entries.Find(candidate => ReferenceEquals(candidate.Handle, handle));
        if (entry is null) return false;
        BringToFront(entry);
        return true;
    }

    public void ClampAll(Vector2 applicationSize)
    {
        if (_disposed) return;
        foreach (Entry entry in _entries) Clamp(entry, applicationSize);
    }

    public void RetireSession()
    {
        if (_disposed) return;
        _dragging = null;
        while (_entries.Count != 0)
        {
            Entry entry = _entries[^1];
            _entries.RemoveAt(_entries.Count - 1);
            entry.Handle.CloseNative();
        }
        _cascadeIndex = 0;
    }

    public GodotGumpWindowManagerSnapshot GetSnapshot() => new(
        _entries.Count,
        _host.GetChildCount(),
        _dragging is not null,
        _frontSequence,
        _cascadeIndex);

    internal Vector2 GetWindowPosition(GodotUiRenderHandle handle)
    {
        Entry? entry = _entries.Find(candidate => ReferenceEquals(candidate.Handle, handle));
        return entry?.Wrapper.Position ?? throw new InvalidOperationException("The gump is not attached.");
    }

    internal bool DragForAcceptance(GodotUiRenderHandle handle, Vector2 requestedPosition)
    {
        Entry? entry = _entries.Find(candidate => ReferenceEquals(candidate.Handle, handle));
        if (entry is null) return false;
        entry.Wrapper.Position = requestedPosition;
        Clamp(entry, _host.GetViewportRect().Size);
        BringToFront(entry);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        RetireSession();
        _disposed = true;
    }

    private void PlaceInitial(Entry entry)
    {
        Vector2 application = _host.GetViewportRect().Size;
        int slot = _cascadeIndex++ % 8;
        float baseX = entry.Wrapper.Size.X + ApplicationMargin * 2 <= application.X
            ? (application.X - entry.Wrapper.Size.X) * 0.5f
            : ApplicationMargin;
        float baseY = entry.Wrapper.Size.Y + ApplicationMargin * 2 <= application.Y
            ? (application.Y - entry.Wrapper.Size.Y) * 0.5f
            : ApplicationMargin;
        entry.Wrapper.Position = new Vector2(
            baseX + slot * CascadeStep,
            baseY + slot * CascadeStep);
        Clamp(entry, application);
    }

    private void BringToFront(Entry entry)
    {
        int index = _entries.IndexOf(entry);
        if (index >= 0 && index != _entries.Count - 1)
        {
            _entries.RemoveAt(index);
            _entries.Add(entry);
        }
        if (entry.Wrapper.GetParent() == _host)
            _host.MoveChild(entry.Wrapper, _host.GetChildCount() - 1);
        if (_frontSequence != ulong.MaxValue) _frontSequence++;
    }

    private void HandleHeaderInput(Entry entry, InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    BringToFront(entry);
                    _dragging = entry;
                }
                else if (ReferenceEquals(_dragging, entry))
                {
                    _dragging = null;
                }
                entry.Header.AcceptEvent();
                break;
            case InputEventMouseMotion motion when ReferenceEquals(_dragging, entry):
                entry.Wrapper.Position += motion.Relative;
                Clamp(entry, _host.GetViewportRect().Size);
                entry.Header.AcceptEvent();
                break;
        }
    }

    private void HookBringToFront(Control control, Entry entry)
    {
        control.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true }) BringToFront(entry);
        };
        foreach (Node child in control.GetChildren())
            if (child is Control nested) HookBringToFront(nested, entry);
    }

    private static void Clamp(Entry entry, Vector2 application)
    {
        Vector2 size = entry.Wrapper.Size;
        float maximumX = size.X + ApplicationMargin * 2 <= application.X
            ? application.X - size.X - ApplicationMargin
            : Math.Max(ApplicationMargin, application.X - MinimumRecoverableWidth);
        float maximumY = size.Y + ApplicationMargin * 2 <= application.Y
            ? application.Y - size.Y - ApplicationMargin
            : Math.Max(ApplicationMargin, application.Y - HeaderHeight - ApplicationMargin);
        entry.Wrapper.Position = new Vector2(
            Math.Clamp(
                entry.Wrapper.Position.X,
                ApplicationMargin,
                maximumX),
            Math.Clamp(
                entry.Wrapper.Position.Y,
                ApplicationMargin,
                maximumY));
    }

    private sealed record Entry(
        GodotUiRenderHandle Handle,
        Control Wrapper,
        Button Header);
}
