using System.Collections.Generic;
using System.Text;
using Godot;
using UltraSim.Server.Hosting;
using UltraSim.Server.Scripting;

namespace UltraSim.Server.Godot;

public partial class ServerShellRoot : Control
{
	private const int MaxOutputLines = 400;

	private readonly Queue<string> _outputLines = new();

	private GodotServerShellOptions? _options;
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
	private Button? _restartWithoutSaveButton;
	private Button? _shutdownButton;
	private Button? _shutdownWithoutSaveButton;
	private Button? _statusButton;
	private Button? _runButton;
	private bool _smokeRequireEmptySessions;
	private double _smokeEmptySessionGraceSeconds = 2.0;
	private double _smokeEmptySessionWaitedSeconds;
	private bool _smokeQuitRequested;
	private bool _smokeEmptySessionWaitLogged;

	public override void _Ready()
	{
		_smokeRequireEmptySessions = HasUserArg("--us-smoke-require-empty-sessions");
		_smokeEmptySessionGraceSeconds = ReadNonNegativeUserArg("--us-smoke-empty-session-grace=", _smokeEmptySessionGraceSeconds);
		ConfigureWindow();
		BuildInterface();

		_options = ReadShellOptions(out bool startOnReady);
		AppendOutput("INFO", "Godot operator shell ready.");
		AppendOutput("INFO", "Use the right-side controls or the command field below the output pane.");
		if (startOnReady)
		{
			AppendOutput("INFO", $"Auto-starting movement server on UDP port {_options.NetworkPort}.");
			ExecuteCommand(ServerControlCommand.Start());
		}

		RefreshStatus();
		ScheduleSmokeQuitIfRequested();
	}

