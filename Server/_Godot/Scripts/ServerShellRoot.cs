using System.Text;
using Godot;

namespace UltraSim.Server.Godot;

public partial class ServerShellRoot : Control
{
    private GodotServerShellController? _controller;
    private RichTextLabel? _outputLog;
    private LineEdit? _commandLine;
    private Label? _stateValue;
    private Label? _ticksValue;
    private Label? _sessionsValue;
    private Label? _profileValue;

    public override void _Ready()
    {
        ConfigureWindow();
        BuildInterface();

        _controller = new GodotServerShellController(new GodotServerShellOptions("godot"));
        _controller.EventRaised += HandleServerEvent;

        AppendOutput("Godot operator shell ready.");
        AppendOutput("Use the buttons on the right or the command field below the output pane.");
        RefreshStatus();
    }

    public override void _Process(double delta)
    {
        _controller?.TickFrame(delta);
        RefreshStatus();
    }

    public override void _ExitTree()
    {
        if (_controller is not null)
        {
            _controller.EventRaised -= HandleServerEvent;
            _controller.Dispose();
            _controller = null;
        }
    }

    private void ConfigureWindow()
    {
        Window window = GetWindow();
        window.Title = "UltraSim Server";
        window.Size = new Vector2I(720, 420);
        window.MinSize = window.Size;
        window.Unresizable = true;
    }

    private void BuildInterface()
    {
        Color background = new(0.08f, 0.09f, 0.11f);
        Color panel = new(0.12f, 0.13f, 0.16f);
        Color panelSoft = new(0.15f, 0.16f, 0.20f);
        Color accent = new(0.86f, 0.55f, 0.27f);
        Color text = new(0.90f, 0.92f, 0.95f);
        Color muted = new(0.62f, 0.67f, 0.74f);

        var root = new MarginContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("margin_left", 8);
        root.AddThemeConstantOverride("margin_top", 8);
        root.AddThemeConstantOverride("margin_right", 8);
        root.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(root);

        var tabs = new TabContainer();
        tabs.SetAnchorsPreset(LayoutPreset.FullRect);
        tabs.AddThemeColorOverride("font_selected_color", text);
        tabs.AddThemeColorOverride("font_unselected_color", muted);
        tabs.AddThemeStyleboxOverride("panel", CreatePanelStyle(background, 0));
        tabs.AddThemeStyleboxOverride("tab_selected", CreatePanelStyle(panelSoft, 8, accent));
        tabs.AddThemeStyleboxOverride("tab_unselected", CreatePanelStyle(panel, 8));
        root.AddChild(tabs);

        Control outputTab = BuildOutputTab(panel, panelSoft, accent, text, muted);
        outputTab.Name = "Output";
        tabs.AddChild(outputTab);

        tabs.AddChild(CreatePlaceholderTab("Players", panel, muted));
        tabs.AddChild(CreatePlaceholderTab("Accounts", panel, muted));
        tabs.AddChild(CreatePlaceholderTab("NPCs", panel, muted));
        tabs.AddChild(CreatePlaceholderTab("Items", panel, muted));
    }

