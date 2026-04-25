using Godot;
using UltraSim.Client;
using UltraSim.Discovery;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class ClientAppShell : Control
{
    public event Action<ClientAppScreen>? ScreenRequested;
    public event Action<ClientDirectConnectRequest>? DirectConnectRequested;
    public event Action? ServerBrowserRefreshRequested;
    public event Action? PreviewRequested;
    public event Action? ResumeRequested;
    public event Action? BackRequested;

    private Label? _title;
    private Label? _body;
    private ColorRect? _shade;
    private PanelContainer? _frame;
    private VBoxContainer? _buttonColumn;
    private LineEdit? _hostField;
    private LineEdit? _portField;
    private ClientAppScreen _currentScreen = ClientAppScreen.Start;
    private ServerBrowserSnapshot? _serverBrowserSnapshot;
    private string _lastHostText = "127.0.0.1";
    private string _lastPortText = "42042";

    public override void _Ready()
    {
        BuildInterface();
        ApplyViewportLayout();
        ShowScreen(ClientAppScreen.Start);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            ApplyViewportLayout();
        }
    }

    public void ShowScreen(ClientAppScreen screen)
    {
        EnsureInterface();
        ClearButtons();
        _currentScreen = screen;

        switch (screen)
        {
            case ClientAppScreen.Start:
                SetText("UltraSim", "Local shell online. Network login and discovery will attach here next.");
                AddButton("Preview World", () => PreviewRequested?.Invoke());
                AddButton("Server Browser", () => ScreenRequested?.Invoke(ClientAppScreen.ServerBrowser));
                AddButton("Direct Connect", () => ScreenRequested?.Invoke(ClientAppScreen.DirectConnect));
                AddButton("Settings", () => ScreenRequested?.Invoke(ClientAppScreen.Settings));
                break;
            case ClientAppScreen.ServerBrowser:
                ShowServerBrowser();
                AddButton("Direct Connect", () => ScreenRequested?.Invoke(ClientAppScreen.DirectConnect));
                AddButton("Refresh", () => ServerBrowserRefreshRequested?.Invoke());
                AddBackButton();
                break;
            case ClientAppScreen.DirectConnect:
                SetText("Direct Connect", "Connect to a current UltraSim UDP server.");
                _hostField = AddTextField("Host", _lastHostText);
                _portField = AddTextField("Port", _lastPortText);
                AddButton("Connect", SubmitDirectConnect);
                AddBackButton();
                break;
            case ClientAppScreen.Settings:
                SetText("Settings", "Local settings will persist nameplate, chat, HUD, debug, and content preferences.");
                AddButton("Toggle Debug Placeholder", () => { });
                AddBackButton();
                break;
            case ClientAppScreen.SessionStatus:
                SetText("Session", "Preview session active. Remote session state will replace this in G3.");
                AddButton("Resume", () => ResumeRequested?.Invoke());
                AddButton("Options", () => ScreenRequested?.Invoke(ClientAppScreen.InSessionOptions));
                break;
            case ClientAppScreen.InSessionOptions:
                SetText("Options", "In-session menu placeholder.");
                AddButton("Resume", () => ResumeRequested?.Invoke());
                AddButton("Settings", () => ScreenRequested?.Invoke(ClientAppScreen.Settings));
                AddButton("Disconnect", () => ScreenRequested?.Invoke(ClientAppScreen.Disconnected));
                break;
            case ClientAppScreen.Disconnected:
                SetText("Disconnected", "Session-owned world state has been cleared.");
                AddButton("Back To Start", () => ScreenRequested?.Invoke(ClientAppScreen.Start));
                break;
            case ClientAppScreen.Error:
                SetText("Error", "A client error occurred.");
                AddButton("Back To Start", () => ScreenRequested?.Invoke(ClientAppScreen.Start));
                break;
        }
    }

    public void SetServerBrowserSnapshot(ServerBrowserSnapshot snapshot)
    {
        _serverBrowserSnapshot = snapshot;
        if (_currentScreen == ClientAppScreen.ServerBrowser)
        {
            ShowScreen(ClientAppScreen.ServerBrowser);
        }
    }

    private void BuildInterface()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        _shade = new ColorRect
        {
            Color = new Color(0.02f, 0.025f, 0.03f, 0.72f)
        };
        _shade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_shade);

        _frame = new PanelContainer
        {
            CustomMinimumSize = new Vector2(460, 360)
        };
        AddChild(_frame);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        _frame.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        margin.AddChild(column);

        _title = new Label
        {
            Text = "UltraSim",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _title.AddThemeFontSizeOverride("font_size", 28);
        column.AddChild(_title);

        _body = new Label
        {
            Text = string.Empty,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        column.AddChild(_body);

        _buttonColumn = new VBoxContainer();
        _buttonColumn.AddThemeConstantOverride("separation", 8);
        column.AddChild(_buttonColumn);
    }

    private void ApplyViewportLayout()
    {
        Vector2 viewportSize = GetViewportRect().Size;
        Position = Vector2.Zero;

        if (_shade is not null)
        {
            _shade.Position = Vector2.Zero;
        }

        if (_frame is not null)
        {
            Vector2 frameSize = _frame.CustomMinimumSize;
            Vector2 framePosition = new(
                Mathf.Round((viewportSize.X - frameSize.X) * 0.5f),
                Mathf.Round((viewportSize.Y - frameSize.Y) * 0.5f));
            SetControlRect(_frame, framePosition, frameSize);
        }
    }

    private static void SetControlRect(Control control, Vector2 position, Vector2 size)
    {
        control.SetAnchorsPreset(LayoutPreset.TopLeft);
        control.OffsetLeft = position.X;
        control.OffsetTop = position.Y;
        control.OffsetRight = position.X + size.X;
        control.OffsetBottom = position.Y + size.Y;
    }

    private void EnsureInterface()
    {
        if (_buttonColumn is null)
        {
            BuildInterface();
        }
    }

    private void SetText(string title, string body)
    {
        if (_title is not null)
        {
            _title.Text = title;
        }

        if (_body is not null)
        {
            _body.Text = body;
        }
    }

    private void ClearButtons()
    {
        if (_buttonColumn is null)
        {
            return;
        }

        foreach (Node child in _buttonColumn.GetChildren())
        {
            child.QueueFree();
        }

        _hostField = null;
        _portField = null;
    }

    private void AddButton(string text, Action pressed)
    {
        if (_buttonColumn is null)
        {
            return;
        }

        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 36)
        };
        button.Pressed += pressed;
        _buttonColumn.AddChild(button);
    }

    private void AddBackButton()
    {
        AddButton("Back", () => BackRequested?.Invoke());
    }

    private void ShowServerBrowser()
    {
        ServerBrowserSnapshot? snapshot = _serverBrowserSnapshot;
        if (snapshot is null)
        {
            SetText("Server Browser", "No discovery providers are configured yet. Direct connect remains available.");
            return;
        }

        string providerName = snapshot.Providers.Count == 0
            ? snapshot.ActiveProviderId
            : ResolveActiveProviderName(snapshot);
        string status = snapshot.IsRefreshing
            ? "refreshing"
            : snapshot.LastError is not null
                ? $"error: {snapshot.LastError}"
                : $"{snapshot.Rows.Count} server(s)";

        SetText("Server Browser", $"{providerName} - {status}");

        for (int i = 0; i < snapshot.Rows.Count; i++)
        {
            ServerDiscoveryRow row = snapshot.Rows[i];
            AddButton(FormatServerRow(row), () => SubmitServerBrowserRow(row));
        }

        if (snapshot.Rows.Count == 0 && snapshot.LastError is null && !snapshot.IsRefreshing)
        {
            AddDisabledButton("No servers found");
        }
    }

    private static string ResolveActiveProviderName(ServerBrowserSnapshot snapshot)
    {
        for (int i = 0; i < snapshot.Providers.Count; i++)
        {
            if (snapshot.Providers[i].IsSelected)
            {
                return snapshot.Providers[i].DisplayName;
            }
        }

        return snapshot.ActiveProviderId;
    }

    private static string FormatServerRow(ServerDiscoveryRow row)
    {
        string users = row.CurrentUsers.HasValue && row.MaxUsers.HasValue
            ? $" {row.CurrentUsers}/{row.MaxUsers}"
            : string.Empty;
        string ping = row.PingMs.HasValue
            ? $" {row.PingMs}ms"
            : string.Empty;
        return $"{row.DisplayName} - {row.Host}:{row.Port}{users}{ping}";
    }

    private void AddDisabledButton(string text)
    {
        if (_buttonColumn is null)
        {
            return;
        }

        var button = new Button
        {
            Text = text,
            Disabled = true,
            CustomMinimumSize = new Vector2(0, 36)
        };
        _buttonColumn.AddChild(button);
    }

    private void SubmitServerBrowserRow(ServerDiscoveryRow row)
    {
        if (!ClientDirectConnectRequest.TryCreate(row, out ClientDirectConnectRequest request, out string error))
        {
            SetText("Server Browser", error);
            return;
        }

        _lastHostText = request.Host;
        _lastPortText = request.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        DirectConnectRequested?.Invoke(request);
    }

    private LineEdit? AddTextField(string label, string value)
    {
        if (_buttonColumn is null)
        {
            return null;
        }

        var field = new LineEdit
        {
            PlaceholderText = label,
            Text = value
        };
        _buttonColumn.AddChild(field);
        return field;
    }

    private void SubmitDirectConnect()
    {
        if (!ClientDirectConnectRequest.TryCreate(
            _hostField?.Text,
            _portField?.Text,
            out ClientDirectConnectRequest request,
            out string error))
        {
            _lastHostText = _hostField?.Text ?? _lastHostText;
            _lastPortText = _portField?.Text ?? _lastPortText;
            SetText("Direct Connect", error);
            return;
        }

        _lastHostText = request.Host;
        _lastPortText = request.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        DirectConnectRequested?.Invoke(request);
    }
}