	public override void _Process(double delta)
	{
		_controller?.TickFrame(delta);
		RefreshStatus();
		if (_smokeQuitRequested)
		{
			CompleteSmokeQuit(delta);
		}
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

	private static GodotServerShellOptions ReadShellOptions(out bool startOnReady)
	{
		string profileName = GodotServerShellOptions.Default.ProfileName;
		int tickMilliseconds = GodotServerShellOptions.Default.TickMilliseconds;
		int maxCatchUpTicks = GodotServerShellOptions.Default.MaxCatchUpTicksPerPump;
		int networkPort = GodotServerShellOptions.Default.NetworkPort;
		double sessionAuthTimeoutSeconds = GodotServerShellOptions.Default.SessionAuthTimeoutSeconds;
		double sessionKeepAliveTimeoutSeconds = GodotServerShellOptions.Default.SessionKeepAliveTimeoutSeconds;
		int maxAutoCreatedAccountsPerRemoteAddress = GodotServerShellOptions.Default.MaxAutoCreatedAccountsPerRemoteAddress;
		bool enableMovementWorld = GodotServerShellOptions.Default.EnableMovementWorld;
		string? runtimeRootPath = GodotServerShellOptions.Default.RuntimeRootPath;
		startOnReady = false;

		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (string.Equals(arg, "--us-server-start", StringComparison.OrdinalIgnoreCase))
			{
				startOnReady = true;
			}
			else if (string.Equals(arg, "--us-server-no-movement-world", StringComparison.OrdinalIgnoreCase))
			{
				enableMovementWorld = false;
			}
			else if (arg.StartsWith("--us-server-profile=", StringComparison.OrdinalIgnoreCase))
			{
				string value = arg["--us-server-profile=".Length..].Trim();
				if (!string.IsNullOrWhiteSpace(value))
				{
					profileName = value;
				}
			}
			else if (arg.StartsWith("--us-server-runtime-root=", StringComparison.OrdinalIgnoreCase))
			{
				string value = arg["--us-server-runtime-root=".Length..].Trim();
				if (!string.IsNullOrWhiteSpace(value))
				{
					runtimeRootPath = value;
				}
			}
			else if (arg.StartsWith("--us-server-port=", StringComparison.OrdinalIgnoreCase))
			{
				if (int.TryParse(arg["--us-server-port=".Length..], out int parsedPort) && parsedPort > 0)
				{
					networkPort = parsedPort;
				}
			}
			else if (arg.StartsWith("--us-server-tick-ms=", StringComparison.OrdinalIgnoreCase))
			{
				if (int.TryParse(arg["--us-server-tick-ms=".Length..], out int parsedTickMilliseconds) && parsedTickMilliseconds > 0)
				{
					tickMilliseconds = parsedTickMilliseconds;
				}
			}
			else if (arg.StartsWith("--us-server-session-auth-timeout-seconds=", StringComparison.OrdinalIgnoreCase))
			{
				if (double.TryParse(arg["--us-server-session-auth-timeout-seconds=".Length..], out double parsedAuthTimeout) && parsedAuthTimeout >= 0.0)
				{
					sessionAuthTimeoutSeconds = parsedAuthTimeout;
				}
			}
			else if (arg.StartsWith("--us-server-session-keepalive-timeout-seconds=", StringComparison.OrdinalIgnoreCase))
			{
				if (double.TryParse(arg["--us-server-session-keepalive-timeout-seconds=".Length..], out double parsedKeepAliveTimeout) && parsedKeepAliveTimeout >= 0.0)
				{
					sessionKeepAliveTimeoutSeconds = parsedKeepAliveTimeout;
				}
			}
			else if (arg.StartsWith("--us-server-max-catchup=", StringComparison.OrdinalIgnoreCase))
			{
				if (int.TryParse(arg["--us-server-max-catchup=".Length..], out int parsedMaxCatchUpTicks) && parsedMaxCatchUpTicks > 0)
				{
					maxCatchUpTicks = parsedMaxCatchUpTicks;
				}
			}
			else if (arg.StartsWith("--us-server-max-auto-created-accounts-per-ip=", StringComparison.OrdinalIgnoreCase))
			{
				if (int.TryParse(arg["--us-server-max-auto-created-accounts-per-ip=".Length..], out int parsedAccountLimit) && parsedAccountLimit >= 0)
				{
					maxAutoCreatedAccountsPerRemoteAddress = parsedAccountLimit;
				}
			}
		}

		return new GodotServerShellOptions(
			profileName,
			TickMilliseconds: tickMilliseconds,
			MaxCatchUpTicksPerPump: maxCatchUpTicks,
			EnableMovementWorld: enableMovementWorld,
			NetworkPort: networkPort,
			SessionAuthTimeoutSeconds: sessionAuthTimeoutSeconds,
			SessionKeepAliveTimeoutSeconds: sessionKeepAliveTimeoutSeconds,
			MaxAutoCreatedAccountsPerRemoteAddress: maxAutoCreatedAccountsPerRemoteAddress,
			RuntimeRootPath: runtimeRootPath);
	}

	private void ScheduleSmokeQuitIfRequested()
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (!arg.StartsWith("--us-smoke-quit-after=", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (!double.TryParse(arg["--us-smoke-quit-after=".Length..], out double seconds) || seconds <= 0.0)
			{
				GD.PushWarning("Invalid --us-smoke-quit-after value.");
				return;
			}

			SceneTreeTimer timer = GetTree().CreateTimer(seconds);
			timer.Timeout += RequestSmokeQuit;
			return;
		}
	}

	private void RequestSmokeQuit()
	{
		_smokeQuitRequested = true;
		_smokeEmptySessionWaitedSeconds = 0.0;
		_smokeEmptySessionWaitLogged = false;
		CompleteSmokeQuit(0.0);
	}

	private void CompleteSmokeQuit(double delta)
	{
		if (_smokeRequireEmptySessions)
		{
			int activeSessions = _controller?.GetStatusSnapshot().ActiveSessions ?? 0;
			if (activeSessions != 0)
			{
				if (!_smokeEmptySessionWaitLogged)
				{
					AppendOutput("SMOKE", $"Waiting up to {_smokeEmptySessionGraceSeconds:0.###}s for empty sessions ({activeSessions} active).");
					_smokeEmptySessionWaitLogged = true;
				}

				_smokeEmptySessionWaitedSeconds += Math.Max(0.0, delta);
				if (_smokeEmptySessionWaitedSeconds < _smokeEmptySessionGraceSeconds)
				{
					return;
				}

				_smokeQuitRequested = false;
				GD.PushError($"UltraSim server smoke failed: expected 0 active sessions after {_smokeEmptySessionGraceSeconds:0.###}s grace, found {activeSessions}.");
				GetTree().Quit(1);
				return;
			}

			AppendOutput("SMOKE", "Empty session requirement satisfied.");
		}

		_smokeQuitRequested = false;
		GetTree().Quit();
	}

