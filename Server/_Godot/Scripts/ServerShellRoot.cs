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
	private readonly List<Button> _tabButtons = new();
	private readonly List<Control> _tabPages = new();
	private Control? _tabPageHost;
	private Label? _bannerPrimaryValue;
	private Label? _bannerPlayersValue;
	private Label? _bannerAccountsValue;
	private Label? _bannerMobilesValue;
	private Label? _bannerItemsValue;
	private Label? _playersSummaryValue;
	private Label? _accountsSummaryValue;
	private Label? _performanceSummaryValue;
	private GridContainer? _performanceGrid;
	private Button? _startButton;
	private Button? _saveButton;
	private Button? _restartButton;
	private Button? _restartWithoutSaveButton;
	private Button? _shutdownButton;
	private Button? _shutdownWithoutSaveButton;
	private bool _smokeRequireEmptySessions;
	private double _smokeEmptySessionGraceSeconds = 2.0;
	private double _smokeEmptySessionWaitedSeconds;
	private bool _smokeQuitRequested;
	private bool _smokeEmptySessionWaitLogged;
	private bool _shellQuitScheduled;
	private long _bannerSampleCount;
	private double _playerOnlineSampleTotal;
	private double _accountOnlineSampleTotal;

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

		runtimeRootPath ??= ResolveExportRuntimeRootPath();

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

	private static string? ResolveExportRuntimeRootPath()
	{
		if (Engine.IsEditorHint())
			return null;

		if (!string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("ULTRASIM_RUNTIME_ROOT")))
			return null;

		string executablePath = OS.GetExecutablePath();
		if (string.IsNullOrWhiteSpace(executablePath))
			return null;

		string? directory = Path.GetDirectoryName(executablePath);
		return string.IsNullOrWhiteSpace(directory) ? null : directory;
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
		window.Size = new Vector2I(900, 525);
		window.MinSize = window.Size;
		window.Unresizable = true;
		window.CloseRequested += HandleWindowCloseRequested;
	}

	private void BuildInterface()
	{
		Color background = new(0.015f, 0.016f, 0.018f);
		Color panel = new(0.055f, 0.060f, 0.070f);
		Color panelSoft = new(0.085f, 0.095f, 0.115f);
		Color accent = new(0.86f, 0.55f, 0.27f);
		Color accentSoft = new(0.31f, 0.45f, 0.57f);
		Color text = new(0.90f, 0.92f, 0.95f);
		Color muted = new(0.62f, 0.67f, 0.74f);

		var backgroundRect = new ColorRect();
		backgroundRect.Color = background;
		backgroundRect.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(backgroundRect);

		var root = new MarginContainer();
		root.SetAnchorsPreset(LayoutPreset.FullRect);
		root.AddThemeConstantOverride("margin_left", 8);
		root.AddThemeConstantOverride("margin_top", 8);
		root.AddThemeConstantOverride("margin_right", 8);
		root.AddThemeConstantOverride("margin_bottom", 8);
		AddChild(root);

		var main = new VBoxContainer();
		main.SetAnchorsPreset(LayoutPreset.FullRect);
		main.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		main.SizeFlagsVertical = SizeFlags.ExpandFill;
		main.AddThemeConstantOverride("separation", 8);
		root.AddChild(main);

		var tabRow = new HBoxContainer();
		tabRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		tabRow.AddThemeConstantOverride("separation", 4);
		main.AddChild(tabRow);

		var banner = BuildMetricBanner(panel, accent, text, muted);
		main.AddChild(banner);

		_tabPageHost = new Control();
		_tabPageHost.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_tabPageHost.SizeFlagsVertical = SizeFlags.ExpandFill;
		main.AddChild(_tabPageHost);

		AddShellTab(tabRow, "Output", BuildOutputTab(panel, panelSoft, accent, text, muted), panel, panelSoft, accent, text, muted);
		AddShellTab(tabRow, "Players", BuildPlayersTab(panel, panelSoft, accentSoft, text, muted), panel, panelSoft, accent, text, muted);
		AddShellTab(tabRow, "Accounts", BuildAccountsTab(panel, panelSoft, accentSoft, text, muted), panel, panelSoft, accent, text, muted);
		AddShellTab(tabRow, "Performance", BuildPerformanceTab(panel, panelSoft, accentSoft, text, muted), panel, panelSoft, accent, text, muted);
		AddShellTab(tabRow, "NPCs", CreatePlaceholderTab("NPCs", "NPC and mob operator tools will land here.", panel, muted), panel, panelSoft, accent, text, muted);
		AddShellTab(tabRow, "Items", CreatePlaceholderTab("Items", "Item and inventory operator tools will land here.", panel, muted), panel, panelSoft, accent, text, muted);
		SelectShellTab(0);
	}

	private PanelContainer BuildMetricBanner(Color panel, Color accent, Color text, Color muted)
	{
		var banner = new PanelContainer();
		banner.CustomMinimumSize = new Vector2(0, 72);
		banner.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		banner.AddThemeStyleboxOverride("panel", CreatePanelStyle(panel, 8, accent));

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_top", 7);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_bottom", 7);
		banner.AddChild(margin);

		var column = new VBoxContainer();
		column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddThemeConstantOverride("separation", 3);
		margin.AddChild(column);

		_bannerPrimaryValue = CreateBannerLine(text);
		column.AddChild(_bannerPrimaryValue);

		var secondary = new HBoxContainer();
		secondary.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		secondary.AddThemeConstantOverride("separation", 18);
		column.AddChild(secondary);

		_bannerPlayersValue = CreateBannerMetricLabel(muted);
		_bannerAccountsValue = CreateBannerMetricLabel(muted);
		_bannerMobilesValue = CreateBannerMetricLabel(muted);
		_bannerItemsValue = CreateBannerMetricLabel(muted);
		secondary.AddChild(_bannerPlayersValue);
		secondary.AddChild(_bannerAccountsValue);
		secondary.AddChild(_bannerMobilesValue);
		secondary.AddChild(_bannerItemsValue);
		return banner;
	}

	private static Label CreateBannerLine(Color color)
	{
		var label = new Label();
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.ClipText = true;
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	private static Label CreateBannerMetricLabel(Color color)
	{
		var label = CreateBannerLine(color);
		label.ClipText = false;
		label.CustomMinimumSize = new Vector2(118, 0);
		label.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		label.MouseFilter = MouseFilterEnum.Stop;
		return label;
	}

	private void AddShellTab(
		HBoxContainer tabRow,
		string title,
		Control page,
		Color panel,
		Color panelSoft,
		Color accent,
		Color text,
		Color muted)
	{
		if (_tabPageHost is null)
			return;

		int index = _tabPages.Count;
		var button = CreateTabButton(title, panel, panelSoft, accent, text, muted);
		button.Pressed += () => SelectShellTab(index);
		tabRow.AddChild(button);
		_tabButtons.Add(button);

		page.Name = title;
		page.SetAnchorsPreset(LayoutPreset.FullRect);
		page.Visible = false;
		_tabPageHost.AddChild(page);
		_tabPages.Add(page);
	}

	private static Button CreateTabButton(string title, Color panel, Color panelSoft, Color accent, Color text, Color muted)
	{
		var button = new Button();
		button.Text = title;
		button.ToggleMode = true;
		button.CustomMinimumSize = new Vector2(96, 32);
		button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		button.AddThemeColorOverride("font_color", muted);
		button.AddThemeStyleboxOverride("normal", CreatePanelStyle(panel, 8));
		button.AddThemeStyleboxOverride("hover", CreatePanelStyle(panelSoft, 8));
		button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(panelSoft, 8, accent));
		button.AddThemeStyleboxOverride("focus", CreatePanelStyle(panelSoft, 8, accent));
		return button;
	}

	private void SelectShellTab(int index)
	{
		for (int i = 0; i < _tabPages.Count; i++)
		{
			bool selected = i == index;
			_tabPages[i].Visible = selected;
			if (i < _tabButtons.Count)
			{
				_tabButtons[i].ButtonPressed = selected;
				_tabButtons[i].AddThemeColorOverride("font_color", selected ? new Color(0.90f, 0.92f, 0.95f) : new Color(0.62f, 0.67f, 0.74f));
			}
		}
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

		var rightPanel = new PanelContainer();
		rightPanel.CustomMinimumSize = new Vector2(232, 0);
		rightPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		rightPanel.AddThemeStyleboxOverride("panel", CreatePanelStyle(panel, 10));
		shellLayout.AddChild(rightPanel);

		var rightScroll = new ScrollContainer();
		rightScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rightScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		rightScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		rightScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		rightPanel.AddChild(rightScroll);

		var rightMargin = new MarginContainer();
		rightMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rightMargin.AddThemeConstantOverride("margin_left", 8);
		rightMargin.AddThemeConstantOverride("margin_top", 8);
		rightMargin.AddThemeConstantOverride("margin_right", 12);
		rightMargin.AddThemeConstantOverride("margin_bottom", 8);
		rightScroll.AddChild(rightMargin);

		var rightColumn = new VBoxContainer();
		rightColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rightColumn.AddThemeConstantOverride("separation", 6);
		rightMargin.AddChild(rightColumn);

		rightColumn.AddChild(CreateSectionLabel("Controls", text));

		_startButton = CreateCommandButton("Start", ServerControlCommand.Start(), accent, text);
		_saveButton = CreateCommandButton("Save", ServerControlCommand.Save(), panelSoft, text);
		_restartButton = CreateCommandButton("Restart", ServerControlCommand.Restart(), panelSoft, text);
		_restartWithoutSaveButton = CreateCommandButton("Restart No Save", ServerControlCommand.RestartWithoutSave(), panelSoft, text);
		_shutdownButton = CreateCommandButton("Shutdown", ServerControlCommand.Shutdown(), panelSoft, text);
		_shutdownWithoutSaveButton = CreateCommandButton("Shutdown No Save", ServerControlCommand.ShutdownWithoutSave(), panelSoft, text);

		rightColumn.AddChild(_startButton);
		rightColumn.AddChild(_saveButton);
		rightColumn.AddChild(_restartButton);
		rightColumn.AddChild(_restartWithoutSaveButton);
		rightColumn.AddChild(_shutdownButton);
		rightColumn.AddChild(_shutdownWithoutSaveButton);

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

		var scroll = new ScrollContainer();
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		logFrame.AddChild(scroll);

		_performanceGrid = new GridContainer();
		_performanceGrid.Columns = 10;
		_performanceGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_performanceGrid.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		_performanceGrid.AddThemeConstantOverride("h_separation", 10);
		_performanceGrid.AddThemeConstantOverride("v_separation", 4);
		scroll.AddChild(_performanceGrid);
		SetPerformancePlaceholder(text, muted);

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
			AppendOutput("HELP", "Commands: start, status, backups, restore <backup-folder-name>, save, restart, restart-nosave, shutdown, shutdown-nosave, quit, exit, help");
			return;
		}

		if (TryParseRestoreBackupCommand(raw, out string backupDirectoryName))
		{
			ExecuteCommand(ServerControlCommand.RestoreBackup(backupDirectoryName));
			return;
		}

		ServerControlCommand? command = raw.ToLowerInvariant() switch
		{
			"start" => ServerControlCommand.Start(),
			"status" => ServerControlCommand.Status(),
			"backups" => ServerControlCommand.ListBackups(),
			"list-backups" => ServerControlCommand.ListBackups(),
			"save" => ServerControlCommand.Save(),
			"restart" => ServerControlCommand.Restart(),
			"restart-nosave" => ServerControlCommand.RestartWithoutSave(),
			"restart-no-save" => ServerControlCommand.RestartWithoutSave(),
			"restart nosave" => ServerControlCommand.RestartWithoutSave(),
			"restart no-save" => ServerControlCommand.RestartWithoutSave(),
			"restart without-save" => ServerControlCommand.RestartWithoutSave(),
			"restart --no-save" => ServerControlCommand.RestartWithoutSave(),
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

	private static bool TryParseRestoreBackupCommand(string command, out string backupDirectoryName)
	{
		const string spacedPrefix = "restore ";
		const string dashedPrefix = "restore-backup ";
		if (command.StartsWith(spacedPrefix, StringComparison.OrdinalIgnoreCase))
		{
			backupDirectoryName = command[spacedPrefix.Length..].Trim();
			return !string.IsNullOrWhiteSpace(backupDirectoryName);
		}

		if (command.StartsWith(dashedPrefix, StringComparison.OrdinalIgnoreCase))
		{
			backupDirectoryName = command[dashedPrefix.Length..].Trim();
			return !string.IsNullOrWhiteSpace(backupDirectoryName);
		}

		backupDirectoryName = string.Empty;
		return false;
	}

	private void ExecuteCommand(ServerControlCommand command)
	{
		if (_controller is null)
		{
			if (command.Kind == ServerControlCommandKind.Shutdown)
			{
				AppendOutput("RESULT", "Shutdown: Accepted - shell closed");
				ScheduleShellQuit();
				return;
			}

			if (command.Kind != ServerControlCommandKind.Start)
			{
				if (command.Kind is not ServerControlCommandKind.ListBackups and not ServerControlCommandKind.RestoreBackup)
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

			if (_controller is null && !TryEnsureController())
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
		if (command.Kind == ServerControlCommandKind.Shutdown &&
			result.Kind is ServerCommandResultKind.Accepted or ServerCommandResultKind.NoOp)
		{
			ScheduleShellQuit();
		}
	}

	private void ScheduleShellQuit()
	{
		if (_shellQuitScheduled)
			return;

		_shellQuitScheduled = true;
		SceneTreeTimer timer = GetTree().CreateTimer(0.05);
		timer.Timeout += () => GetTree().Quit();
	}

	private void HandleWindowCloseRequested()
	{
		if (_shellQuitScheduled)
			return;

		if (_controller is null)
		{
			ScheduleShellQuit();
			return;
		}

		ExecuteCommand(ServerControlCommand.Shutdown());
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

		if (_bannerPrimaryValue is not null)
			_bannerPrimaryValue.Text = "ticks 0    meanTickMs 0    maxTickMs 0";

		RefreshControls(ServerApplicationState.Faulted);
	}

	private void RefreshStatus()
	{
		if (_controller is null)
		{
			UpdateMetricBanner(null, null, Array.Empty<ServerPlayerSessionSnapshot>(), Array.Empty<ServerAccountSummarySnapshot>());

			if (_playersSummaryValue is not null)
				_playersSummaryValue.Text = "No active players.";

			if (_accountsSummaryValue is not null)
				_accountsSummaryValue.Text = "No accounts loaded.";

			if (_performanceSummaryValue is not null)
				_performanceSummaryValue.Text = "Waiting for Start.";

			if (_performanceGrid is not null)
				SetPerformancePlaceholder(new Color(0.90f, 0.92f, 0.95f), new Color(0.62f, 0.67f, 0.74f));

			RefreshControls(ServerApplicationState.Created);
			return;
		}

		ServerStatusSnapshot status = _controller.GetStatusSnapshot();
		ServerMetricsSnapshot metrics = _controller.GetMetricsSnapshot();
		IReadOnlyList<ServerSystemRuntimeSnapshot> systems = _controller.GetSystemRuntimeSnapshots();
		IReadOnlyList<ServerPlayerSessionSnapshot> players = _controller.GetPlayerSessionsSnapshot();
		IReadOnlyList<ServerAccountSummarySnapshot> accounts = _controller.GetAccountSummariesSnapshot();

		UpdateMetricBanner(status, metrics, players, accounts);

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

	private void UpdateMetricBanner(
		ServerStatusSnapshot? status,
		ServerMetricsSnapshot? metrics,
		IReadOnlyList<ServerPlayerSessionSnapshot> players,
		IReadOnlyList<ServerAccountSummarySnapshot> accounts)
	{
		if (!status.HasValue || !metrics.HasValue)
		{
			_bannerSampleCount = 0;
			_playerOnlineSampleTotal = 0.0;
			_accountOnlineSampleTotal = 0.0;
			if (_bannerPrimaryValue is not null)
				_bannerPrimaryValue.Text = "ticks 0    meanTickMs 0    maxTickMs 0";
			SetBannerMetric(_bannerPlayersValue, "players 0/0/0", "players on/avg/total: 0 / 0 / 0");
			SetBannerMetric(_bannerAccountsValue, "accounts 0/0/0", "accounts on/avg/total: 0 / 0 / 0");
			SetBannerMetric(_bannerMobilesValue, "mobiles 0/0", "mobiles spawned/prefabs: 0 / 0");
			SetBannerMetric(_bannerItemsValue, "items 0/0", "items spawned/prefabs: 0 / 0");
			return;
		}

		ServerStatusSnapshot statusValue = status.Value;
		ServerMetricsSnapshot metricsValue = metrics.Value;
		int onlineAccounts = CountOnlineAccounts(accounts);
		_bannerSampleCount++;
		_playerOnlineSampleTotal += statusValue.ActiveSessions;
		_accountOnlineSampleTotal += onlineAccounts;

		double averagePlayers = _playerOnlineSampleTotal / _bannerSampleCount;
		double averageAccounts = _accountOnlineSampleTotal / _bannerSampleCount;
		if (_bannerPrimaryValue is not null)
		{
			_bannerPrimaryValue.Text =
				$"ticks {statusValue.TickCount}    " +
				$"meanTickMs {metricsValue.MeanTickMilliseconds:0.###}    maxTickMs {metricsValue.MaxTickMilliseconds:0.###}";
		}

		SetBannerMetric(
			_bannerPlayersValue,
			$"players {statusValue.ActiveSessions}/{averagePlayers:0.#}/{players.Count}",
			$"players on/avg/total: {statusValue.ActiveSessions} / {averagePlayers:0.#} / {players.Count}");
		SetBannerMetric(
			_bannerAccountsValue,
			$"accounts {onlineAccounts}/{averageAccounts:0.#}/{accounts.Count}",
			$"accounts on/avg/total: {onlineAccounts} / {averageAccounts:0.#} / {accounts.Count}");
		SetBannerMetric(_bannerMobilesValue, "mobiles 0/0", "mobiles spawned/prefabs: 0 / 0");
		SetBannerMetric(_bannerItemsValue, "items 0/0", "items spawned/prefabs: 0 / 0");
	}

	private static void SetBannerMetric(Label? label, string text, string tooltip)
	{
		if (label is null)
			return;

		label.Text = text;
		label.TooltipText = tooltip;
	}

	private static int CountOnlineAccounts(IReadOnlyList<ServerAccountSummarySnapshot> accounts)
	{
		int count = 0;
		for (int i = 0; i < accounts.Count; i++)
		{
			if (accounts[i].IsOnline)
				count++;
		}

		return count;
	}

	private void RefreshPerformance(ServerMetricsSnapshot metrics, IReadOnlyList<ServerSystemRuntimeSnapshot> systems)
	{
		if (_performanceSummaryValue is not null)
		{
			_performanceSummaryValue.Text =
				$"ticks={metrics.TotalTicks}  mean={metrics.MeanTickMilliseconds:0.###} ms  " +
				$"max={metrics.MaxTickMilliseconds:0.###} ms  systems={systems.Count}";
		}

		if (_performanceGrid is null)
			return;

		ClearPerformanceGrid();
		AddPerformanceHeader();
		if (systems.Count == 0)
			return;

		for (int i = 0; i < systems.Count; i++)
		{
			ServerSystemRuntimeSnapshot system = systems[i];
			string mode = system.Manual ? "manual" : (system.Enabled ? "enabled" : "disabled");
			string period = $"{system.PeriodTicks}/{system.StartOffset}";
			AddPerformanceRow(
				system.Name,
				system.Stage,
				mode,
				period,
				system.LastUpdateMilliseconds.ToString("0.###"),
				system.AverageUpdateMilliseconds.ToString("0.###"),
				system.PeakUpdateMilliseconds.ToString("0.###"),
				system.MinUpdateMilliseconds.ToString("0.###"),
				system.UpdateCount.ToString(),
				system.ControlCount.ToString());
		}
	}

	private void SetPerformancePlaceholder(Color text, Color muted)
	{
		if (_performanceGrid is null)
			return;

		ClearPerformanceGrid();
		AddPerformanceHeader();
		AddPerformanceCell("Waiting for Start", text, 180);
		AddPerformanceCell("", muted, 100);
		AddPerformanceCell("", muted, 76);
		AddPerformanceCell("", muted, 70, alignRight: true);
		AddPerformanceCell("", muted, 76, alignRight: true);
		AddPerformanceCell("", muted, 76, alignRight: true);
		AddPerformanceCell("", muted, 76, alignRight: true);
		AddPerformanceCell("", muted, 76, alignRight: true);
		AddPerformanceCell("", muted, 86, alignRight: true);
		AddPerformanceCell("", muted, 72, alignRight: true);
	}

	private void AddPerformanceHeader()
	{
		Color text = new(0.90f, 0.92f, 0.95f);
		AddPerformanceCell("Name", text, 180);
		AddPerformanceCell("Stage", text, 100);
		AddPerformanceCell("Mode", text, 76);
		AddPerformanceCell("Period", text, 70, alignRight: true);
		AddPerformanceCell("Last", text, 76, alignRight: true);
		AddPerformanceCell("Avg", text, 76, alignRight: true);
		AddPerformanceCell("Peak", text, 76, alignRight: true);
		AddPerformanceCell("Min", text, 76, alignRight: true);
		AddPerformanceCell("Runs", text, 86, alignRight: true);
		AddPerformanceCell("Controls", text, 72, alignRight: true);
	}

	private void AddPerformanceRow(
		string name,
		string stage,
		string mode,
		string period,
		string last,
		string average,
		string peak,
		string min,
		string runs,
		string controls)
	{
		Color muted = new(0.62f, 0.67f, 0.74f);
		AddPerformanceCell(name, muted, 180);
		AddPerformanceCell(stage, muted, 100);
		AddPerformanceCell(mode, muted, 76);
		AddPerformanceCell(period, muted, 70, alignRight: true);
		AddPerformanceCell(last, muted, 76, alignRight: true);
		AddPerformanceCell(average, muted, 76, alignRight: true);
		AddPerformanceCell(peak, muted, 76, alignRight: true);
		AddPerformanceCell(min, muted, 76, alignRight: true);
		AddPerformanceCell(runs, muted, 86, alignRight: true);
		AddPerformanceCell(controls, muted, 72, alignRight: true);
	}

	private void AddPerformanceCell(string value, Color color, float width, bool alignRight = false)
	{
		if (_performanceGrid is null)
			return;

		var label = new Label();
		label.Text = value;
		label.ClipText = true;
		label.CustomMinimumSize = new Vector2(width, 22);
		label.HorizontalAlignment = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
		label.AddThemeColorOverride("font_color", color);
		_performanceGrid.AddChild(label);
	}

	private void ClearPerformanceGrid()
	{
		if (_performanceGrid is null)
			return;

		foreach (Node child in _performanceGrid.GetChildren())
		{
			_performanceGrid.RemoveChild(child);
			child.QueueFree();
		}
	}

	private void RefreshControls(ServerApplicationState state)
	{
		bool hasController = _controller is not null;
		bool canStart = !hasController || state is ServerApplicationState.Created or ServerApplicationState.Initialized or ServerApplicationState.Stopped;
		bool canSave = hasController && state is ServerApplicationState.Initialized or ServerApplicationState.Running or ServerApplicationState.Faulted;
		bool canRestart = hasController && state != ServerApplicationState.Created;
		bool canShutdown = hasController && state != ServerApplicationState.Stopped;

		if (_startButton is not null)
			_startButton.Disabled = !canStart;

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
