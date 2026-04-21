using System.Collections.Generic;
using System.Text;
using Godot;

namespace UltraSim.Server.Godot;

public partial class ServerShellRoot : Control
{
    private const int MaxOutputLines = 400;

    private readonly Queue<string> _outputLines = new();

    private GodotServerShellController? _controller;
    private RichTextLabel? _outputLog;
    private LineEdit? _commandLine;
    private Label? _stateValue;
    private Label? _ticksValue;
    private Label? _sessionsValue;
    private Label? _profileValue;
    private Label? _playersSummaryValue;
    private Label? _accountsSummaryValue;
    private Label? _performanceSummaryValue;
    private RichTextLabel? _performanceLog;
    private Button? _startButton;
    private Button? _stopButton;
    private Button? _saveButton;
    private Button? _restartButton;
    private Button? _shutdownButton;
    private Button? _statusButton;
    private Button? _runButton;

    public override void _Ready()
    {
        ConfigureWindow();
        BuildInterface();

        _controller = new GodotServerShellController(new GodotServerShellOptions("godot"));
        _controller.EventRaised += HandleServerEvent;

        AppendOutput("INFO", "Godot operator shell ready.");
        AppendOutput("INFO", "Use the right-side controls or the command field below the output pane.");
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
        Color accentSoft = new(0.31f, 0.45f, 0.57f);
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
        tabs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        tabs.SizeFlagsVertical = SizeFlags.ExpandFill;
        tabs.AddThemeColorOverride("font_selected_color", text);
        tabs.AddThemeColorOverride("font_unselected_color", muted);
        tabs.AddThemeStyleboxOverride("panel", CreatePanelStyle(background, 0));
        tabs.AddThemeStyleboxOverride("tab_selected", CreatePanelStyle(panelSoft, 8, accent));
        tabs.AddThemeStyleboxOverride("tab_unselected", CreatePanelStyle(panel, 8));
        root.AddChild(tabs);

        Control outputTab = BuildOutputTab(panel, panelSoft, accent, text, muted);
        outputTab.Name = "Output";
        tabs.AddChild(outputTab);

        Control playersTab = BuildPlayersTab(panel, panelSoft, accentSoft, text, muted);
        playersTab.Name = "Players";
        tabs.AddChild(playersTab);

        Control accountsTab = BuildAccountsTab(panel, panelSoft, accentSoft, text, muted);
        accountsTab.Name = "Accounts";
        tabs.AddChild(accountsTab);

        Control performanceTab = BuildPerformanceTab(panel, panelSoft, accentSoft, text, muted);
        performanceTab.Name = "Performance";
        tabs.AddChild(performanceTab);

        tabs.AddChild(CreatePlaceholderTab("NPCs", "NPC and mob operator tools will land here.", panel, muted));
        tabs.AddChild(CreatePlaceholderTab("Items", "Item and inventory operator tools will land here.", panel, muted));
    }