	private static double ReadNonNegativeUserArg(string prefix, double defaultValue)
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (double.TryParse(arg[prefix.Length..], out double seconds) && seconds >= 0.0)
			{
				return seconds;
			}

			GD.PushWarning($"Invalid {prefix.TrimEnd('=')} value.");
			return defaultValue;
		}

		return defaultValue;
	}

	private static bool HasUserArg(string expected)
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
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
		_restartWithoutSaveButton = CreateCommandButton("Restart No Save", ServerControlCommand.RestartWithoutSave(), panelSoft, text);
		_shutdownButton = CreateCommandButton("Shutdown", ServerControlCommand.Shutdown(), panelSoft, text);
		_shutdownWithoutSaveButton = CreateCommandButton("Shutdown No Save", ServerControlCommand.ShutdownWithoutSave(), panelSoft, text);
		_statusButton = CreateStatusButton(panelSoft, text);

		rightColumn.AddChild(_startButton);
		rightColumn.AddChild(_stopButton);
		rightColumn.AddChild(_saveButton);
		rightColumn.AddChild(_restartButton);
		rightColumn.AddChild(_restartWithoutSaveButton);
		rightColumn.AddChild(_shutdownButton);
		rightColumn.AddChild(_shutdownWithoutSaveButton);
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
			AppendOutput("HELP", "Commands: start, status, save, restart, restart-nosave, stop, shutdown, shutdown-nosave, quit, exit, help");
			return;
		}

		ServerControlCommand? command = raw.ToLowerInvariant() switch
		{
			"start" => ServerControlCommand.Start(),
			"status" => ServerControlCommand.Status(),
			"save" => ServerControlCommand.Save(),
			"restart" => ServerControlCommand.Restart(),
			"restart-nosave" => ServerControlCommand.RestartWithoutSave(),
			"restart-no-save" => ServerControlCommand.RestartWithoutSave(),
			"restart nosave" => ServerControlCommand.RestartWithoutSave(),
			"restart no-save" => ServerControlCommand.RestartWithoutSave(),
			"restart without-save" => ServerControlCommand.RestartWithoutSave(),
			"restart --no-save" => ServerControlCommand.RestartWithoutSave(),
			"stop" => ServerControlCommand.Stop(),
			"shutdown" => ServerControlCommand.Shutdown(),
			"shutdown-nosave" => ServerControlCommand.ShutdownWithoutSave(),
			"shutdown-no-save" => ServerControlCommand.ShutdownWithoutSave(),
			"shutdown nosave" => ServerControlCommand.ShutdownWithoutSave(),
			"shutdown no-save" => ServerControlCommand.ShutdownWithoutSave(),
			"shutdown without-save" => ServerControlCommand.ShutdownWithoutSave(),
			"shutdown --no-save" => ServerControlCommand.ShutdownWithoutSave(),
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
		{
			if (command.Kind != ServerControlCommandKind.Start)
			{
				AppendOutput("ERROR", "Server runtime is not started yet.");
				RefreshStatus();
				return;
			}

			if (!TryEnsureController())
			{
				RefreshStatus();
				return;
			}
		}

		if (_controller is null)
			return;

		ServerCommandResult result = _controller.Execute(command);
		AppendOutput("RESULT", $"{command.Kind}: {result.Kind} - {result.Message}");
		RefreshStatus();
	}

	private void PrintStatusSnapshot()
	{
		if (_controller is null)
		{
			AppendOutput("STATUS", $"profile={(_options?.ProfileName ?? GodotServerShellOptions.Default.ProfileName)} state=ShellReady ticks=0 sessions=0/0");
			return;
		}

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
		IReadOnlyList<ServerHostLogLine> lines = ServerHostLogFormatter.Format(evt);
		if (lines.Count > 0)
		{
			foreach (ServerHostLogLine line in lines)
			{
				AppendOutput(line.Category, line.Message, evt.TimestampUtc);
			}

			RefreshStatus();
			return;
		}

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

	private bool TryEnsureController()
	{
		if (_controller is not null)
			return true;

		GodotServerShellOptions options = _options ?? ReadShellOptions(out _);
		try
		{
			_controller = new GodotServerShellController(options);
		}
		catch (UltraSimScriptStartupException ex)
		{
			ReportScriptStartupFailure(ex);
			return false;
		}

		_controller.EventRaised += HandleServerEvent;
		foreach (ServerApplicationEvent startupEvent in _controller.StartupEvents)
		{
			HandleServerEvent(startupEvent);
		}

		return true;
	}

	private void ReportScriptStartupFailure(UltraSimScriptStartupException exception)
	{
		foreach (ServerApplicationEvent startupEvent in UltraSimScriptStartupLog.CreateEvents(exception.Result))
			HandleServerEvent(startupEvent);

		AppendOutput("FAULT", exception.Message);
		int emitted = 0;
		foreach (UltraSimScriptCompileDiagnostic diagnostic in exception.Result.CompileResult.Diagnostics)
		{
			if (diagnostic.Severity != UltraSimScriptDiagnosticSeverity.Error)
				continue;

			string file = string.IsNullOrWhiteSpace(diagnostic.FilePath)
				? "<unknown>"
				: System.IO.Path.GetFileName(diagnostic.FilePath);
			AppendOutput("SCRIPT", $"{diagnostic.Id} {file}:{diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}");
			emitted++;
			if (emitted >= 8)
				break;
		}

		if (exception.Result.ErrorCount > emitted)
			AppendOutput("SCRIPT", $"{exception.Result.ErrorCount - emitted} additional script error(s) omitted; see cache/scripts/diagnostics.");

		if (_stateValue is not null)
			_stateValue.Text = "ScriptFault";

		RefreshControls(ServerApplicationState.Faulted);
	}

	private void RefreshStatus()
	{
		if (_controller is null)
		{
			if (_profileValue is not null)
				_profileValue.Text = _options?.ProfileName ?? GodotServerShellOptions.Default.ProfileName;

			if (_stateValue is not null)
				_stateValue.Text = "ShellReady";

			if (_ticksValue is not null)
				_ticksValue.Text = "0";

			if (_sessionsValue is not null)
				_sessionsValue.Text = "0/0";

			if (_playersSummaryValue is not null)
				_playersSummaryValue.Text = "No active players.";

			if (_accountsSummaryValue is not null)
				_accountsSummaryValue.Text = "No accounts loaded.";

			if (_performanceSummaryValue is not null)
				_performanceSummaryValue.Text = "Waiting for Start.";

			if (_performanceLog is not null)
				_performanceLog.Text = "Server systems will appear after Start.";

			RefreshControls(ServerApplicationState.Created);
			return;
		}

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
		bool canStart = !hasController || state is ServerApplicationState.Created or ServerApplicationState.Initialized or ServerApplicationState.Stopped;
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

		if (_restartWithoutSaveButton is not null)
			_restartWithoutSaveButton.Disabled = !canRestart;

		if (_shutdownButton is not null)
			_shutdownButton.Disabled = !canShutdown;

		if (_shutdownWithoutSaveButton is not null)
			_shutdownWithoutSaveButton.Disabled = !canShutdown;

		if (_statusButton is not null)
			_statusButton.Disabled = !hasController;

		if (_runButton is not null)
			_runButton.Disabled = false;

		if (_commandLine is not null)
			_commandLine.Editable = true;
	}

	private void AppendOutput(string category, string message, DateTimeOffset? timestamp = null)
	{
		DateTimeOffset displayTime = (timestamp ?? DateTimeOffset.Now).ToLocalTime();
		string line = $"{displayTime:HH:mm:ss}  {category.PadRight(6)}  {message}";
		GD.Print($"UltraSim server: {line}");
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
