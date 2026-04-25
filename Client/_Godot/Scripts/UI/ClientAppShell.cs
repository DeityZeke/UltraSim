using Godot;
using UltraSim.Client;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class ClientAppShell : Control
{
    public event Action<ClientAppScreen>? ScreenRequested;
    public event Action? PreviewRequested;
    public event Action? BackRequested;

    private Label? _title;
    private Label? _body;
    private VBoxContainer? _buttonColumn;

    public override void _Ready()
    {
        BuildInterface();
        ShowScreen(ClientAppScreen.Start);
    }

    public void ShowScreen(ClientAppScreen screen)
    {
        EnsureInterface();
        ClearButtons();

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
                SetText("Server Browser", "Discovery providers are not wired yet. Direct connect remains the fallback.");
                AddButton("Direct Connect", () => ScreenRequested?.Invoke(ClientAppScreen.DirectConnect));
                AddBackButton();
                break;
            case ClientAppScreen.DirectConnect:
                SetText("Direct Connect", "Connection fields and session driver wiring land in G3.");
                AddTextField("Host", "127.0.0.1");
                AddTextField("Port", "42042");
                AddButton("Connect Placeholder", () => ScreenRequested?.Invoke(ClientAppScreen.SessionStatus));
                AddBackButton();
                break;
            case ClientAppScreen.Settings:
                SetText("Settings", "Local settings will persist nameplate, chat, HUD, debug, and content preferences.");
                AddButton("Toggle Debug Placeholder", () => { });
                AddBackButton();
                break;
            case ClientAppScreen.SessionStatus:
                SetText("Session", "Preview session active. Remote session state will replace this in G3.");
                AddButton("Resume", () => PreviewRequested?.Invoke());
                AddButton("Options", () => ScreenRequested?.Invoke(ClientAppScreen.InSessionOptions));
                break;
            case ClientAppScreen.InSessionOptions:
                SetText("Options", "In-session menu placeholder.");
                AddButton("Resume", () => PreviewRequested?.Invoke());
                AddButton("Settings", () => ScreenRequested?.Invoke(ClientAppScreen.Settings));
                AddButton("Disconnect Placeholder", () => ScreenRequested?.Invoke(ClientAppScreen.Disconnected));
                break;
            case ClientAppScreen.Disconnected:
                SetText("Disconnected", "Session-owned world state should be cleared here once networking is attached.");
                AddButton("Back To Start", () => ScreenRequested?.Invoke(ClientAppScreen.Start));
                break;
            case ClientAppScreen.Error:
                SetText("Error", "A client error occurred.");
                AddButton("Back To Start", () => ScreenRequested?.Invoke(ClientAppScreen.Start));
                break;
        }
    }

    private void BuildInterface()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var shade = new ColorRect
        {
            Color = new Color(0.02f, 0.025f, 0.03f, 0.72f)
        };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(shade);

        var frame = new PanelContainer
        {
            CustomMinimumSize = new Vector2(460, 360)
        };
        frame.SetAnchorsPreset(LayoutPreset.Center);
        frame.OffsetLeft = -230;
        frame.OffsetTop = -180;
        frame.OffsetRight = 230;
        frame.OffsetBottom = 180;
        AddChild(frame);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        frame.AddChild(margin);

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

    private void AddTextField(string label, string value)
    {
        if (_buttonColumn is null)
        {
            return;
        }

        var field = new LineEdit
        {
            PlaceholderText = label,
            Text = value
        };
        _buttonColumn.AddChild(field);
    }
}