    private Control BuildOutputTab(Color panel, Color panelSoft, Color accent, Color text, Color muted)
    {
        var shellLayout = new HBoxContainer();
        shellLayout.SetAnchorsPreset(LayoutPreset.FullRect);
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
        _outputLog.ContextMenuEnabled = true;
        _outputLog.AddThemeColorOverride("default_color", text);
        _outputLog.AddThemeConstantOverride("line_separation", 2);
        outputFrame.AddChild(_outputLog);

        var commandFrame = new PanelContainer();
        commandFrame.CustomMinimumSize = new Vector2(0, 44);
        commandFrame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        commandFrame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panel, 10));
        leftColumn.AddChild(commandFrame);

        var commandRow = new HBoxContainer();
        commandRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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

        _runButton = CreateActionButton("Run", accent, text);
        _runButton.Pressed += () =>
        {
            if (_commandLine is not null)
                HandleCommandSubmitted(_commandLine.Text);
        };
        commandRow.AddChild(_runButton);

        var rightPanel = new PanelContainer();
        rightPanel.CustomMinimumSize = new Vector2(180, 0);
        rightPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        rightPanel.AddThemeStyleboxOverride("panel", CreatePanelStyle(panel, 10));
        shellLayout.AddChild(rightPanel);

        var rightScroll = new ScrollContainer();
        rightScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rightScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        rightScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        rightScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        rightPanel.AddChild(rightScroll);

        var rightColumn = new VBoxContainer();
        rightColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rightColumn.AddThemeConstantOverride("separation", 6);
        rightScroll.AddChild(rightColumn);

        rightColumn.AddChild(CreateSectionLabel("Controls", text));

        _startButton = CreateCommandButton("Start", ServerControlCommand.Start(), accent, text);
        _stopButton = CreateCommandButton("Stop", ServerControlCommand.Stop(), panelSoft, text);
        _saveButton = CreateCommandButton("Save", ServerControlCommand.Save(), panelSoft, text);
        _restartButton = CreateCommandButton("Restart", ServerControlCommand.Restart(), panelSoft, text);
        _shutdownButton = CreateCommandButton("Shutdown", ServerControlCommand.Shutdown(), panelSoft, text);
        _statusButton = CreateStatusButton(panelSoft, text);

        rightColumn.AddChild(_startButton);
        rightColumn.AddChild(_stopButton);
        rightColumn.AddChild(_saveButton);
        rightColumn.AddChild(_restartButton);
        rightColumn.AddChild(_shutdownButton);
        rightColumn.AddChild(_statusButton);

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

    private Control BuildPlayersTab(Color panel, Color panelSoft, Color accentSoft, Color text, Color muted)
    {
        var frame = new PanelContainer();
        frame.SetAnchorsPreset(LayoutPreset.FullRect);
        frame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        frame.SizeFlagsVertical = SizeFlags.ExpandFill;
        frame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panel, 10));

        var column = new VBoxContainer();
        column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        column.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddThemeConstantOverride("separation", 10);
        frame.AddChild(column);

        column.AddChild(CreateSectionLabel("Players Online", text));

        _playersSummaryValue = new Label();
        _playersSummaryValue.AddThemeColorOverride("font_color", muted);
        column.AddChild(_playersSummaryValue);

        var listFrame = new PanelContainer();
        listFrame.SizeFlagsVertical = SizeFlags.ExpandFill;
        listFrame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panelSoft, 10, accentSoft));
        column.AddChild(listFrame);

        var listText = new RichTextLabel();
        listText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        listText.SizeFlagsVertical = SizeFlags.ExpandFill;
        listText.SelectionEnabled = true;
        listText.ScrollActive = true;
        listText.AddThemeColorOverride("default_color", text);
        listText.Text =
            "Active player rows will appear here once session roster plumbing is wired.\n\n" +
            "Planned first fields:\n" +
            "- player name\n" +
            "- account id\n" +
            "- session state\n" +
            "- kick / inspect actions";
        listFrame.AddChild(listText);

        return frame;
    }

    private Control BuildAccountsTab(Color panel, Color panelSoft, Color accentSoft, Color text, Color muted)
    {
        var frame = new PanelContainer();
        frame.SetAnchorsPreset(LayoutPreset.FullRect);
        frame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        frame.SizeFlagsVertical = SizeFlags.ExpandFill;
        frame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panel, 10));

        var column = new VBoxContainer();
        column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        column.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddThemeConstantOverride("separation", 10);
        frame.AddChild(column);

        column.AddChild(CreateSectionLabel("Accounts", text));

        _accountsSummaryValue = new Label();
        _accountsSummaryValue.AddThemeColorOverride("font_color", muted);
        column.AddChild(_accountsSummaryValue);

        var actionRow = new HBoxContainer();
        actionRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        actionRow.AddThemeConstantOverride("separation", 8);
        column.AddChild(actionRow);

        actionRow.AddChild(CreateDisabledButton("Add", accentSoft, text));
        actionRow.AddChild(CreateDisabledButton("Remove", panelSoft, text));
        actionRow.AddChild(CreateDisabledButton("Ban", panelSoft, text));

        var detailsFrame = new PanelContainer();
        detailsFrame.SizeFlagsVertical = SizeFlags.ExpandFill;
        detailsFrame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panelSoft, 10, accentSoft));
        column.AddChild(detailsFrame);

        var detailsText = new RichTextLabel();
        detailsText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        detailsText.SizeFlagsVertical = SizeFlags.ExpandFill;
        detailsText.SelectionEnabled = true;
        detailsText.ScrollActive = true;
        detailsText.AddThemeColorOverride("default_color", text);
        detailsText.Text =
            "Account directory and moderation tools are intentionally not wired yet.\n\n" +
            "This tab will become the thin operator view for:\n" +
            "- account lookup\n" +
            "- add / remove / ban actions\n" +
            "- audit notes and current flags";
        detailsFrame.AddChild(detailsText);

        return frame;
    }

    private Control BuildPerformanceTab(Color panel, Color panelSoft, Color accentSoft, Color text, Color muted)
    {
        var frame = new PanelContainer();
        frame.SetAnchorsPreset(LayoutPreset.FullRect);
        frame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        frame.SizeFlagsVertical = SizeFlags.ExpandFill;
        frame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panel, 10));

        var column = new VBoxContainer();
        column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        column.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddThemeConstantOverride("separation", 10);
        frame.AddChild(column);

        column.AddChild(CreateSectionLabel("Performance", text));

        _performanceSummaryValue = new Label();
        _performanceSummaryValue.AddThemeColorOverride("font_color", muted);
        _performanceSummaryValue.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_performanceSummaryValue);

        var logFrame = new PanelContainer();
        logFrame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        logFrame.SizeFlagsVertical = SizeFlags.ExpandFill;
        logFrame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panelSoft, 10, accentSoft));
        column.AddChild(logFrame);

        _performanceLog = new RichTextLabel();
        _performanceLog.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _performanceLog.SizeFlagsVertical = SizeFlags.ExpandFill;
        _performanceLog.SelectionEnabled = true;
        _performanceLog.ScrollActive = true;
        _performanceLog.BbcodeEnabled = false;
        _performanceLog.FitContent = false;
        _performanceLog.ContextMenuEnabled = true;
        _performanceLog.AddThemeColorOverride("default_color", text);
        _performanceLog.Text =
            "Per-system runtime metrics will appear here once the server runtime is backed by a real ECS world.";
        logFrame.AddChild(_performanceLog);

        return frame;
    }

    private Control CreatePlaceholderTab(string title, string message, Color panel, Color muted)
    {
        var frame = new PanelContainer();
        frame.Name = title;
        frame.SetAnchorsPreset(LayoutPreset.FullRect);
        frame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        frame.SizeFlagsVertical = SizeFlags.ExpandFill;
        frame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panel, 10));

        var label = new Label();
        label.Text = message;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
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

    private static Button CreateDisabledButton(string text, Color fill, Color fontColor)
    {
        var button = CreateActionButton(text, fill, fontColor);
        button.Disabled = true;
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
            AppendOutput("HELP", "Commands: start, status, save, restart, stop, shutdown, quit, exit, help");
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

        AppendOutput("ERROR", $"Unknown command '{raw}'.");
    }

    private void ExecuteCommand(ServerControlCommand command)
    {
        if (_controller is null)
            return;

        ServerCommandResult result = _controller.Execute(command);
        AppendOutput("RESULT", $"{command.Kind}: {result.Kind} - {result.Message}");
        RefreshStatus();
    }

    private void PrintStatusSnapshot()
    {
        if (_controller is null)
            return;

        ServerStatusSnapshot status = _controller.GetStatusSnapshot();
        ServerMetricsSnapshot metrics = _controller.GetMetricsSnapshot();

        var builder = new StringBuilder();
        builder.Append($"profile={status.ProfileName} ");
        builder.Append($"state={status.State} ");
        builder.Append($"ticks={status.TickCount} ");
        builder.Append($"sessions={status.ActiveSessions}/{status.PeakSessions} ");
        builder.Append($"meanTickMs={metrics.MeanTickMilliseconds:0.###} ");
        builder.Append($"maxTickMs={metrics.MaxTickMilliseconds:0.###}");
        AppendOutput("STATUS", builder.ToString());
    }

    private void HandleServerEvent(ServerApplicationEvent evt)
    {
        switch (evt.Kind)
        {
            case ServerApplicationEventKind.StateChanged:
                AppendOutput("STATE", evt.Message, evt.TimestampUtc);
                break;

            case ServerApplicationEventKind.SaveRequested:
                AppendOutput("SAVE", evt.Message, evt.TimestampUtc);
                break;

            case ServerApplicationEventKind.SaveCompleted:
                AppendOutput("SAVE", evt.Message, evt.TimestampUtc);
                break;

            case ServerApplicationEventKind.Faulted:
                AppendOutput("FAULT", evt.Message, evt.TimestampUtc);
                break;

            case ServerApplicationEventKind.Log:
                AppendOutput("LOG", evt.Message, evt.TimestampUtc);
                break;

            case ServerApplicationEventKind.CommandHandled:
                break;
        }

        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (_controller is null)
            return;

        ServerStatusSnapshot status = _controller.GetStatusSnapshot();
        ServerMetricsSnapshot metrics = _controller.GetMetricsSnapshot();
        IReadOnlyList<ServerSystemRuntimeSnapshot> systems = _controller.GetSystemRuntimeSnapshots();
        IReadOnlyList<ServerPlayerSessionSnapshot> players = _controller.GetPlayerSessionsSnapshot();
        IReadOnlyList<ServerAccountSummarySnapshot> accounts = _controller.GetAccountSummariesSnapshot();

        if (_profileValue is not null)
            _profileValue.Text = status.ProfileName;

        if (_stateValue is not null)
            _stateValue.Text = status.State.ToString();

        if (_ticksValue is not null)
            _ticksValue.Text = status.TickCount.ToString();

        if (_sessionsValue is not null)
            _sessionsValue.Text = $"{status.ActiveSessions}/{status.PeakSessions}";

        if (_playersSummaryValue is not null)
            _playersSummaryValue.Text = players.Count == 0
                ? $"{status.ActiveSessions} online now, peak {status.PeakSessions}."
                : $"{players.Count} player rows available for operator view.";

        if (_accountsSummaryValue is not null)
            _accountsSummaryValue.Text = accounts.Count == 0
                ? "Account directory actions are placeholders until account storage is wired."
                : $"{accounts.Count} account rows available for operator view.";

        RefreshPerformance(metrics, systems);
        RefreshControls(status.State);
    }

    private void RefreshPerformance(ServerMetricsSnapshot metrics, IReadOnlyList<ServerSystemRuntimeSnapshot> systems)
    {
        if (_performanceSummaryValue is not null)
        {
            _performanceSummaryValue.Text =
                $"ticks={metrics.TotalTicks}  mean={metrics.MeanTickMilliseconds:0.###} ms  " +
                $"max={metrics.MaxTickMilliseconds:0.###} ms  systems={systems.Count}";
        }

        if (_performanceLog is null)
            return;

        if (systems.Count == 0)
        {
            _performanceLog.Text =
                "Per-system runtime metrics will appear here once the server runtime is backed by a real ECS world.\n\n" +
                "The shell contract is already in place, so future runtimes can expose live scheduler/system timings here.";
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Name | Stage | Mode | Period | Last | Avg | Peak | Min | Runs | Controls");
        builder.AppendLine(new string('-', 96));

        for (int i = 0; i < systems.Count; i++)
        {
            ServerSystemRuntimeSnapshot system = systems[i];
            string mode = system.Manual ? "manual" : (system.Enabled ? "enabled" : "disabled");
            string period = $"{system.PeriodTicks}/{system.StartOffset}";
            builder.Append(system.Name);
            builder.Append(" | ");
            builder.Append(system.Stage);
            builder.Append(" | ");
            builder.Append(mode);
            builder.Append(" | ");
            builder.Append(period);
            builder.Append(" | ");
            builder.Append(system.LastUpdateMilliseconds.ToString("0.###"));
            builder.Append(" | ");
            builder.Append(system.AverageUpdateMilliseconds.ToString("0.###"));
            builder.Append(" | ");
            builder.Append(system.PeakUpdateMilliseconds.ToString("0.###"));
            builder.Append(" | ");
            builder.Append(system.MinUpdateMilliseconds.ToString("0.###"));
            builder.Append(" | ");
            builder.Append(system.UpdateCount);
            builder.Append(" | ");
            builder.Append(system.ControlCount);
            builder.AppendLine();
        }

        _performanceLog.Text = builder.ToString();
    }

    private void RefreshControls(ServerApplicationState state)
    {
        bool hasController = _controller is not null;
        bool canStart = hasController && state is ServerApplicationState.Created or ServerApplicationState.Initialized or ServerApplicationState.Stopped;
        bool canStop = hasController && state != ServerApplicationState.Created && state != ServerApplicationState.Stopped;
        bool canSave = hasController && state is ServerApplicationState.Initialized or ServerApplicationState.Running or ServerApplicationState.Faulted;
        bool canRestart = hasController && state != ServerApplicationState.Created;
        bool canShutdown = hasController && state != ServerApplicationState.Stopped;

        if (_startButton is not null)
            _startButton.Disabled = !canStart;

        if (_stopButton is not null)
            _stopButton.Disabled = !canStop;

        if (_saveButton is not null)
            _saveButton.Disabled = !canSave;

        if (_restartButton is not null)
            _restartButton.Disabled = !canRestart;

        if (_shutdownButton is not null)
            _shutdownButton.Disabled = !canShutdown;

        if (_statusButton is not null)
            _statusButton.Disabled = !hasController;

        if (_runButton is not null)
            _runButton.Disabled = !hasController;

        if (_commandLine is not null)
            _commandLine.Editable = hasController;
    }

    private void AppendOutput(string category, string message, DateTimeOffset? timestamp = null)
    {
        string line = $"{(timestamp ?? DateTimeOffset.Now):HH:mm:ss}  {category.PadRight(6)}  {message}";
        _outputLines.Enqueue(line);

        while (_outputLines.Count > MaxOutputLines)
            _outputLines.Dequeue();

        RebuildOutputLog();
    }

    private void RebuildOutputLog()
    {
        if (_outputLog is null)
            return;

        _outputLog.Clear();
        foreach (string line in _outputLines)
            _outputLog.AppendText(line + "\n");

        CallDeferred(nameof(ScrollOutputToBottom));
    }

    private void ScrollOutputToBottom()
    {
        if (_outputLog is null)
            return;

        _outputLog.ScrollToLine(Math.Max(0, _outputLines.Count - 1));
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