    private Control BuildOutputTab(Color panel, Color panelSoft, Color accent, Color text, Color muted)
    {
        var shellLayout = new HBoxContainer();
        shellLayout.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        shellLayout.SizeFlagsVertical = SizeFlags.ExpandFill;
        shellLayout.AddThemeConstantOverride("separation", 10);

        var leftColumn = new VBoxContainer();
        leftColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftColumn.SizeFlagsVertical = SizeFlags.ExpandFill;
        leftColumn.AddThemeConstantOverride("separation", 8);
        shellLayout.AddChild(leftColumn);

        var outputFrame = new PanelContainer();
        outputFrame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        outputFrame.SizeFlagsVertical = SizeFlags.ExpandFill;
        outputFrame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panelSoft, 10, accent));
        leftColumn.AddChild(outputFrame);

        _outputLog = new RichTextLabel();
        _outputLog.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _outputLog.SizeFlagsVertical = SizeFlags.ExpandFill;
        _outputLog.SelectionEnabled = true;
        _outputLog.ScrollFollowing = true;
        _outputLog.BbcodeEnabled = false;
        _outputLog.FitContent = false;
        _outputLog.AddThemeColorOverride("default_color", text);
        _outputLog.AddThemeConstantOverride("line_separation", 2);
        outputFrame.AddChild(_outputLog);

        var commandFrame = new PanelContainer();
        commandFrame.CustomMinimumSize = new Vector2(0, 44);
        commandFrame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panel, 10));
        leftColumn.AddChild(commandFrame);

        var commandRow = new HBoxContainer();
        commandRow.AddThemeConstantOverride("separation", 8);
        commandFrame.AddChild(commandRow);

        _commandLine = new LineEdit();
        _commandLine.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _commandLine.PlaceholderText = "Command line";
        _commandLine.AddThemeColorOverride("font_color", text);
        _commandLine.AddThemeColorOverride("font_placeholder_color", muted);
        _commandLine.AddThemeStyleboxOverride("normal", CreatePanelStyle(panelSoft, 6));
        _commandLine.AddThemeStyleboxOverride("focus", CreatePanelStyle(panelSoft, 6, accent));
        _commandLine.TextSubmitted += HandleCommandSubmitted;
        commandRow.AddChild(_commandLine);

        var executeButton = CreateActionButton("Run", accent, text);
        executeButton.Pressed += () =>
        {
            if (_commandLine is not null)
                HandleCommandSubmitted(_commandLine.Text);
        };
        commandRow.AddChild(executeButton);

        var rightPanel = new PanelContainer();
        rightPanel.CustomMinimumSize = new Vector2(180, 0);
        rightPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        rightPanel.AddThemeStyleboxOverride("panel", CreatePanelStyle(panel, 10));
        shellLayout.AddChild(rightPanel);

        var rightColumn = new VBoxContainer();
        rightColumn.AddThemeConstantOverride("separation", 8);
        rightPanel.AddChild(rightColumn);

        rightColumn.AddChild(CreateSectionLabel("Controls", text));
        rightColumn.AddChild(CreateCommandButton("Start", ServerControlCommand.Start(), accent, text));
        rightColumn.AddChild(CreateCommandButton("Stop", ServerControlCommand.Stop(), panelSoft, text));
        rightColumn.AddChild(CreateCommandButton("Save", ServerControlCommand.Save(), panelSoft, text));
        rightColumn.AddChild(CreateCommandButton("Restart", ServerControlCommand.Restart(), panelSoft, text));
        rightColumn.AddChild(CreateCommandButton("Shutdown", ServerControlCommand.Shutdown(), panelSoft, text));
        rightColumn.AddChild(CreateStatusButton(panelSoft, text));

        var separator = new HSeparator();
        rightColumn.AddChild(separator);

        rightColumn.AddChild(CreateSectionLabel("Status", text));
        _profileValue = CreateValueLabel(muted);
        _stateValue = CreateValueLabel(muted);
        _ticksValue = CreateValueLabel(muted);
        _sessionsValue = CreateValueLabel(muted);

        rightColumn.AddChild(CreateKeyValueRow("Profile", _profileValue, text));
        rightColumn.AddChild(CreateKeyValueRow("State", _stateValue, text));
        rightColumn.AddChild(CreateKeyValueRow("Ticks", _ticksValue, text));
        rightColumn.AddChild(CreateKeyValueRow("Sessions", _sessionsValue, text));

        return shellLayout;
    }

    private Control CreatePlaceholderTab(string title, Color panel, Color muted)
    {
        var frame = new PanelContainer();
        frame.Name = title;
        frame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panel, 10));

        var label = new Label();
        label.Text = $"{title} tab coming later.";
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SetAnchorsPreset(LayoutPreset.FullRect);
        label.AddThemeColorOverride("font_color", muted);
        frame.AddChild(label);
        return frame;
    }

    private Button CreateCommandButton(string label, ServerControlCommand command, Color fill, Color text)
    {
        var button = CreateActionButton(label, fill, text);
        button.Pressed += () => ExecuteCommand(command);
        return button;
    }

    private Button CreateStatusButton(Color fill, Color text)
    {
        var button = CreateActionButton("Status", fill, text);
        button.Pressed += PrintStatusSnapshot;
        return button;
    }

    private static Button CreateActionButton(string text, Color fill, Color fontColor)
    {
        var button = new Button();
        button.Text = text;
        button.CustomMinimumSize = new Vector2(0, 30);
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        button.AddThemeColorOverride("font_color", fontColor);
        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(fill, 8));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(fill.Lightened(0.08f), 8));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(fill.Darkened(0.10f), 8));
        return button;
    }

    private static Label CreateSectionLabel(string text, Color color)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Label CreateValueLabel(Color color)
    {
        var label = new Label();
        label.AddThemeColorOverride("font_color", color);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        return label;
    }

    private static HBoxContainer CreateKeyValueRow(string key, Label value, Color keyColor)
    {
        var row = new HBoxContainer();

        var keyLabel = new Label();
        keyLabel.Text = key;
        keyLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        keyLabel.AddThemeColorOverride("font_color", keyColor);
        row.AddChild(keyLabel);
        row.AddChild(value);

        return row;
    }

    private void HandleCommandSubmitted(string text)
    {
        string raw = text.Trim();
        if (string.IsNullOrEmpty(raw))
            return;

        if (_commandLine is not null)
            _commandLine.Text = string.Empty;

        if (string.Equals(raw, "help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "?", StringComparison.OrdinalIgnoreCase))
        {
            AppendOutput("Commands: start, status, save, restart, stop, shutdown, quit, exit, help");
            return;
        }

        ServerControlCommand? command = raw.ToLowerInvariant() switch
        {
            "start" => ServerControlCommand.Start(),
            "status" => ServerControlCommand.Status(),
            "save" => ServerControlCommand.Save(),
            "restart" => ServerControlCommand.Restart(),
            "stop" => ServerControlCommand.Stop(),
            "shutdown" => ServerControlCommand.Shutdown(),
            "quit" => ServerControlCommand.Shutdown(),
            "exit" => ServerControlCommand.Shutdown(),
            _ => null,
        };

        if (command.HasValue)
        {
            ExecuteCommand(command.Value);
            return;
        }

        AppendOutput($"Unknown command '{raw}'.");
    }

    private void ExecuteCommand(ServerControlCommand command)
    {
        if (_controller is null)
            return;

        ServerCommandResult result = _controller.Execute(command);
        AppendOutput($"> {command.Kind}: {result.Kind} - {result.Message}");
        RefreshStatus();
    }

    private void PrintStatusSnapshot()
    {
        if (_controller is null)
            return;

        ServerStatusSnapshot status = _controller.GetStatusSnapshot();
        ServerMetricsSnapshot metrics = _controller.GetMetricsSnapshot();

        var builder = new StringBuilder();
        builder.Append("status ");
        builder.Append($"profile={status.ProfileName} ");
        builder.Append($"state={status.State} ");
        builder.Append($"ticks={status.TickCount} ");
        builder.Append($"sessions={status.ActiveSessions}/{status.PeakSessions} ");
        builder.Append($"meanTickMs={metrics.MeanTickMilliseconds:0.###} ");
        builder.Append($"maxTickMs={metrics.MaxTickMilliseconds:0.###}");
        AppendOutput(builder.ToString());
    }

    private void HandleServerEvent(ServerApplicationEvent evt)
    {
        AppendOutput($"[{evt.TimestampUtc:HH:mm:ss}] {evt.Kind}: {evt.Message}");
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (_controller is null)
            return;

        ServerStatusSnapshot status = _controller.GetStatusSnapshot();

        if (_profileValue is not null)
            _profileValue.Text = status.ProfileName;

        if (_stateValue is not null)
            _stateValue.Text = status.State.ToString();

        if (_ticksValue is not null)
            _ticksValue.Text = status.TickCount.ToString();

        if (_sessionsValue is not null)
            _sessionsValue.Text = $"{status.ActiveSessions}/{status.PeakSessions}";
    }

    private void AppendOutput(string line)
    {
        _outputLog?.AppendText(line + "\n");
    }

    private static StyleBoxFlat CreatePanelStyle(Color color, int radius, Color? borderColor = null)
    {
        var style = new StyleBoxFlat();
        style.BgColor = color;
        style.SetCornerRadiusAll(radius);
        style.ContentMarginLeft = 10;
        style.ContentMarginTop = 8;
        style.ContentMarginRight = 10;
        style.ContentMarginBottom = 8;

        if (borderColor.HasValue)
        {
            style.BorderColor = borderColor.Value;
            style.BorderWidthBottom = 1;
            style.BorderWidthLeft = 1;
            style.BorderWidthRight = 1;
            style.BorderWidthTop = 1;
        }

        return style;
    }
}
