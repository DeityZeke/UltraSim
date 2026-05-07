using System.Globalization;
using Godot;
using UltraSim.Client;
using UltraSim.Discovery;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class ClientAppShell : Control
{
    public event Action<ClientAppScreen>? ScreenRequested;
    public event Action<ClientDirectConnectRequest>? DirectConnectRequested;
    public event Action<ServerDiscoveryRow>? ServerBrowserRowSubmitted;
    public event Action<ClientDirectConnectRequest>? ManualDirectConnectSubmitted;
    public event Action<ServerDiscoveryRow>? ServerBrowserFavoriteRequested;
    public event Action<ServerDiscoveryRow>? ServerBrowserUnfavoriteRequested;
    public event Action? ServerBrowserClearHistoryRequested;
    public event Action? ServerBrowserRefreshRequested;
    public event Action<string>? ServerBrowserProviderRequested;
    public event Action? PreviewRequested;
    public event Action? ResumeRequested;
    public event Action? BackRequested;
    public event Action<bool>? OverheadChatVisibilityChanged;
    public event Action<bool>? NameplateVisibilityChanged;
    public event Action<bool>? OverheadHealthBarVisibilityChanged;
    public event Action<bool>? ChatPanelVisibilityChanged;
    public event Action<int>? MaxVisibleWorldUiChanged;
    public event Action<float>? WorldUiScaleChanged;

    private const float StartPanelWidth = 360.0f;
    private const float StartPanelHeight = 270.0f;
    private const float SettingsPanelWidth = 420.0f;
    private const float SettingsPanelHeight = 350.0f;
    private const float DirectConnectPanelWidth = 480.0f;
    private const float DirectConnectPanelHeight = 210.0f;
    private const float CompactPanelHeight = 220.0f;
    private const float BrowserMaxWidth = 1536.0f;
    private const float BrowserMaxHeight = 864.0f;
    private const float ViewportMargin = 48.0f;

    private ColorRect? _shade;
    private PanelContainer? _frame;
    private MarginContainer? _margin;
    private VBoxContainer? _rootColumn;
    private Label? _title;
    private Label? _subtitle;
    private Control? _contentRoot;
    private Label? _statusLabel;
    private LineEdit? _hostField;
    private LineEdit? _portField;
    private ItemList? _serverList;
    private Label? _serverDetailLabel;
    private Button? _favoriteButton;
    private Button? _clearHistoryButton;
    private Button? _joinButton;
    private ClientAppScreen _currentScreen = ClientAppScreen.Start;
    private ServerBrowserSnapshot? _serverBrowserSnapshot;
    private int _selectedServerIndex = -1;
    private string _lastHostText = "127.0.0.1";
    private string _lastPortText = "42042";
    private bool _showOverheadChat = true;
    private bool _showNameplates = true;
    private bool _showOverheadHealthBars = true;
    private bool _showChatPanel = true;
    private int _maxVisibleWorldUi = 32;
    private float _worldUiScale = 1.0f;

    public override void _Ready()
    {
        BuildShell();
        CallDeferred(MethodName.ShowInitialScreenIfEmpty);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            ApplyViewportLayout();
    }

    public void ShowScreen(ClientAppScreen screen)
    {
        EnsureShell();
        _currentScreen = screen;
        _selectedServerIndex = -1;
        ClearContent();

        switch (screen)
        {
            case ClientAppScreen.Start:
                ConfigureFrame(StartPanelWidth, StartPanelHeight);
                SetHeader("Ultra Sim", string.Empty);
                BuildStartMenu();
                SetStatus("select a mode");
                break;

            case ClientAppScreen.ServerBrowser:
                ConfigureFrame(ResolveBrowserSize().X, ResolveBrowserSize().Y);
                SetHeader("Multiplayer", "Server discovery and direct connection");
                BuildServerBrowser();
                break;

            case ClientAppScreen.DirectConnect:
                ConfigureFrame(DirectConnectPanelWidth, DirectConnectPanelHeight);
                SetHeader("Direct Connect", "Connect to a current UltraSim UDP server.");
                BuildDirectConnect();
                SetStatus("Enter an IP and port.");
                break;

            case ClientAppScreen.Settings:
                ConfigureFrame(SettingsPanelWidth, SettingsPanelHeight);
                SetHeader("Settings", "World UI settings");
                BuildSettings();
                SetStatus(string.Empty);
                break;

            case ClientAppScreen.SessionStatus:
                ConfigureFrame(StartPanelWidth, CompactPanelHeight);
                SetHeader("Session", "Preview session active.");
                AddPrimaryButton(_contentRoot, "Resume", () => ResumeRequested?.Invoke());
                AddPrimaryButton(_contentRoot, "Options", () => ScreenRequested?.Invoke(ClientAppScreen.InSessionOptions));
                SetStatus("session active");
                break;

            case ClientAppScreen.InSessionOptions:
                ConfigureFrame(StartPanelWidth, CompactPanelHeight);
                SetHeader("Options", "In-session menu");
                AddPrimaryButton(_contentRoot, "Resume", () => ResumeRequested?.Invoke());
                AddPrimaryButton(_contentRoot, "Settings", () => ScreenRequested?.Invoke(ClientAppScreen.Settings));
                AddPrimaryButton(_contentRoot, "Disconnect", () => ScreenRequested?.Invoke(ClientAppScreen.Disconnected));
                SetStatus("paused");
                break;

            case ClientAppScreen.Disconnected:
                ConfigureFrame(StartPanelWidth, CompactPanelHeight);
                SetHeader("Disconnected", "Session-owned world state has been cleared.");
                AddPrimaryButton(_contentRoot, "Back To Start", () => ScreenRequested?.Invoke(ClientAppScreen.Start));
                SetStatus("offline");
                break;

            case ClientAppScreen.Error:
                ConfigureFrame(StartPanelWidth, CompactPanelHeight);
                SetHeader("Error", "A client error occurred.");
                AddPrimaryButton(_contentRoot, "Back To Start", () => ScreenRequested?.Invoke(ClientAppScreen.Start));
                SetStatus("error");
                break;
        }

        ApplyViewportLayout();
    }

    public void ShowError(string message)
    {
        ShowScreen(ClientAppScreen.Error);
        SetHeader("Error", string.IsNullOrWhiteSpace(message) ? "A client error occurred." : message);
    }

    public void SetServerBrowserSnapshot(ServerBrowserSnapshot snapshot)
    {
        _serverBrowserSnapshot = snapshot;
    }

    public void SetPresentationSettings(
        bool showOverheadChat,
        bool showNameplates,
        bool showOverheadHealthBars,
        bool showChatPanel,
        int maxVisibleWorldUi,
        float worldUiScale)
    {
        _showOverheadChat = showOverheadChat;
        _showNameplates = showNameplates;
        _showOverheadHealthBars = showOverheadHealthBars;
        _showChatPanel = showChatPanel;
        _maxVisibleWorldUi = Math.Clamp(maxVisibleWorldUi, 1, 256);
        _worldUiScale = Mathf.Clamp(worldUiScale, 0.5f, 3.0f);
    }

    private void BuildShell()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        _shade = new ColorRect
        {
            Color = new Color(0.015f, 0.018f, 0.022f, 0.72f),
            MouseFilter = MouseFilterEnum.Stop
        };
        _shade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_shade);

        _frame = new PanelContainer();
        _frame.AddThemeStyleboxOverride("panel", CreatePanelStyle(new Color(0.075f, 0.082f, 0.092f, 0.96f), 8));
        AddChild(_frame);

        _margin = new MarginContainer();
        _margin.AddThemeConstantOverride("margin_left", 18);
        _margin.AddThemeConstantOverride("margin_top", 18);
        _margin.AddThemeConstantOverride("margin_right", 18);
        _margin.AddThemeConstantOverride("margin_bottom", 18);
        _frame.AddChild(_margin);

        _rootColumn = new VBoxContainer();
        _rootColumn.AddThemeConstantOverride("separation", 10);
        _margin.AddChild(_rootColumn);

        _title = CreateLabel("Ultra Sim", 28, HorizontalAlignment.Center);
        _rootColumn.AddChild(_title);

        _subtitle = CreateLabel(" ", 14, HorizontalAlignment.Center);
        _subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _rootColumn.AddChild(_subtitle);

        _contentRoot = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        _rootColumn.AddChild(_contentRoot);

        _statusLabel = CreateLabel(string.Empty, 13, HorizontalAlignment.Center);
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _rootColumn.AddChild(_statusLabel);
    }

    private void ShowInitialScreenIfEmpty()
    {
        if (_contentRoot?.GetChildCount() == 0)
        {
            ShowScreen(ClientAppScreen.Start);
        }
    }

    private void EnsureShell()
    {
        if (_rootColumn is null)
            BuildShell();
    }

    private void ConfigureFrame(float width, float height)
    {
        if (_frame is null)
            return;

        _frame.CustomMinimumSize = new Vector2(width, height);
    }

    private void ApplyViewportLayout()
    {
        Vector2 viewportSize = GetViewportRect().Size;
        Position = Vector2.Zero;

        if (_shade is not null)
            _shade.Position = Vector2.Zero;

        if (_frame is null)
            return;

        Vector2 requestedSize = _frame.CustomMinimumSize;
        if (_currentScreen == ClientAppScreen.ServerBrowser)
            requestedSize = ResolveBrowserSize();

        float maxWidth = Mathf.Max(240.0f, viewportSize.X - ViewportMargin);
        float maxHeight = Mathf.Max(180.0f, viewportSize.Y - ViewportMargin);
        Vector2 minimumSize = _currentScreen == ClientAppScreen.ServerBrowser
            ? Vector2.Zero
            : _frame.GetCombinedMinimumSize();
        Vector2 frameSize = new(
            Mathf.Min(Mathf.Max(requestedSize.X, minimumSize.X), maxWidth),
            Mathf.Min(requestedSize.Y > 0.0f ? requestedSize.Y : minimumSize.Y, maxHeight));
        Vector2 framePosition = new(
            Mathf.Round((viewportSize.X - frameSize.X) * 0.5f),
            Mathf.Round((viewportSize.Y - frameSize.Y) * 0.5f));

        SetControlRect(_frame, framePosition, frameSize);
    }

    private Vector2 ResolveBrowserSize()
    {
        Vector2 viewport = GetViewportRect().Size;
        float maxWidth = Mathf.Max(720.0f, viewport.X - ViewportMargin);
        float maxHeight = Mathf.Max(480.0f, viewport.Y - ViewportMargin);
        return new Vector2(
            Mathf.Min(Mathf.Clamp(viewport.X * 0.82f, 780.0f, BrowserMaxWidth), maxWidth),
            Mathf.Min(Mathf.Clamp(viewport.Y * 0.78f, 500.0f, BrowserMaxHeight), maxHeight));
    }

    private static void SetControlRect(Control control, Vector2 position, Vector2 size)
    {
        control.SetAnchorsPreset(LayoutPreset.TopLeft);
        control.OffsetLeft = position.X;
        control.OffsetTop = position.Y;
        control.OffsetRight = position.X + size.X;
        control.OffsetBottom = position.Y + size.Y;
    }

    private void SetHeader(string title, string subtitle)
    {
        if (_title is not null)
            _title.Text = title;

        if (_subtitle is not null)
        {
            _subtitle.Text = subtitle;
            _subtitle.Visible = !string.IsNullOrWhiteSpace(subtitle);
        }
    }

    private void SetStatus(string text)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = text;
            _statusLabel.Visible = !string.IsNullOrWhiteSpace(text);
        }
    }

    private void ClearContent()
    {
        if (_contentRoot is null)
            return;

        foreach (Node child in _contentRoot.GetChildren())
        {
            _contentRoot.RemoveChild(child);
            child.QueueFree();
        }

        _contentRoot.SizeFlagsVertical = _currentScreen == ClientAppScreen.ServerBrowser
            ? SizeFlags.ExpandFill
            : SizeFlags.ShrinkBegin;
        _hostField = null;
        _portField = null;
        _serverList = null;
        _serverDetailLabel = null;
        _favoriteButton = null;
        _clearHistoryButton = null;
        _joinButton = null;
    }

    private void BuildStartMenu()
    {
        if (_contentRoot is null)
            return;

        var column = CreateVBox(10);
        _contentRoot.AddChild(column);
        AddPrimaryButton(column, "Single Player", () => PreviewRequested?.Invoke());
        AddPrimaryButton(column, "Multiplayer", () => ScreenRequested?.Invoke(ClientAppScreen.ServerBrowser));
        AddPrimaryButton(column, "Settings", () => ScreenRequested?.Invoke(ClientAppScreen.Settings));
        AddPrimaryButton(column, "Exit", () => GetTree().Quit());
    }

    private void BuildServerBrowser()
    {
        if (_contentRoot is null)
            return;

        ServerBrowserSnapshot? snapshot = _serverBrowserSnapshot;

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _contentRoot.AddChild(scroll);

        var layout = CreateVBox(14);
        layout.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        layout.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.AddChild(layout);

        layout.AddChild(BuildProviderTabs(snapshot));

        var contentRow = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        contentRow.AddThemeConstantOverride("separation", 18);
        layout.AddChild(contentRow);

        PanelContainer leftPanel = CreateInnerPanel();
        leftPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        leftPanel.SizeFlagsStretchRatio = 3.0f;
        contentRow.AddChild(leftPanel);

        var leftMargin = CreateMargin(18);
        leftPanel.AddChild(leftMargin);
        var leftColumn = CreateVBox(12);
        leftColumn.SizeFlagsVertical = SizeFlags.ExpandFill;
        leftMargin.AddChild(leftColumn);

        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 12);
        leftColumn.AddChild(headerRow);
        Label listHeader = CreateLabel("Server List", 15, HorizontalAlignment.Left);
        listHeader.VerticalAlignment = VerticalAlignment.Center;
        headerRow.AddChild(listHeader);

        var headerSpacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        headerRow.AddChild(headerSpacer);

        AddPrimaryButton(headerRow, "Refresh", () => ServerBrowserRefreshRequested?.Invoke(), minWidth: 108.0f);
        leftColumn.AddChild(CreateDivider());
        leftColumn.AddChild(BuildFilterRow());

        _serverList = new ItemList
        {
            CustomMinimumSize = new Vector2(0.0f, 260.0f),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
            SameColumnWidth = true
        };
        _serverList.ItemSelected += index => SelectServerRow((int)index);
        _serverList.ItemActivated += index =>
        {
            SelectServerRow((int)index);
            SubmitSelectedServer();
        };
        leftColumn.AddChild(_serverList);

        PanelContainer rightPanel = CreateInnerPanel();
        rightPanel.CustomMinimumSize = new Vector2(320.0f, 0.0f);
        rightPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rightPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        rightPanel.SizeFlagsStretchRatio = 1.0f;
        contentRow.AddChild(rightPanel);

        var rightMargin = CreateMargin(18);
        rightPanel.AddChild(rightMargin);
        var rightColumn = CreateVBox(12);
        rightColumn.SizeFlagsVertical = SizeFlags.ExpandFill;
        rightMargin.AddChild(rightColumn);
        rightColumn.AddChild(CreateLabel("Server Details", 15, HorizontalAlignment.Left));
        rightColumn.AddChild(CreateDivider());
        _serverDetailLabel = CreateLabel("Select a server to view detailed information.", 13, HorizontalAlignment.Left);
        _serverDetailLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _serverDetailLabel.VerticalAlignment = VerticalAlignment.Top;
        _serverDetailLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
        rightColumn.AddChild(_serverDetailLabel);
        _favoriteButton = AddPrimaryButton(rightColumn, "Add Favorite", SubmitFavoriteToggle, minWidth: 180.0f);
        _favoriteButton.Disabled = true;
        _clearHistoryButton = AddPrimaryButton(rightColumn, "Clear History", () => ServerBrowserClearHistoryRequested?.Invoke(), minWidth: 180.0f);
        _clearHistoryButton.Visible = snapshot?.ActiveProviderId == "history";

        var footerStatus = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 3.0f
        };
        layout.AddChild(footerStatus);
        layout.AddChild(CreateDivider());

        var footerButtons = new HBoxContainer();
        footerButtons.AddThemeConstantOverride("separation", 18);
        layout.AddChild(footerButtons);
        AddPrimaryButton(footerButtons, "Direct Connect", () => ScreenRequested?.Invoke(ClientAppScreen.DirectConnect), minWidth: 200.0f);
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        footerButtons.AddChild(spacer);
        _joinButton = AddPrimaryButton(footerButtons, "Join", SubmitSelectedServer, minWidth: 180.0f);
        _joinButton.Disabled = true;
        AddPrimaryButton(footerButtons, "Back", () => BackRequested?.Invoke(), minWidth: 180.0f);

        PopulateServerList(snapshot, footerStatus);
    }

    private Control BuildProviderTabs(ServerBrowserSnapshot? snapshot)
    {
        var band = new PanelContainer();
        band.AddThemeStyleboxOverride("panel", CreatePanelStyle(new Color(0.11f, 0.12f, 0.135f, 0.95f), 8));
        var margin = CreateMargin(8, 6, 8, 6);
        band.AddChild(margin);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        margin.AddChild(row);

        if (snapshot is null || snapshot.Providers.Count == 0)
        {
            Button manual = AddPrimaryButton(row, "Manual", () => { }, minWidth: 128.0f);
            manual.Disabled = true;
            return band;
        }

        for (int i = 0; i < snapshot.Providers.Count; i++)
        {
            ServerBrowserProviderSnapshot provider = snapshot.Providers[i];
            Button tab = AddPrimaryButton(row, provider.DisplayName, () => ServerBrowserProviderRequested?.Invoke(provider.ProviderId), minWidth: 132.0f);
            tab.Disabled = provider.IsSelected;
        }

        var filler = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(filler);
        return band;
    }

    private Control BuildFilterRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(CreateFilterField("Search", 180.0f, expand: true));
        row.AddChild(CreateFilterField("Region", 110.0f));
        row.AddChild(CreateFilterField("Language", 110.0f));
        row.AddChild(CreateOption(["Trust: Any", "Trust: Unverified", "Trust: Verified", "Trust: Official"], 132.0f));
        row.AddChild(CreateOption(["Password: Any", "Password: Open", "Password: Required"], 148.0f));
        row.AddChild(CreateOption(["Whitelist: Any", "Whitelist: Off", "Whitelist: On"], 138.0f));
        return row;
    }

    private void PopulateServerList(ServerBrowserSnapshot? snapshot, Label footerStatus)
    {
        if (_serverList is null)
            return;

        _serverList.Clear();
        if (snapshot is null)
        {
            footerStatus.Text = "Manual: no discovery providers are configured. manual connect available.";
            return;
        }

        for (int i = 0; i < snapshot.Rows.Count; i++)
            _serverList.AddItem(FormatServerRow(snapshot.Rows[i]));

        if (snapshot.Rows.Count == 0)
        {
            _serverList.AddItem("No servers found");
            _serverList.SetItemDisabled(0, true);
        }

        string status = snapshot.IsRefreshing
            ? "refreshing..."
            : snapshot.LastError is not null
                ? $"error: {snapshot.LastError}"
                : $"{snapshot.Rows.Count} server(s)";
        footerStatus.Text = $"{ResolveActiveProviderName(snapshot)}: {status}. manual connect available.";
    }

    private void SelectServerRow(int index)
    {
        ServerBrowserSnapshot? snapshot = _serverBrowserSnapshot;
        if (snapshot is null || index < 0 || index >= snapshot.Rows.Count)
        {
            _selectedServerIndex = -1;
            if (_joinButton is not null)
                _joinButton.Disabled = true;
            if (_favoriteButton is not null)
                _favoriteButton.Disabled = true;
            return;
        }

        _selectedServerIndex = index;
        ServerDiscoveryRow row = snapshot.Rows[index];
        if (_joinButton is not null)
            _joinButton.Disabled = false;
        if (_favoriteButton is not null)
        {
            _favoriteButton.Disabled = false;
            _favoriteButton.Text = row.IsFavorite ? "Remove Favorite" : "Add Favorite";
        }
        if (_serverDetailLabel is not null)
            _serverDetailLabel.Text = FormatServerDetails(row);
    }

    private void SubmitFavoriteToggle()
    {
        ServerBrowserSnapshot? snapshot = _serverBrowserSnapshot;
        if (snapshot is null || _selectedServerIndex < 0 || _selectedServerIndex >= snapshot.Rows.Count)
            return;

        ServerDiscoveryRow row = snapshot.Rows[_selectedServerIndex];
        if (row.IsFavorite)
        {
            ServerBrowserUnfavoriteRequested?.Invoke(row);
        }
        else
        {
            ServerBrowserFavoriteRequested?.Invoke(row);
        }
    }

    private void SubmitSelectedServer()
    {
        ServerBrowserSnapshot? snapshot = _serverBrowserSnapshot;
        if (snapshot is null || _selectedServerIndex < 0 || _selectedServerIndex >= snapshot.Rows.Count)
            return;

        SubmitServerBrowserRow(snapshot.Rows[_selectedServerIndex]);
    }

    private void BuildDirectConnect()
    {
        if (_contentRoot is null)
            return;

        var column = CreateVBox(12);
        _contentRoot.AddChild(column);
        _hostField = AddLabeledTextField(column, "IP", _lastHostText, "host", labelWidth: 64.0f);
        _portField = AddLabeledTextField(column, "Port", _lastPortText, "port", labelWidth: 64.0f, fieldWidth: 120.0f);
        _hostField.TextSubmitted += _ => SubmitDirectConnect();
        _portField.TextSubmitted += _ => SubmitDirectConnect();

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        column.AddChild(row);
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(spacer);
        AddPrimaryButton(row, "Connect", SubmitDirectConnect, minWidth: 140.0f);
        AddPrimaryButton(row, "Back", () => BackRequested?.Invoke(), minWidth: 140.0f);
    }

    private void BuildSettings()
    {
        if (_contentRoot is null)
            return;

        var column = CreateVBox(10);
        _contentRoot.AddChild(column);

        column.AddChild(CreateCheckBox("Show overhead chat", _showOverheadChat, enabled =>
        {
            _showOverheadChat = enabled;
            OverheadChatVisibilityChanged?.Invoke(enabled);
        }));
        column.AddChild(CreateCheckBox("Show nameplates", _showNameplates, enabled =>
        {
            _showNameplates = enabled;
            NameplateVisibilityChanged?.Invoke(enabled);
        }));
        column.AddChild(CreateCheckBox("Show overhead health bars", _showOverheadHealthBars, enabled =>
        {
            _showOverheadHealthBars = enabled;
            OverheadHealthBarVisibilityChanged?.Invoke(enabled);
        }));
        column.AddChild(CreateCheckBox("Show chat panel", _showChatPanel, enabled =>
        {
            _showChatPanel = enabled;
            ChatPanelVisibilityChanged?.Invoke(enabled);
        }));
        column.AddChild(CreateSpinRow("Max visible world UI", _maxVisibleWorldUi, min: 1.0, max: 256.0, newValue =>
        {
            _maxVisibleWorldUi = Math.Clamp((int)Math.Round(newValue), 1, 256);
            MaxVisibleWorldUiChanged?.Invoke(_maxVisibleWorldUi);
        }));
        column.AddChild(CreateSliderRow("World UI size", _worldUiScale, min: 0.5, max: 3.0, newValue =>
        {
            _worldUiScale = Mathf.Clamp((float)newValue, 0.5f, 3.0f);
            WorldUiScaleChanged?.Invoke(_worldUiScale);
        }));
        AddPrimaryButton(column, "Back", () => BackRequested?.Invoke());
    }

    private void SubmitServerBrowserRow(ServerDiscoveryRow row)
    {
        if (!ClientDirectConnectRequest.TryCreate(row, out ClientDirectConnectRequest request, out string error))
        {
            SetStatus(error);
            return;
        }

        _lastHostText = request.Host;
        _lastPortText = request.Port.ToString(CultureInfo.InvariantCulture);
        ServerBrowserRowSubmitted?.Invoke(row);
        DirectConnectRequested?.Invoke(request);
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
            SetHeader("Direct Connect", error);
            return;
        }

        _lastHostText = request.Host;
        _lastPortText = request.Port.ToString(CultureInfo.InvariantCulture);
        ManualDirectConnectSubmitted?.Invoke(request);
        DirectConnectRequested?.Invoke(request);
    }

    private static string ResolveActiveProviderName(ServerBrowserSnapshot snapshot)
    {
        for (int i = 0; i < snapshot.Providers.Count; i++)
        {
            if (snapshot.Providers[i].IsSelected)
                return snapshot.Providers[i].DisplayName;
        }

        return snapshot.ActiveProviderId;
    }

    private static string FormatServerRow(ServerDiscoveryRow row)
    {
        string users = row.CurrentUsers.HasValue && row.MaxUsers.HasValue
            ? $" {row.CurrentUsers}/{row.MaxUsers}"
            : string.Empty;
        string ping = row.PingMs.HasValue ? $" {row.PingMs}ms" : string.Empty;
        string region = string.IsNullOrWhiteSpace(row.Region) ? string.Empty : $" [{row.Region}]";
        return $"{row.DisplayName}{region} - {row.Host}:{row.Port}{users}{ping}";
    }

    private static string FormatServerDetails(ServerDiscoveryRow row)
    {
        string users = row.CurrentUsers.HasValue && row.MaxUsers.HasValue
            ? $"{row.CurrentUsers}/{row.MaxUsers}"
            : "unknown";
        string tags = row.TagsOrEmpty.Count == 0
            ? "none"
            : string.Join(", ", row.TagsOrEmpty);

        return
            $"{row.DisplayName}\n\n" +
            $"Endpoint: {row.Host}:{row.Port}\n" +
            $"Health: {row.HealthState}\n" +
            $"Trust: {row.TrustLevel}\n" +
            $"Users: {users}\n" +
            $"Region: {row.Region ?? "unknown"}\n" +
            $"Language: {row.PrimaryLanguage ?? "unknown"}\n" +
            $"Version: {row.VersionLabel ?? row.CompatibilityId ?? "unknown"}\n" +
            $"Tags: {tags}";
    }

    private Button AddPrimaryButton(Node? parent, string text, Action pressed, float minWidth = 0.0f)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(minWidth, 34.0f)
        };
        button.Pressed += pressed;
        parent?.AddChild(button);
        return button;
    }

    private static VBoxContainer CreateVBox(int separation)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", separation);
        return column;
    }

    private static PanelContainer CreateInnerPanel()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle(new Color(0.095f, 0.105f, 0.12f, 0.86f), 8));
        return panel;
    }

    private static MarginContainer CreateMargin(int all)
    {
        return CreateMargin(all, all, all, all);
    }

    private static MarginContainer CreateMargin(int left, int top, int right, int bottom)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        return margin;
    }

    private static ColorRect CreateDivider()
    {
        return new ColorRect
        {
            CustomMinimumSize = new Vector2(0.0f, 2.0f),
            Color = new Color(1.0f, 1.0f, 1.0f, 0.28f)
        };
    }

    private static Label CreateLabel(string text, int fontSize, HorizontalAlignment alignment)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = alignment
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static StyleBoxFlat CreatePanelStyle(Color color, int radius)
    {
        var style = new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(1f, 1f, 1f, 0.10f)
        };
        return style;
    }

    private static LineEdit CreateFilterField(string placeholder, float width, bool expand = false)
    {
        var field = new LineEdit
        {
            PlaceholderText = placeholder,
            CustomMinimumSize = new Vector2(width, 0.0f)
        };
        if (expand)
            field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return field;
    }

    private static OptionButton CreateOption(string[] values, float width)
    {
        var option = new OptionButton
        {
            CustomMinimumSize = new Vector2(width, 0.0f)
        };
        for (int i = 0; i < values.Length; i++)
            option.AddItem(values[i]);
        return option;
    }

    private static CheckBox CreateCheckBox(string text, bool pressed, Action<bool> toggled)
    {
        var checkBox = new CheckBox
        {
            Text = text,
            ButtonPressed = pressed
        };
        checkBox.Toggled += pressed => toggled(pressed);
        return checkBox;
    }

    private static Control CreateSpinRow(string label, double value, double min, double max, Action<double> changed)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label
        {
            Text = label,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        });
        var spinBox = new SpinBox
        {
            CustomMinimumSize = new Vector2(96.0f, 0.0f),
            MinValue = min,
            MaxValue = max,
            Step = 1.0,
            Value = value
        };
        spinBox.ValueChanged += value => changed(value);
        row.AddChild(spinBox);
        return row;
    }

    private static Control CreateSliderRow(string label, double value, double min, double max, Action<double> changed)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label
        {
            Text = label,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        });
        var slider = new HSlider
        {
            CustomMinimumSize = new Vector2(160.0f, 0.0f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MinValue = min,
            MaxValue = max,
            Step = 0.05,
            Value = value
        };
        var valueLabel = new Label
        {
            CustomMinimumSize = new Vector2(52.0f, 0.0f),
            HorizontalAlignment = HorizontalAlignment.Right,
            Text = $"{value:0.00}x"
        };
        slider.ValueChanged += newValue =>
        {
            valueLabel.Text = $"{newValue:0.00}x";
            changed(newValue);
        };
        row.AddChild(slider);
        row.AddChild(valueLabel);
        return row;
    }

    private LineEdit AddLabeledTextField(Node parent, string label, string value, string placeholder, float labelWidth, float fieldWidth = 0.0f)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        parent.AddChild(row);

        row.AddChild(new Label
        {
            Text = label,
            CustomMinimumSize = new Vector2(labelWidth, 0.0f),
            VerticalAlignment = VerticalAlignment.Center
        });

        var field = new LineEdit
        {
            Text = value,
            PlaceholderText = placeholder,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(fieldWidth, 0.0f)
        };
        row.AddChild(field);
        return field;
    }
}
