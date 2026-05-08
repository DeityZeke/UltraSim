using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using UltraSim.Server.Hosting;
using UltraSim.Server.Persistence;
using UltraSim.Server.Scripting;

namespace UltraSim.Server.Godot;

public partial class ServerShellRoot : Control
{
	private const int MaxOutputLines = 400;
	private enum PlayerSortColumn
	{
		PlayerName,
		RemoteAddress,
		SessionId,
		AccountName,
		State,
		CurrentUptime,
		TotalPlayTime,
		AccessLevel
	}

	private enum AccountSortColumn
	{
		DisplayName,
		AccountId,
		OnlineState,
		BanState,
		AccessLevel,
		LastLogin,
		TotalPlayTime,
		RemoteAddress
	}

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
	private Label? _discoverySummaryValue;
	private Label? _discoveryEnabledValue;
	private Label? _discoveryRegistryValue;
	private Label? _discoveryPublishModeValue;
	private Label? _discoveryServerIdValue;
	private Label? _discoveryAdvertisedValue;
	private Label? _discoveryStatusEndpointValue;
	private Label? _discoveryPublisherValue;
	private Label? _discoveryLastPublishValue;
	private Label? _discoveryLastMessageValue;
	private ItemList? _playersList;
	private Label? _playersSelectedValue;
	private Button? _playerKickButton;
	private Button? _playerBanButton;
	private Button? _playerPromoteButton;
	private Button? _playerDemoteButton;
	private Button? _playerPermissionsButton;
	private ItemList? _accountsList;
	private Label? _accountsSelectedValue;
	private Button? _accountBanButton;
	private Button? _accountPromoteButton;
	private Button? _accountDemoteButton;
	private Button? _accountPermissionsButton;
	private PanelContainer? _permissionsOverlay;
	private Label? _permissionsTitleLabel;
	private OptionButton? _permissionsAccessLevel;
	private TextEdit? _permissionsRevokedText;
	private TextEdit? _permissionsSpecialText;
	private Label? _permissionsCatalogLabel;
	private PanelContainer? _permissionPickerOverlay;
	private Label? _permissionPickerTitleLabel;
	private ItemList? _permissionPickerList;
	private TextEdit? _permissionPickerTarget;
	private bool _permissionPickerAddMode;
	private string _permissionsAccountId = string.Empty;
	private string _permissionsPlayerName = string.Empty;
	private VBoxContainer? _settingsList;
	private PanelContainer? _restartRequiredBannerPanel;
	private Label? _restartRequiredBannerText;
	private Label? _settingsTitlePathLabel;
	private Label? _settingsNote;
	private Button? _cancelSettingsButton;
	private Button? _applySettingsButton;
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
	private bool _settingsRestartRequiredPending;
	private ServerApplicationState _lastServerState = ServerApplicationState.Created;
	private string? _selectedPlayerSessionId;
	private string? _selectedAccountId;
	private IReadOnlyList<ServerPlayerSessionSnapshot> _currentPlayers = Array.Empty<ServerPlayerSessionSnapshot>();
	private IReadOnlyList<ServerAccountSummarySnapshot> _currentAccounts = Array.Empty<ServerAccountSummarySnapshot>();
	private string _playersListSignature = string.Empty;
	private string _accountsListSignature = string.Empty;
	private PlayerSortColumn _playerSortColumn = PlayerSortColumn.PlayerName;
	private bool _playerSortAscending = true;
	private AccountSortColumn _accountSortColumn = AccountSortColumn.DisplayName;
	private bool _accountSortAscending = true;
	private IReadOnlyList<ServerSettingDescriptor> _settingsDescriptors = Array.Empty<ServerSettingDescriptor>();
	private readonly Dictionary<string, Control> _settingsRows = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Control> _settingsEditors = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Label> _settingsDirtyMarkers = new(StringComparer.Ordinal);
	private Color _settingsPanelSoftColor;
	private Color _settingsAccentSoftColor;
	private Color _settingsTextColor;
	private Color _settingsMutedColor;
	private static readonly PermissionCatalogEntry[] PermissionCatalog =
	[
		new("command.help", "Show available chat commands.", 0),
		new("command.ping", "Check the command path.", 0),
		new("command.panel", "Open script-registered UI panels.", 0),
		new("command.spawn", "Spawn a registered script prefab.", 60),
		new("command.announce", "Publish a server announcement.", 100),
		new("admin.entity.inspect", "Inspect ECS entity/component state.", 80),
		new("admin.account.access", "Edit account access metadata.", 80),
		new("admin.account.kick", "Kick a connected account/player session.", 80),
		new("admin.account.ban", "Ban an account/player.", 80),
		new("admin.server.shutdown", "Stop or shut down server runtime.", 100),
		new("command.*", "Wildcard grant/deny for all command permissions.", 100),
		new("admin.*", "Wildcard grant/deny for all admin permissions.", 100)
	];

	private readonly record struct PermissionCatalogEntry(string Key, string Description, int AccessLevel);

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

		RefreshSettingsTab();
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
		string? runtimeRootPath = ReadRuntimeRootPathFromArgs() ?? ResolveExportRuntimeRootPath();
		GodotServerShellOptions options = GodotServerShellOptions.Default with
		{
			RuntimeRootPath = runtimeRootPath
		};

		options = LoadConfiguredShellOptions(options);
		options = ApplyCommandLineOptions(options, out startOnReady);
		return options;
	}

	private static string? ReadRuntimeRootPathFromArgs()
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (!arg.StartsWith("--us-server-runtime-root=", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string value = arg["--us-server-runtime-root=".Length..].Trim();
			return string.IsNullOrWhiteSpace(value) ? null : value;
		}

		return null;
	}

	private static GodotServerShellOptions ApplyCommandLineOptions(GodotServerShellOptions options, out bool startOnReady)
	{
		string profileName = options.ProfileName;
		int tickMilliseconds = options.TickMilliseconds;
		int maxCatchUpTicks = options.MaxCatchUpTicksPerPump;
		int networkPort = options.NetworkPort;
		string bindAddress = options.BindAddress;
		double sessionAuthTimeoutSeconds = options.SessionAuthTimeoutSeconds;
		double sessionKeepAliveTimeoutSeconds = options.SessionKeepAliveTimeoutSeconds;
		string commandPrefix = options.CommandPrefix;
		int maxAutoCreatedAccountsPerRemoteAddress = options.MaxAutoCreatedAccountsPerRemoteAddress;
		bool saveOnStop = options.SaveOnStop;
		bool enableMovementWorld = options.EnableMovementWorld;
		string? runtimeRootPath = options.RuntimeRootPath;
		int maxClients = options.MaxClients;
		double autoSaveIntervalSeconds = options.AutoSaveIntervalSeconds;
		ServerPersistenceBackupStorageMode backupStorageMode = options.BackupStorageMode;
		int backupCount = options.BackupCount;
		int backupZstdCompressionLevel = options.BackupZstdCompressionLevel;
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
			else if (arg.StartsWith("--us-server-backup-storage=", StringComparison.OrdinalIgnoreCase))
			{
				string value = arg["--us-server-backup-storage=".Length..].Trim();
				if (ServerPersistenceBackupCompressorFactory.TryParseMode(value, out ServerPersistenceBackupStorageMode parsedBackupStorageMode))
				{
					backupStorageMode = parsedBackupStorageMode;
				}
			}
			else if (arg.StartsWith("--us-server-backup-zstd-level=", StringComparison.OrdinalIgnoreCase))
			{
				if (int.TryParse(arg["--us-server-backup-zstd-level=".Length..], out int parsedBackupZstdCompressionLevel))
				{
					backupZstdCompressionLevel = parsedBackupZstdCompressionLevel;
				}
			}
			else if (arg.StartsWith("--us-server-backup-count=", StringComparison.OrdinalIgnoreCase))
			{
				if (int.TryParse(arg["--us-server-backup-count=".Length..], out int parsedBackupCount) &&
					parsedBackupCount is >= 0 and <= 3)
				{
					backupCount = parsedBackupCount;
				}
			}
			else if (arg.StartsWith("--us-server-command-prefix=", StringComparison.OrdinalIgnoreCase))
			{
				string value = arg["--us-server-command-prefix=".Length..].Trim();
				if (!string.IsNullOrWhiteSpace(value))
				{
					commandPrefix = value;
				}
			}
			else if (arg.StartsWith("--us-server-port=", StringComparison.OrdinalIgnoreCase))
			{
				if (int.TryParse(arg["--us-server-port=".Length..], out int parsedPort) && parsedPort > 0)
				{
					networkPort = parsedPort;
				}
			}
			else if (arg.StartsWith("--us-server-bind-address=", StringComparison.OrdinalIgnoreCase))
			{
				string value = arg["--us-server-bind-address=".Length..].Trim();
				if (!string.IsNullOrWhiteSpace(value))
				{
					bindAddress = value;
				}
			}
			else if (arg.StartsWith("--us-server-max-clients=", StringComparison.OrdinalIgnoreCase))
			{
				if (int.TryParse(arg["--us-server-max-clients=".Length..], out int parsedMaxClients) && parsedMaxClients >= 0)
				{
					maxClients = parsedMaxClients;
				}
			}
			else if (arg.StartsWith("--us-server-autosave-interval-seconds=", StringComparison.OrdinalIgnoreCase))
			{
				if (double.TryParse(arg["--us-server-autosave-interval-seconds=".Length..], out double parsedAutoSaveIntervalSeconds) &&
					parsedAutoSaveIntervalSeconds >= 0.0)
				{
					autoSaveIntervalSeconds = parsedAutoSaveIntervalSeconds;
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
			else if (string.Equals(arg, "--us-server-save-on-stop", StringComparison.OrdinalIgnoreCase))
			{
				saveOnStop = true;
			}
		}

		return options with
		{
			ProfileName = profileName,
			TickMilliseconds = tickMilliseconds,
			MaxCatchUpTicksPerPump = maxCatchUpTicks,
			SaveOnStop = saveOnStop,
			EnableMovementWorld = enableMovementWorld,
			NetworkPort = networkPort,
			BindAddress = bindAddress,
			MaxClients = maxClients,
			AutoSaveIntervalSeconds = autoSaveIntervalSeconds,
			SessionAuthTimeoutSeconds = sessionAuthTimeoutSeconds,
			SessionKeepAliveTimeoutSeconds = sessionKeepAliveTimeoutSeconds,
			CommandPrefix = commandPrefix,
			MaxAutoCreatedAccountsPerRemoteAddress = maxAutoCreatedAccountsPerRemoteAddress,
			RuntimeRootPath = runtimeRootPath,
			BackupStorageMode = backupStorageMode,
			BackupCount = backupCount,
			BackupZstdCompressionLevel = backupZstdCompressionLevel
		};
	}

	private static GodotServerShellOptions LoadConfiguredShellOptions(GodotServerShellOptions options)
	{
		try
		{
			UltraSimRuntimeRootLayout runtimeRoot = UltraSimRuntimeRootResolver.Resolve(options.RuntimeRootPath);
			string path = GetSettingsConfigPath(runtimeRoot);
			ServerSettingsConfigDocument document = new ServerSettingsConfigStore().LoadOrEmpty(path);
			ServerSettingsRegistry registry = BuildSettingsRegistry(options, runtimeRoot);
			var proposed = new List<ServerSettingProposedValue>();
			foreach (ServerSettingProposedValue value in document.ToProposedValues())
			{
				if (registry.TryGetSetting(value.Key, out _))
				{
					proposed.Add(value);
				}
			}

			ServerSettingsChangeSetValidationResult validation = registry.ValidateChanges(proposed);
			if (!validation.IsValid)
			{
				foreach (ServerSettingChangeError error in validation.Errors)
					GD.PushWarning($"UltraSim server setting '{error.Key}' was ignored: {error.Error}");
			}

			return ApplyValidatedSettingsToOptions(options, validation.Changes);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"UltraSim server settings config could not be loaded: {ex.Message}");
			return options;
		}
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

		main.AddChild(BuildRestartRequiredBanner(text));

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
		AddShellTab(tabRow, "Discovery", BuildDiscoveryTab(panel, panelSoft, accentSoft, text, muted), panel, panelSoft, accent, text, muted);
		AddShellTab(tabRow, "NPCs", CreatePlaceholderTab("NPCs", "NPC and mob operator tools will land here.", panel, muted), panel, panelSoft, accent, text, muted);
		AddShellTab(tabRow, "Items", CreatePlaceholderTab("Items", "Item and inventory operator tools will land here.", panel, muted), panel, panelSoft, accent, text, muted);
		AddTabSpacer(tabRow);
		AddShellTab(tabRow, "Settings", BuildSettingsTab(panel, panelSoft, accentSoft, text, muted), panel, panelSoft, accent, text, muted);
		AddChild(BuildPermissionsOverlay(panel, panelSoft, accentSoft, text, muted));
		AddChild(BuildPermissionPickerOverlay(panel, panelSoft, accentSoft, text, muted));
		SelectShellTab(0);
	}

	private PanelContainer BuildRestartRequiredBanner(Color text)
	{
		_restartRequiredBannerPanel = new PanelContainer();
		_restartRequiredBannerPanel.CustomMinimumSize = new Vector2(0, 34);
		_restartRequiredBannerPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_restartRequiredBannerPanel.Visible = false;
		_restartRequiredBannerPanel.AddThemeStyleboxOverride("panel", CreatePanelStyle(new Color(0.58f, 0.04f, 0.04f), 6, new Color(0.96f, 0.20f, 0.16f)));

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_top", 6);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_bottom", 6);
		_restartRequiredBannerPanel.AddChild(margin);

		_restartRequiredBannerText = new Label();
		_restartRequiredBannerText.Text = "Server restart required to apply new settings.";
		_restartRequiredBannerText.HorizontalAlignment = HorizontalAlignment.Center;
		_restartRequiredBannerText.AddThemeColorOverride("font_color", text);
		margin.AddChild(_restartRequiredBannerText);
		return _restartRequiredBannerPanel;
	}

	private static void AddTabSpacer(HBoxContainer tabRow)
	{
		var spacer = new Control();
		spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		tabRow.AddChild(spacer);
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

		var headerRow = new HBoxContainer();
		headerRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		headerRow.AddThemeConstantOverride("separation", 4);
		column.AddChild(headerRow);
		headerRow.AddChild(CreatePlayerSortButton("Name", PlayerSortColumn.PlayerName, panelSoft, text));
		headerRow.AddChild(CreatePlayerSortButton("IP", PlayerSortColumn.RemoteAddress, panelSoft, text));
		headerRow.AddChild(CreatePlayerSortButton("Session", PlayerSortColumn.SessionId, panelSoft, text));
		headerRow.AddChild(CreatePlayerSortButton("Account", PlayerSortColumn.AccountName, panelSoft, text));
		headerRow.AddChild(CreatePlayerSortButton("State", PlayerSortColumn.State, panelSoft, text));
		headerRow.AddChild(CreatePlayerSortButton("Up", PlayerSortColumn.CurrentUptime, panelSoft, text));
		headerRow.AddChild(CreatePlayerSortButton("Total", PlayerSortColumn.TotalPlayTime, panelSoft, text));

		var listFrame = new PanelContainer();
		listFrame.SizeFlagsVertical = SizeFlags.ExpandFill;
		listFrame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panelSoft, 10, accentSoft));
		column.AddChild(listFrame);

		_playersList = new ItemList();
		_playersList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_playersList.SizeFlagsVertical = SizeFlags.ExpandFill;
		_playersList.SelectMode = ItemList.SelectModeEnum.Single;
		_playersList.ItemSelected += index =>
		{
			int selected = (int)index;
			if ((uint)selected < (uint)_currentPlayers.Count)
				_selectedPlayerSessionId = _currentPlayers[selected].SessionId;
			RefreshPlayerSelection();
		};
		listFrame.AddChild(_playersList);

		_playersSelectedValue = new Label();
		_playersSelectedValue.AddThemeColorOverride("font_color", muted);
		_playersSelectedValue.ClipText = true;
		column.AddChild(_playersSelectedValue);

		var actionRow = new HBoxContainer();
		actionRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		actionRow.AddThemeConstantOverride("separation", 8);
		column.AddChild(actionRow);

		_playerKickButton = CreateActionButton("Kick", panelSoft, text);
		_playerKickButton.Disabled = true;
		_playerKickButton.Pressed += KickSelectedPlayer;
		actionRow.AddChild(_playerKickButton);
		_playerBanButton = CreateActionButton("Ban", panelSoft, text);
		_playerBanButton.Disabled = true;
		_playerBanButton.Pressed += BanSelectedPlayerAccount;
		actionRow.AddChild(_playerBanButton);
		_playerPromoteButton = CreateActionButton("Promote", panelSoft, text);
		_playerPromoteButton.Disabled = true;
		_playerPromoteButton.Pressed += PromoteSelectedPlayerAccount;
		actionRow.AddChild(_playerPromoteButton);
		_playerDemoteButton = CreateActionButton("Demote", panelSoft, text);
		_playerDemoteButton.Disabled = true;
		_playerDemoteButton.Pressed += DemoteSelectedPlayerAccount;
		actionRow.AddChild(_playerDemoteButton);
		actionRow.AddChild(CreateDisabledButton("Whitelist", panelSoft, text));
		actionRow.AddChild(CreateDisabledButton("Blacklist", panelSoft, text));
		_playerPermissionsButton = CreateActionButton("Permissions", accentSoft, text);
		_playerPermissionsButton.Disabled = true;
		_playerPermissionsButton.Pressed += OpenSelectedPlayerPermissions;
		actionRow.AddChild(_playerPermissionsButton);
		actionRow.AddChild(CreateDisabledButton("Inspect", panelSoft, text));

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

		var headerRow = new HBoxContainer();
		headerRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		headerRow.AddThemeConstantOverride("separation", 4);
		column.AddChild(headerRow);
		headerRow.AddChild(CreateAccountSortButton("Name", AccountSortColumn.DisplayName, panelSoft, text));
		headerRow.AddChild(CreateAccountSortButton("Account", AccountSortColumn.AccountId, panelSoft, text));
		headerRow.AddChild(CreateAccountSortButton("State", AccountSortColumn.OnlineState, panelSoft, text));
		headerRow.AddChild(CreateAccountSortButton("Ban", AccountSortColumn.BanState, panelSoft, text));
		headerRow.AddChild(CreateAccountSortButton("Access", AccountSortColumn.AccessLevel, panelSoft, text));
		headerRow.AddChild(CreateAccountSortButton("Last", AccountSortColumn.LastLogin, panelSoft, text));
		headerRow.AddChild(CreateAccountSortButton("Total", AccountSortColumn.TotalPlayTime, panelSoft, text));
		headerRow.AddChild(CreateAccountSortButton("IP", AccountSortColumn.RemoteAddress, panelSoft, text));

		var detailsFrame = new PanelContainer();
		detailsFrame.SizeFlagsVertical = SizeFlags.ExpandFill;
		detailsFrame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panelSoft, 10, accentSoft));
		column.AddChild(detailsFrame);

		_accountsList = new ItemList();
		_accountsList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_accountsList.SizeFlagsVertical = SizeFlags.ExpandFill;
		_accountsList.SelectMode = ItemList.SelectModeEnum.Single;
		_accountsList.ItemSelected += index =>
		{
			int selected = (int)index;
			if ((uint)selected < (uint)_currentAccounts.Count)
				_selectedAccountId = _currentAccounts[selected].AccountId;
			RefreshAccountSelection();
		};
		detailsFrame.AddChild(_accountsList);

		_accountsSelectedValue = new Label();
		_accountsSelectedValue.AddThemeColorOverride("font_color", muted);
		_accountsSelectedValue.ClipText = true;
		column.AddChild(_accountsSelectedValue);

		var actionRow = new HBoxContainer();
		actionRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		actionRow.AddThemeConstantOverride("separation", 8);
		column.AddChild(actionRow);

		actionRow.AddChild(CreateDisabledButton("Add", accentSoft, text));
		actionRow.AddChild(CreateDisabledButton("Remove", panelSoft, text));
		_accountBanButton = CreateActionButton("Ban", panelSoft, text);
		_accountBanButton.Disabled = true;
		_accountBanButton.Pressed += ToggleSelectedAccountBan;
		actionRow.AddChild(_accountBanButton);
		_accountPromoteButton = CreateActionButton("Promote", panelSoft, text);
		_accountPromoteButton.Disabled = true;
		_accountPromoteButton.Pressed += PromoteSelectedAccount;
		actionRow.AddChild(_accountPromoteButton);
		_accountDemoteButton = CreateActionButton("Demote", panelSoft, text);
		_accountDemoteButton.Disabled = true;
		_accountDemoteButton.Pressed += DemoteSelectedAccount;
		actionRow.AddChild(_accountDemoteButton);
		actionRow.AddChild(CreateDisabledButton("Whitelist", panelSoft, text));
		actionRow.AddChild(CreateDisabledButton("Blacklist", panelSoft, text));
		_accountPermissionsButton = CreateActionButton("Permissions", accentSoft, text);
		_accountPermissionsButton.Disabled = true;
		_accountPermissionsButton.Pressed += OpenSelectedAccountPermissions;
		actionRow.AddChild(_accountPermissionsButton);
		actionRow.AddChild(CreateDisabledButton("Inspect", panelSoft, text));

		return frame;
	}

	private Control BuildPermissionsOverlay(Color panel, Color panelSoft, Color accentSoft, Color text, Color muted)
	{
		_permissionsOverlay = new PanelContainer();
		_permissionsOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
		_permissionsOverlay.Visible = false;
		_permissionsOverlay.MouseFilter = MouseFilterEnum.Stop;
		_permissionsOverlay.AddThemeStyleboxOverride("panel", CreatePanelStyle(new Color(0f, 0f, 0f, 0.45f), 0));

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		_permissionsOverlay.AddChild(center);

		var dialog = new PanelContainer();
		dialog.CustomMinimumSize = new Vector2(640, 430);
		dialog.AddThemeStyleboxOverride("panel", CreatePanelStyle(panel, 10, accentSoft));
		center.AddChild(dialog);

		var column = new VBoxContainer();
		column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.SizeFlagsVertical = SizeFlags.ExpandFill;
		column.AddThemeConstantOverride("separation", 8);
		dialog.AddChild(column);

		var titleRow = new HBoxContainer();
		titleRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddChild(titleRow);

		_permissionsTitleLabel = CreateSectionLabel("Permissions", text);
		_permissionsTitleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleRow.AddChild(_permissionsTitleLabel);

		var close = CreateActionButton("X", panelSoft, text);
		close.CustomMinimumSize = new Vector2(36, 28);
		close.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		close.TooltipText = "Close permissions editor.";
		close.Pressed += ClosePermissionsOverlay;
		titleRow.AddChild(close);

		var accessRow = new HBoxContainer();
		accessRow.AddThemeConstantOverride("separation", 8);
		column.AddChild(accessRow);
		accessRow.AddChild(CreateSectionLabel("Access level", muted));
		_permissionsAccessLevel = new OptionButton();
		_permissionsAccessLevel.CustomMinimumSize = new Vector2(220, 30);
		_permissionsAccessLevel.AddItem("Player (0)", 0);
		_permissionsAccessLevel.AddItem("Councilor (40)", 40);
		_permissionsAccessLevel.AddItem("Seer (60)", 60);
		_permissionsAccessLevel.AddItem("Game Master (GM) (80)", 80);
		_permissionsAccessLevel.AddItem("Developer / Owner (100)", 100);
		_permissionsAccessLevel.AddThemeColorOverride("font_color", text);
		_permissionsAccessLevel.ItemSelected += _ => RefreshPermissionsCatalogLabel();
		accessRow.AddChild(_permissionsAccessLevel);

		var editorRow = new HBoxContainer();
		editorRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		editorRow.SizeFlagsVertical = SizeFlags.ExpandFill;
		editorRow.AddThemeConstantOverride("separation", 8);
		column.AddChild(editorRow);

		_permissionsRevokedText = CreatePermissionTextBox("Revoked permissions/commands", panelSoft, text, muted);
		editorRow.AddChild(WrapPermissionTextBox("Revoked permissions/commands", _permissionsRevokedText, panelSoft, text));
		_permissionsSpecialText = CreatePermissionTextBox("Special permissions/commands", panelSoft, text, muted);
		editorRow.AddChild(WrapPermissionTextBox("Special permissions/commands", _permissionsSpecialText, panelSoft, text));

		_permissionsCatalogLabel = new Label();
		_permissionsCatalogLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_permissionsCatalogLabel.AddThemeColorOverride("font_color", muted);
		column.AddChild(_permissionsCatalogLabel);

		var actionRow = new HBoxContainer();
		actionRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		actionRow.AddThemeConstantOverride("separation", 8);
		column.AddChild(actionRow);

		var cancel = CreateActionButton("Cancel", panelSoft, text);
		cancel.Pressed += ClosePermissionsOverlay;
		actionRow.AddChild(cancel);
		var apply = CreateActionButton("Apply", accentSoft, text);
		apply.Pressed += ApplyPermissionsOverlay;
		actionRow.AddChild(apply);
		return _permissionsOverlay;
	}

	private TextEdit CreatePermissionTextBox(string placeholder, Color panelSoft, Color text, Color muted)
	{
		var edit = new TextEdit();
		edit.PlaceholderText = "One permission per line";
		edit.TooltipText = placeholder + "\nUse normalized keys, for example command.spawn or admin.entity.inspect.";
		edit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		edit.SizeFlagsVertical = SizeFlags.ExpandFill;
		edit.CustomMinimumSize = new Vector2(0, 150);
		edit.AddThemeColorOverride("font_color", text);
		edit.AddThemeColorOverride("font_placeholder_color", muted);
		edit.AddThemeStyleboxOverride("normal", CreatePanelStyle(panelSoft, 6));
		edit.AddThemeStyleboxOverride("focus", CreatePanelStyle(panelSoft, 6, _settingsAccentSoftColor));
		return edit;
	}

	private Control WrapPermissionTextBox(string title, TextEdit edit, Color panelSoft, Color text)
	{
		var column = new VBoxContainer();
		column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.SizeFlagsVertical = SizeFlags.ExpandFill;
		column.AddThemeConstantOverride("separation", 4);
		var titleRow = new HBoxContainer();
		titleRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleRow.AddThemeConstantOverride("separation", 4);
		column.AddChild(titleRow);

		Label label = CreateSectionLabel(title, text);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleRow.AddChild(label);

		Button add = CreateActionButton("+", panelSoft, text);
		add.CustomMinimumSize = new Vector2(28, 24);
		add.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		add.TooltipText = "Add permission.";
		add.Pressed += () => OpenPermissionPicker(edit, true, title);
		titleRow.AddChild(add);

		Button remove = CreateActionButton("-", panelSoft, text);
		remove.CustomMinimumSize = new Vector2(28, 24);
		remove.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		remove.TooltipText = "Remove permission.";
		remove.Pressed += () => OpenPermissionPicker(edit, false, title);
		titleRow.AddChild(remove);

		column.AddChild(edit);
		return column;
	}

	private Control BuildPermissionPickerOverlay(Color panel, Color panelSoft, Color accentSoft, Color text, Color muted)
	{
		_permissionPickerOverlay = new PanelContainer();
		_permissionPickerOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
		_permissionPickerOverlay.Visible = false;
		_permissionPickerOverlay.MouseFilter = MouseFilterEnum.Stop;
		_permissionPickerOverlay.AddThemeStyleboxOverride("panel", CreatePanelStyle(new Color(0f, 0f, 0f, 0.55f), 0));

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		_permissionPickerOverlay.AddChild(center);

		var dialog = new PanelContainer();
		dialog.CustomMinimumSize = new Vector2(520, 420);
		dialog.AddThemeStyleboxOverride("panel", CreatePanelStyle(panel, 10, accentSoft));
		center.AddChild(dialog);

		var column = new VBoxContainer();
		column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.SizeFlagsVertical = SizeFlags.ExpandFill;
		column.AddThemeConstantOverride("separation", 8);
		dialog.AddChild(column);

		var titleRow = new HBoxContainer();
		titleRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddChild(titleRow);

		_permissionPickerTitleLabel = CreateSectionLabel("Add commands or permissions", text);
		_permissionPickerTitleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleRow.AddChild(_permissionPickerTitleLabel);

		Button close = CreateActionButton("X", panelSoft, text);
		close.CustomMinimumSize = new Vector2(36, 28);
		close.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		close.TooltipText = "Close permission picker.";
		close.Pressed += ClosePermissionPickerOverlay;
		titleRow.AddChild(close);

		_permissionPickerList = new ItemList();
		_permissionPickerList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_permissionPickerList.SizeFlagsVertical = SizeFlags.ExpandFill;
		_permissionPickerList.SelectMode = ItemList.SelectModeEnum.Multi;
		_permissionPickerList.AddThemeColorOverride("font_color", text);
		column.AddChild(_permissionPickerList);

		var actionRow = new HBoxContainer();
		actionRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		actionRow.AddThemeConstantOverride("separation", 8);
		column.AddChild(actionRow);

		Button cancel = CreateActionButton("Cancel", panelSoft, text);
		cancel.Pressed += ClosePermissionPickerOverlay;
		actionRow.AddChild(cancel);

		Button apply = CreateActionButton("Apply", accentSoft, text);
		apply.Pressed += ApplyPermissionPickerOverlay;
		actionRow.AddChild(apply);
		return _permissionPickerOverlay;
	}

	private void OpenSelectedPlayerPermissions()
	{
		if (!TryFindSelectedPlayer(out ServerPlayerSessionSnapshot player))
		{
			AppendOutput("ACCT", "Select a player before opening permissions.");
			return;
		}

		if (string.IsNullOrWhiteSpace(player.AccountId) ||
			string.Equals(player.AccountId, "local", StringComparison.OrdinalIgnoreCase))
		{
			AppendOutput("ACCT", "Selected row is not bound to a persisted account.");
			return;
		}

		_permissionsAccountId = player.AccountId;
		_permissionsPlayerName = player.PlayerName;
		if (_permissionsTitleLabel is not null)
			_permissionsTitleLabel.Text = $"Permissions: {player.PlayerName} ({AccountNameOrId(player)})";
		if (_permissionsAccessLevel is not null)
			SelectPermissionAccessLevel(player.AccessLevel);
		if (_permissionsRevokedText is not null)
			_permissionsRevokedText.Text = FormatPermissionLines(player.DirectDenies);
		if (_permissionsSpecialText is not null)
			_permissionsSpecialText.Text = FormatPermissionLines(player.DirectGrants);

		RefreshPermissionsCatalogLabel();
		if (_permissionsOverlay is not null)
			_permissionsOverlay.Visible = true;
	}

	private void KickSelectedPlayer()
	{
		if (_controller is null)
		{
			AppendOutput("PLAYER", "Server controller is not available.");
			return;
		}

		if (!TryFindSelectedPlayer(out ServerPlayerSessionSnapshot player))
		{
			AppendOutput("PLAYER", "Select a player before kicking.");
			return;
		}

		ServerPlayerSessionActionResult result = _controller.KickPlayerSession(player.SessionId);
		AppendOutput(result.Succeeded ? "PLAYER" : "WARN", result.Message);
		_playersListSignature = string.Empty;
		_accountsListSignature = string.Empty;
		RefreshStatus();
	}

	private void BanSelectedPlayerAccount()
	{
		if (!TryFindSelectedPlayer(out ServerPlayerSessionSnapshot player))
		{
			AppendOutput("ACCT", "Select a player before banning their account.");
			return;
		}

		if (string.IsNullOrWhiteSpace(player.AccountId) ||
			string.Equals(player.AccountId, "local", StringComparison.OrdinalIgnoreCase))
		{
			AppendOutput("ACCT", "Selected player is not bound to a persisted account.");
			return;
		}

		BanAccount(player.AccountId);
	}

	private void PromoteSelectedPlayerAccount()
	{
		if (!TryFindSelectedPlayer(out ServerPlayerSessionSnapshot player))
		{
			AppendOutput("ACCT", "Select a player before promoting their account.");
			return;
		}

		SetAccountAccessLevel(player.AccountId, NextAccessLevel(player.AccessLevel));
	}

	private void DemoteSelectedPlayerAccount()
	{
		if (!TryFindSelectedPlayer(out ServerPlayerSessionSnapshot player))
		{
			AppendOutput("ACCT", "Select a player before demoting their account.");
			return;
		}

		SetAccountAccessLevel(player.AccountId, PreviousAccessLevel(player.AccessLevel));
	}

	private void ToggleSelectedAccountBan()
	{
		if (!TryFindSelectedAccount(out ServerAccountSummarySnapshot account))
		{
			AppendOutput("ACCT", "Select an account before changing ban status.");
			return;
		}

		if (account.IsBanned)
			UnbanAccount(account.AccountId);
		else
			BanAccount(account.AccountId);
	}

	private void PromoteSelectedAccount()
	{
		if (!TryFindSelectedAccount(out ServerAccountSummarySnapshot account))
		{
			AppendOutput("ACCT", "Select an account before promoting.");
			return;
		}

		SetAccountAccessLevel(account.AccountId, NextAccessLevel(account.AccessLevel));
	}

	private void DemoteSelectedAccount()
	{
		if (!TryFindSelectedAccount(out ServerAccountSummarySnapshot account))
		{
			AppendOutput("ACCT", "Select an account before demoting.");
			return;
		}

		SetAccountAccessLevel(account.AccountId, PreviousAccessLevel(account.AccessLevel));
	}

	private void BanAccount(string accountId)
	{
		if (_controller is null)
		{
			AppendOutput("ACCT", "Server controller is not available.");
			return;
		}

		ServerAccountActionResult result = _controller.BanAccount(accountId);
		AppendOutput(result.Succeeded ? "ACCT" : "WARN", result.Message);
		_playersListSignature = string.Empty;
		_accountsListSignature = string.Empty;
		RefreshStatus();
	}

	private void UnbanAccount(string accountId)
	{
		if (_controller is null)
		{
			AppendOutput("ACCT", "Server controller is not available.");
			return;
		}

		ServerAccountActionResult result = _controller.UnbanAccount(accountId);
		AppendOutput(result.Succeeded ? "ACCT" : "WARN", result.Message);
		_playersListSignature = string.Empty;
		_accountsListSignature = string.Empty;
		RefreshStatus();
	}

	private void SetAccountAccessLevel(string accountId, int accessLevel)
	{
		if (_controller is null)
		{
			AppendOutput("ACCT", "Server controller is not available.");
			return;
		}

		if (string.IsNullOrWhiteSpace(accountId) ||
			string.Equals(accountId, "local", StringComparison.OrdinalIgnoreCase))
		{
			AppendOutput("ACCT", "Selected row is not bound to a persisted account.");
			return;
		}

		ServerAccountAccessUpdateResult result = _controller.SetAccountAccessLevel(accountId, accessLevel);
		AppendOutput(result.Succeeded ? "ACCT" : "WARN", result.Message);
		_playersListSignature = string.Empty;
		_accountsListSignature = string.Empty;
		RefreshStatus();
	}

	private void OpenSelectedAccountPermissions()
	{
		if (!TryFindSelectedAccount(out ServerAccountSummarySnapshot account))
		{
			AppendOutput("ACCT", "Select an account before opening permissions.");
			return;
		}

		if (string.IsNullOrWhiteSpace(account.AccountId))
		{
			AppendOutput("ACCT", "Selected account row is missing an account id.");
			return;
		}

		_permissionsAccountId = account.AccountId;
		_permissionsPlayerName = account.DisplayName;
		if (_permissionsTitleLabel is not null)
			_permissionsTitleLabel.Text = $"Permissions: {account.DisplayName} (account {account.AccountId})";
		if (_permissionsAccessLevel is not null)
			SelectPermissionAccessLevel(account.AccessLevel);
		if (_permissionsRevokedText is not null)
			_permissionsRevokedText.Text = FormatPermissionLines(account.DirectDenies);
		if (_permissionsSpecialText is not null)
			_permissionsSpecialText.Text = FormatPermissionLines(account.DirectGrants);

		RefreshPermissionsCatalogLabel();
		if (_permissionsOverlay is not null)
			_permissionsOverlay.Visible = true;
	}

	private void ClosePermissionsOverlay()
	{
		ClosePermissionPickerOverlay();
		if (_permissionsOverlay is not null)
			_permissionsOverlay.Visible = false;
		_permissionsAccountId = string.Empty;
		_permissionsPlayerName = string.Empty;
	}

	private void OpenPermissionPicker(TextEdit target, bool addMode, string categoryTitle)
	{
		_permissionPickerTarget = target;
		_permissionPickerAddMode = addMode;
		if (_permissionPickerTitleLabel is not null)
			_permissionPickerTitleLabel.Text = addMode
				? $"Add commands or permissions: {categoryTitle}"
				: $"Remove commands or permissions: {categoryTitle}";
		PopulatePermissionPickerList(target, addMode);
		if (_permissionPickerOverlay is not null)
			_permissionPickerOverlay.Visible = true;
	}

	private void ClosePermissionPickerOverlay()
	{
		if (_permissionPickerOverlay is not null)
			_permissionPickerOverlay.Visible = false;
		_permissionPickerTarget = null;
		_permissionPickerAddMode = false;
	}

	private void PopulatePermissionPickerList(TextEdit target, bool addMode)
	{
		if (_permissionPickerList is null)
			return;

		_permissionPickerList.Clear();
		if (addMode)
		{
			for (int i = 0; i < PermissionCatalog.Length; i++)
			{
				PermissionCatalogEntry entry = PermissionCatalog[i];
				int item = _permissionPickerList.AddItem(entry.Key);
				_permissionPickerList.SetItemTooltip(item, $"{entry.Description}\nAccess level: {AccessLevelName(entry.AccessLevel)}");
			}
			return;
		}

		IReadOnlyList<string> values = ParsePermissionLines(target.Text);
		for (int i = 0; i < values.Count; i++)
		{
			int item = _permissionPickerList.AddItem(values[i]);
			_permissionPickerList.SetItemTooltip(item, "Remove this direct permission override from the selected category.");
		}
	}

	private void ApplyPermissionPickerOverlay()
	{
		if (_permissionPickerTarget is null || _permissionPickerList is null)
		{
			ClosePermissionPickerOverlay();
			return;
		}

		int[] selected = _permissionPickerList.GetSelectedItems();
		if (selected.Length == 0)
		{
			ClosePermissionPickerOverlay();
			return;
		}

		var values = new SortedSet<string>(ParsePermissionLines(_permissionPickerTarget.Text), StringComparer.Ordinal);
		for (int i = 0; i < selected.Length; i++)
		{
			string key = ServerAccessKey.NormalizePermissionKey(_permissionPickerList.GetItemText(selected[i]));
			if (_permissionPickerAddMode)
				values.Add(key);
			else
				values.Remove(key);
		}

		_permissionPickerTarget.Text = FormatPermissionLines(values);
		ClosePermissionPickerOverlay();
	}

	private void ApplyPermissionsOverlay()
	{
		if (_controller is null)
		{
			AppendOutput("ACCT", "Server controller is not available.");
			return;
		}

		if (string.IsNullOrWhiteSpace(_permissionsAccountId))
		{
			AppendOutput("ACCT", "No account is selected for permission update.");
			return;
		}

		try
		{
			int accessLevel = ReadSelectedPermissionAccessLevel();
			IReadOnlyList<string> directDenies = ParsePermissionLines(_permissionsRevokedText?.Text);
			IReadOnlyList<string> directGrants = ParsePermissionLines(_permissionsSpecialText?.Text);
			ServerAccountAccessUpdateResult result = _controller.ReplaceAccountAccess(
				_permissionsAccountId,
				accessLevel,
				directGrants,
				directDenies);

			AppendOutput(result.Succeeded ? "ACCT" : "WARN", result.Message);
			if (!result.Succeeded)
				return;

			_playersListSignature = string.Empty;
			_accountsListSignature = string.Empty;
			ClosePermissionsOverlay();
			RefreshStatus();
		}
		catch (ArgumentException ex)
		{
			AppendOutput("WARN", $"Permission update rejected: {ex.Message}");
		}
	}

	private void SelectPermissionAccessLevel(int accessLevel)
	{
		if (_permissionsAccessLevel is null)
			return;

		int bestIndex = 0;
		int bestDelta = int.MaxValue;
		for (int i = 0; i < _permissionsAccessLevel.ItemCount; i++)
		{
			int itemLevel = (int)_permissionsAccessLevel.GetItemId(i);
			int delta = Math.Abs(itemLevel - accessLevel);
			if (delta < bestDelta)
			{
				bestDelta = delta;
				bestIndex = i;
			}
		}

		_permissionsAccessLevel.Select(bestIndex);
	}

	private int ReadSelectedPermissionAccessLevel()
	{
		if (_permissionsAccessLevel is null || _permissionsAccessLevel.Selected < 0)
			return 0;

		return Math.Max(0, (int)_permissionsAccessLevel.GetSelectedId());
	}

	private void RefreshPermissionsCatalogLabel()
	{
		if (_permissionsCatalogLabel is null)
			return;

		int accessLevel = ReadSelectedPermissionAccessLevel();
		_permissionsCatalogLabel.Text = BuildPermissionsCatalogText(accessLevel);
	}

	private static string BuildPermissionsCatalogText(int accessLevel)
	{
		var builder = new StringBuilder();
		builder.Append("Visible permission groups for this access level: player permissions/commands");
		if (accessLevel >= 40)
			builder.Append(", councilor permissions/commands");
		if (accessLevel >= 60)
			builder.Append(", seer permissions/commands");
		if (accessLevel >= 80)
			builder.Append(", game master (GM) permissions/commands");
		if (accessLevel >= 100)
			builder.Append(", developer/owner permissions/commands");
		builder.Append(". Direct revoked/special entries override the access-level defaults for this account.");
		return builder.ToString();
	}

	private static string AccessLevelName(int accessLevel)
	{
		return accessLevel switch
		{
			>= 100 => "Developer / Owner",
			>= 80 => "Game Master (GM)",
			>= 60 => "Seer",
			>= 40 => "Councilor",
			_ => "Player"
		};
	}

	private static int NextAccessLevel(int accessLevel)
	{
		return accessLevel switch
		{
			< 40 => 40,
			< 60 => 60,
			< 80 => 80,
			< 100 => 100,
			_ => 100
		};
	}

	private static int PreviousAccessLevel(int accessLevel)
	{
		return accessLevel switch
		{
			> 80 => 80,
			> 60 => 60,
			> 40 => 40,
			_ => 0
		};
	}

	private static bool CanEditAccountAccess(string? accountId)
	{
		return !string.IsNullOrWhiteSpace(accountId) &&
			!string.Equals(accountId, "local", StringComparison.OrdinalIgnoreCase);
	}

	private static IReadOnlyList<string> ParsePermissionLines(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return Array.Empty<string>();

		var values = new List<string>();
		string normalizedNewlines = text.Replace('\r', '\n');
		string[] lines = normalizedNewlines.Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (line.Length == 0)
				continue;

			string key = ServerAccessKey.NormalizePermissionKey(line);
			bool exists = false;
			for (int j = 0; j < values.Count; j++)
			{
				if (string.Equals(values[j], key, StringComparison.Ordinal))
				{
					exists = true;
					break;
				}
			}

			if (!exists)
				values.Add(key);
		}

		values.Sort(StringComparer.Ordinal);
		return values;
	}

	private static string FormatPermissionLines(IEnumerable<string> values)
	{
		var builder = new StringBuilder();
		bool hasValue = false;
		foreach (string value in values)
		{
			if (hasValue)
				builder.Append('\n');
			builder.Append(value);
			hasValue = true;
		}

		return builder.ToString();
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

	private Control BuildDiscoveryTab(Color panel, Color panelSoft, Color accentSoft, Color text, Color muted)
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

		column.AddChild(CreateSectionLabel("Discovery", text));

		_discoverySummaryValue = new Label();
		_discoverySummaryValue.AddThemeColorOverride("font_color", muted);
		_discoverySummaryValue.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		column.AddChild(_discoverySummaryValue);

		var detailsFrame = new PanelContainer();
		detailsFrame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		detailsFrame.SizeFlagsVertical = SizeFlags.ExpandFill;
		detailsFrame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panelSoft, 10, accentSoft));
		column.AddChild(detailsFrame);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		detailsFrame.AddChild(margin);

		var rows = new VBoxContainer();
		rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rows.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		rows.AddThemeConstantOverride("separation", 8);
		margin.AddChild(rows);

		_discoveryEnabledValue = CreateDiscoveryValueLabel(muted);
		_discoveryRegistryValue = CreateDiscoveryValueLabel(muted);
		_discoveryPublishModeValue = CreateDiscoveryValueLabel(muted);
		_discoveryServerIdValue = CreateDiscoveryValueLabel(muted);
		_discoveryAdvertisedValue = CreateDiscoveryValueLabel(muted);
		_discoveryStatusEndpointValue = CreateDiscoveryValueLabel(muted);
		_discoveryPublisherValue = CreateDiscoveryValueLabel(muted);
		_discoveryLastPublishValue = CreateDiscoveryValueLabel(muted);
		_discoveryLastMessageValue = CreateDiscoveryValueLabel(muted);

		rows.AddChild(CreateKeyValueRow("Enabled", _discoveryEnabledValue, text));
		rows.AddChild(CreateKeyValueRow("Registry URL", _discoveryRegistryValue, text));
		rows.AddChild(CreateKeyValueRow("Publish mode", _discoveryPublishModeValue, text));
		rows.AddChild(CreateKeyValueRow("Server ID", _discoveryServerIdValue, text));
		rows.AddChild(CreateKeyValueRow("Advertised", _discoveryAdvertisedValue, text));
		rows.AddChild(CreateKeyValueRow("Status endpoint", _discoveryStatusEndpointValue, text));
		rows.AddChild(CreateKeyValueRow("Publisher", _discoveryPublisherValue, text));
		rows.AddChild(CreateKeyValueRow("Last publish", _discoveryLastPublishValue, text));
		rows.AddChild(CreateKeyValueRow("Last message", _discoveryLastMessageValue, text));

		RefreshDiscoveryStatus(null);
		return frame;
	}

	private Control BuildSettingsTab(Color panel, Color panelSoft, Color accentSoft, Color text, Color muted)
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

		var titleRow = new HBoxContainer();
		titleRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleRow.AddThemeConstantOverride("separation", 4);
		column.AddChild(titleRow);

		titleRow.AddChild(CreateSectionLabel("Settings:", text));
		_settingsTitlePathLabel = CreateSectionLabel(string.Empty, accentSoft.Lightened(0.15f));
		_settingsTitlePathLabel.ClipText = true;
		_settingsTitlePathLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleRow.AddChild(_settingsTitlePathLabel);

		var settingsFrame = new PanelContainer();
		settingsFrame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		settingsFrame.SizeFlagsVertical = SizeFlags.ExpandFill;
		settingsFrame.AddThemeStyleboxOverride("panel", CreatePanelStyle(panelSoft, 10, accentSoft));
		column.AddChild(settingsFrame);

		_settingsPanelSoftColor = panelSoft;
		_settingsAccentSoftColor = accentSoft;
		_settingsTextColor = text;
		_settingsMutedColor = muted;

		var settingsColumn = new VBoxContainer();
		settingsColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		settingsColumn.SizeFlagsVertical = SizeFlags.ExpandFill;
		settingsColumn.AddThemeConstantOverride("separation", 8);
		settingsFrame.AddChild(settingsColumn);

		var scroll = new ScrollContainer();
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		settingsColumn.AddChild(scroll);

		_settingsList = new VBoxContainer();
		_settingsList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_settingsList.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		_settingsList.AddThemeConstantOverride("separation", 8);
		scroll.AddChild(_settingsList);

		var actionSpacer = new Control();
		actionSpacer.CustomMinimumSize = new Vector2(0, 6);
		settingsColumn.AddChild(actionSpacer);

		var actionRow = new HBoxContainer();
		actionRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		actionRow.AddThemeConstantOverride("separation", 8);
		settingsColumn.AddChild(actionRow);

		_cancelSettingsButton = CreateActionButton("Cancel", panelSoft, text);
		_cancelSettingsButton.Pressed += RefreshSettingsTab;
		actionRow.AddChild(_cancelSettingsButton);

		_applySettingsButton = CreateActionButton("Apply", panelSoft.Darkened(0.08f), muted);
		_applySettingsButton.Pressed += HandleApplySettingsPressed;
		_applySettingsButton.Disabled = true;
		actionRow.AddChild(_applySettingsButton);

		_settingsNote = new Label();
		_settingsNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_settingsNote.AddThemeColorOverride("font_color", accentSoft.Lightened(0.15f));
		settingsColumn.AddChild(_settingsNote);
		RefreshSettingsTab();

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

	private Button CreatePlayerSortButton(string label, PlayerSortColumn column, Color fill, Color text)
	{
		var button = CreateActionButton(label, fill, text);
		button.CustomMinimumSize = new Vector2(0, 26);
		button.TooltipText = $"Sort players by {label}.";
		button.Pressed += () =>
		{
			if (_playerSortColumn == column)
				_playerSortAscending = !_playerSortAscending;
			else
			{
				_playerSortColumn = column;
				_playerSortAscending = true;
			}

			_playersListSignature = string.Empty;
			RefreshPlayersList(_currentPlayers);
		};
		return button;
	}

	private Button CreateAccountSortButton(string label, AccountSortColumn column, Color fill, Color text)
	{
		var button = CreateActionButton(label, fill, text);
		button.CustomMinimumSize = new Vector2(0, 26);
		button.TooltipText = $"Sort accounts by {label}.";
		button.Pressed += () =>
		{
			if (_accountSortColumn == column)
				_accountSortAscending = !_accountSortAscending;
			else
			{
				_accountSortColumn = column;
				_accountSortAscending = true;
			}

			_accountsListSignature = string.Empty;
			RefreshAccountsList(_currentAccounts);
		};
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

	private static Label CreateDiscoveryValueLabel(Color color)
	{
		var label = CreateValueLabel(color);
		label.ClipText = true;
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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
			RefreshDiscoveryStatus(null);

			if (_playersSummaryValue is not null)
				_playersSummaryValue.Text = "No active players.";

			if (_accountsSummaryValue is not null)
				_accountsSummaryValue.Text = "No accounts loaded.";

			if (_performanceSummaryValue is not null)
				_performanceSummaryValue.Text = "Waiting for Start.";

			if (_performanceGrid is not null)
				SetPerformancePlaceholder(new Color(0.90f, 0.92f, 0.95f), new Color(0.62f, 0.67f, 0.74f));

			RefreshPlayersList(Array.Empty<ServerPlayerSessionSnapshot>());
			RefreshAccountsList(Array.Empty<ServerAccountSummarySnapshot>());
			RefreshControls(ServerApplicationState.Created);
			return;
		}

		ServerStatusSnapshot status = _controller.GetStatusSnapshot();
		ServerMetricsSnapshot metrics = _controller.GetMetricsSnapshot();
		IReadOnlyList<ServerSystemRuntimeSnapshot> systems = _controller.GetSystemRuntimeSnapshots();
		IReadOnlyList<ServerPlayerSessionSnapshot> players = _controller.GetPlayerSessionsSnapshot();
		IReadOnlyList<ServerAccountSummarySnapshot> accounts = _controller.GetAccountSummariesSnapshot();

		UpdateMetricBanner(status, metrics, players, accounts);
		RefreshDiscoveryStatus(_controller.GetDiscoverySnapshot());

		if (_playersSummaryValue is not null)
			_playersSummaryValue.Text = players.Count == 0
				? $"{status.ActiveSessions} online now, peak {status.PeakSessions}."
				: $"{players.Count} player rows available for operator view.";

		if (_accountsSummaryValue is not null)
			_accountsSummaryValue.Text = accounts.Count == 0
				? "Account directory actions are placeholders until account storage is wired."
				: $"{accounts.Count} account rows available for operator view.";

		RefreshPlayersList(players);
		RefreshAccountsList(accounts);
		RefreshPerformance(metrics, systems);
		RefreshControls(status.State);
	}

	private void RefreshDiscoveryStatus(GodotServerDiscoverySnapshot? snapshot)
	{
		GodotServerShellOptions options = _options ?? GodotServerShellOptions.Default;
		if (snapshot is null)
		{
			if (_discoverySummaryValue is not null)
				_discoverySummaryValue.Text = options.DiscoveryEnabled
					? "Discovery is configured but the server runtime has not been created."
					: "Discovery is disabled.";

			SetDiscoveryLabel(_discoveryEnabledValue, FormatBool(options.DiscoveryEnabled));
			SetDiscoveryLabel(_discoveryRegistryValue, options.DiscoveryRegistryUrl);
			SetDiscoveryLabel(_discoveryPublishModeValue, options.DiscoveryPublishMode);
			SetDiscoveryLabel(_discoveryServerIdValue, string.IsNullOrWhiteSpace(options.DiscoveryServerId) ? options.ProfileName : options.DiscoveryServerId);
			SetDiscoveryLabel(_discoveryAdvertisedValue, $"{options.DiscoveryAdvertisedHost}:{options.NetworkPort}");
			SetDiscoveryLabel(_discoveryStatusEndpointValue, string.IsNullOrWhiteSpace(options.DiscoveryStatusEndpointUrl) ? "off" : options.DiscoveryStatusEndpointUrl);
			SetDiscoveryLabel(_discoveryPublisherValue, "not created");
			SetDiscoveryLabel(_discoveryLastPublishValue, "never");
			SetDiscoveryLabel(_discoveryLastMessageValue, "Runtime not created.");
			return;
		}

		if (_discoverySummaryValue is not null)
		{
			_discoverySummaryValue.Text = snapshot.Enabled
				? $"{(snapshot.HasPublished ? "Published" : "Not published")} to {snapshot.RegistryUrl}."
				: "Discovery is disabled.";
		}

		SetDiscoveryLabel(_discoveryEnabledValue, FormatBool(snapshot.Enabled));
		SetDiscoveryLabel(_discoveryRegistryValue, snapshot.RegistryUrl);
		SetDiscoveryLabel(_discoveryPublishModeValue, snapshot.PublishMode);
		SetDiscoveryLabel(_discoveryServerIdValue, snapshot.ServerId);
		SetDiscoveryLabel(_discoveryAdvertisedValue, $"{snapshot.AdvertisedHost}:{snapshot.AdvertisedPort}");
		SetDiscoveryLabel(
			_discoveryStatusEndpointValue,
			string.IsNullOrWhiteSpace(snapshot.StatusEndpointUrl)
				? "off"
				: $"{snapshot.StatusEndpointUrl} ({(snapshot.StatusEndpointRunning ? "listening" : "not listening")})");
		SetDiscoveryLabel(
			_discoveryPublisherValue,
			snapshot.PublisherCreated
				? snapshot.HasPublished ? "published" : "waiting"
				: snapshot.Enabled ? "not created" : "disabled");
		SetDiscoveryLabel(
			_discoveryLastPublishValue,
			snapshot.LastPublishUtc.HasValue
				? snapshot.LastPublishUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
				: "never");
		SetDiscoveryLabel(_discoveryLastMessageValue, snapshot.LastMessage);
	}

	private static void SetDiscoveryLabel(Label? label, string value)
	{
		if (label is null)
			return;

		label.Text = string.IsNullOrWhiteSpace(value) ? "-" : value;
		label.TooltipText = label.Text;
	}

	private void RefreshPlayersList(IReadOnlyList<ServerPlayerSessionSnapshot> players)
	{
		_currentPlayers = SortPlayers(players);
		if (_playersList is null)
			return;

		IReadOnlyList<ServerPlayerSessionSnapshot> sortedPlayers = _currentPlayers;
		string signature = BuildPlayersListSignature(sortedPlayers);
		bool listChanged = !string.Equals(signature, _playersListSignature, StringComparison.Ordinal);
		if (listChanged)
		{
			_playersList.Clear();
			for (int i = 0; i < sortedPlayers.Count; i++)
				_playersList.AddItem(FormatPlayerRow(sortedPlayers[i]));
			_playersListSignature = signature;
		}

		int selectedIndex = -1;
		for (int i = 0; i < sortedPlayers.Count; i++)
		{
			ServerPlayerSessionSnapshot player = sortedPlayers[i];
			if (!string.IsNullOrWhiteSpace(_selectedPlayerSessionId) &&
				string.Equals(player.SessionId, _selectedPlayerSessionId, StringComparison.Ordinal))
			{
				selectedIndex = i;
			}
		}

		if (selectedIndex >= 0)
			_playersList.Select(selectedIndex);
		else
			_selectedPlayerSessionId = null;

		RefreshPlayerSelection();
	}

	private void RefreshAccountsList(IReadOnlyList<ServerAccountSummarySnapshot> accounts)
	{
		_currentAccounts = SortAccounts(accounts);
		if (_accountsList is null)
			return;

		IReadOnlyList<ServerAccountSummarySnapshot> sortedAccounts = _currentAccounts;
		string signature = BuildAccountsListSignature(sortedAccounts);
		bool listChanged = !string.Equals(signature, _accountsListSignature, StringComparison.Ordinal);
		if (listChanged)
		{
			_accountsList.Clear();
			for (int i = 0; i < sortedAccounts.Count; i++)
				_accountsList.AddItem(FormatAccountRow(sortedAccounts[i]));
			_accountsListSignature = signature;
		}

		int selectedIndex = -1;
		for (int i = 0; i < sortedAccounts.Count; i++)
		{
			ServerAccountSummarySnapshot account = sortedAccounts[i];
			if (!string.IsNullOrWhiteSpace(_selectedAccountId) &&
				string.Equals(account.AccountId, _selectedAccountId, StringComparison.Ordinal))
			{
				selectedIndex = i;
			}
		}

		if (selectedIndex >= 0)
			_accountsList.Select(selectedIndex);
		else
			_selectedAccountId = null;

		RefreshAccountSelection();
	}

	private IReadOnlyList<ServerAccountSummarySnapshot> SortAccounts(IReadOnlyList<ServerAccountSummarySnapshot> accounts)
	{
		if (accounts.Count <= 1)
			return accounts;

		var sorted = new ServerAccountSummarySnapshot[accounts.Count];
		for (int i = 0; i < accounts.Count; i++)
			sorted[i] = accounts[i];

		Array.Sort(sorted, CompareAccountRows);
		return sorted;
	}

	private int CompareAccountRows(ServerAccountSummarySnapshot left, ServerAccountSummarySnapshot right)
	{
		int result = _accountSortColumn switch
		{
			AccountSortColumn.AccountId => CompareText(left.AccountId, right.AccountId),
			AccountSortColumn.OnlineState => left.IsOnline.CompareTo(right.IsOnline),
			AccountSortColumn.BanState => left.IsBanned.CompareTo(right.IsBanned),
			AccountSortColumn.AccessLevel => left.AccessLevel.CompareTo(right.AccessLevel),
			AccountSortColumn.LastLogin => Nullable.Compare(left.LastLoginUtc, right.LastLoginUtc),
			AccountSortColumn.TotalPlayTime => left.TotalPlayTime.CompareTo(right.TotalPlayTime),
			AccountSortColumn.RemoteAddress => CompareText(FirstKnownRemote(left), FirstKnownRemote(right)),
			_ => CompareText(left.DisplayName, right.DisplayName)
		};

		return _accountSortAscending ? result : -result;
	}

	private IReadOnlyList<ServerPlayerSessionSnapshot> SortPlayers(IReadOnlyList<ServerPlayerSessionSnapshot> players)
	{
		if (players.Count <= 1)
			return players;

		var sorted = new ServerPlayerSessionSnapshot[players.Count];
		for (int i = 0; i < players.Count; i++)
			sorted[i] = players[i];

		Array.Sort(sorted, ComparePlayerRows);
		return sorted;
	}

	private int ComparePlayerRows(ServerPlayerSessionSnapshot left, ServerPlayerSessionSnapshot right)
	{
		int result = _playerSortColumn switch
		{
			PlayerSortColumn.RemoteAddress => CompareText(left.RemoteAddress, right.RemoteAddress),
			PlayerSortColumn.SessionId => CompareText(left.SessionId, right.SessionId),
			PlayerSortColumn.AccountName => CompareText(AccountNameOrId(left), AccountNameOrId(right)),
			PlayerSortColumn.State => CompareText(OnlineStateText(left), OnlineStateText(right)),
			PlayerSortColumn.CurrentUptime => left.CurrentUptime.CompareTo(right.CurrentUptime),
			PlayerSortColumn.TotalPlayTime => left.TotalPlayTime.CompareTo(right.TotalPlayTime),
			PlayerSortColumn.AccessLevel => left.AccessLevel.CompareTo(right.AccessLevel),
			_ => CompareText(left.PlayerName, right.PlayerName)
		};

		return _playerSortAscending ? result : -result;
	}

	private void RefreshPlayerSelection()
	{
		if (_playersSelectedValue is null)
			return;

		if (TryFindSelectedPlayer(out ServerPlayerSessionSnapshot player))
		{
			if (_playerKickButton is not null)
				_playerKickButton.Disabled = !player.IsOnline || string.IsNullOrWhiteSpace(player.SessionId);
			if (_playerBanButton is not null)
				_playerBanButton.Disabled = string.IsNullOrWhiteSpace(player.AccountId) || string.Equals(player.AccountId, "local", StringComparison.OrdinalIgnoreCase);
			if (_playerPromoteButton is not null)
				_playerPromoteButton.Disabled = !CanEditAccountAccess(player.AccountId) || player.AccessLevel >= 100;
			if (_playerDemoteButton is not null)
				_playerDemoteButton.Disabled = !CanEditAccountAccess(player.AccountId) || player.AccessLevel <= 0;
			if (_playerPermissionsButton is not null)
				_playerPermissionsButton.Disabled = string.IsNullOrWhiteSpace(player.AccountId) || string.Equals(player.AccountId, "local", StringComparison.OrdinalIgnoreCase);
			_playersSelectedValue.Text = string.IsNullOrWhiteSpace(player.Detail)
				? $"Selected: {player.PlayerName}  session={player.SessionId}  account={AccountNameOrId(player)}  access={player.AccessLevel}"
				: $"Selected: {player.PlayerName}  session={player.SessionId}  account={AccountNameOrId(player)}  access={player.AccessLevel}  {player.Detail}";
			return;
		}

		if (_playerKickButton is not null)
			_playerKickButton.Disabled = true;
		if (_playerBanButton is not null)
			_playerBanButton.Disabled = true;
		if (_playerPromoteButton is not null)
			_playerPromoteButton.Disabled = true;
		if (_playerDemoteButton is not null)
			_playerDemoteButton.Disabled = true;
		if (_playerPermissionsButton is not null)
			_playerPermissionsButton.Disabled = true;
		_playersSelectedValue.Text = "Select a player row to enable player actions.";
	}

	private void RefreshAccountSelection()
	{
		if (_accountsSelectedValue is null)
			return;

		if (TryFindSelectedAccount(out ServerAccountSummarySnapshot account))
		{
			string flags = $"{(account.IsOnline ? "online" : "offline")}, {(account.IsBanned ? "banned" : "clear")}";
			if (_accountBanButton is not null)
			{
				_accountBanButton.Text = account.IsBanned ? "Unban" : "Ban";
				_accountBanButton.Disabled = string.IsNullOrWhiteSpace(account.AccountId);
			}
			if (_accountPromoteButton is not null)
				_accountPromoteButton.Disabled = !CanEditAccountAccess(account.AccountId) || account.AccessLevel >= 100;
			if (_accountDemoteButton is not null)
				_accountDemoteButton.Disabled = !CanEditAccountAccess(account.AccountId) || account.AccessLevel <= 0;
			if (_accountPermissionsButton is not null)
				_accountPermissionsButton.Disabled = string.IsNullOrWhiteSpace(account.AccountId);
			_accountsSelectedValue.Text = string.IsNullOrWhiteSpace(account.Detail)
				? $"Selected: {account.DisplayName}  account={account.AccountId}  access={account.AccessLevel}  {flags}"
				: $"Selected: {account.DisplayName}  account={account.AccountId}  access={account.AccessLevel}  {flags}  {account.Detail}";
			return;
		}

		if (_accountBanButton is not null)
		{
			_accountBanButton.Text = "Ban";
			_accountBanButton.Disabled = true;
		}
		if (_accountPromoteButton is not null)
			_accountPromoteButton.Disabled = true;
		if (_accountDemoteButton is not null)
			_accountDemoteButton.Disabled = true;
		if (_accountPermissionsButton is not null)
			_accountPermissionsButton.Disabled = true;
		_accountsSelectedValue.Text = "Select an account row to enable account actions.";
	}

	private bool TryFindSelectedPlayer(out ServerPlayerSessionSnapshot player)
	{
		for (int i = 0; i < _currentPlayers.Count; i++)
		{
			if (string.Equals(_currentPlayers[i].SessionId, _selectedPlayerSessionId, StringComparison.Ordinal))
			{
				player = _currentPlayers[i];
				return true;
			}
		}

		player = default;
		return false;
	}

	private bool TryFindSelectedAccount(out ServerAccountSummarySnapshot account)
	{
		for (int i = 0; i < _currentAccounts.Count; i++)
		{
			if (string.Equals(_currentAccounts[i].AccountId, _selectedAccountId, StringComparison.Ordinal))
			{
				account = _currentAccounts[i];
				return true;
			}
		}

		account = default;
		return false;
	}

	private static string FormatPlayerRow(ServerPlayerSessionSnapshot player)
	{
		return $"{TrimForRow(player.PlayerName, 18),-18}  " +
			$"{TrimForRow(player.RemoteAddress, 18),-18}  " +
			$"{TrimForRow(player.SessionId, 10),-10}  " +
			$"{TrimForRow(AccountNameOrId(player), 16),-16}  " +
			$"{TrimForRow(OnlineStateText(player), 9),-9}  " +
			$"{FormatDuration(player.CurrentUptime),9}  " +
			$"{FormatDuration(player.TotalPlayTime),9}  " +
			$"{player.AccessLevel,4}";
	}

	private static string FormatAccountRow(ServerAccountSummarySnapshot account)
	{
		return $"{TrimForRow(account.DisplayName, 18),-18}  " +
			$"{TrimForRow(account.AccountId, 10),-10}  " +
			$"{TrimForRow(account.IsOnline ? "Online" : "Offline", 9),-9}  " +
			$"{TrimForRow(account.IsBanned ? "Banned" : "Clear", 8),-8}  " +
			$"{account.AccessLevel,4}  " +
			$"{TrimForRow(FormatDate(account.LastLoginUtc), 12),-12}  " +
			$"{FormatDuration(account.TotalPlayTime),9}  " +
			$"{TrimForRow(FirstKnownRemote(account), 18),-18}";
	}

	private static string BuildPlayersListSignature(IReadOnlyList<ServerPlayerSessionSnapshot> players)
	{
		var builder = new StringBuilder(players.Count * 96);
		builder.Append(players.Count);
		for (int i = 0; i < players.Count; i++)
		{
			ServerPlayerSessionSnapshot player = players[i];
			builder.Append('|')
				.Append(player.SessionId)
				.Append('\u001f')
				.Append(player.PlayerName)
				.Append('\u001f')
				.Append(player.AccountId)
				.Append('\u001f')
				.Append(player.AccountName)
				.Append('\u001f')
				.Append(player.RemoteAddress)
				.Append('\u001f')
				.Append(player.IsOnline ? '1' : '0')
				.Append('\u001f')
				.Append(player.SessionState)
				.Append('\u001f')
				.Append(player.CurrentUptime.Ticks)
				.Append('\u001f')
				.Append(player.TotalPlayTime.Ticks)
				.Append('\u001f')
				.Append(player.AccessLevel)
				.Append('\u001f')
				.Append(JoinKeys(player.DirectGrants))
				.Append('\u001f')
				.Append(JoinKeys(player.DirectDenies))
				.Append('\u001f')
				.Append(player.Detail);
		}

		return builder.ToString();
	}

	private static string BuildAccountsListSignature(IReadOnlyList<ServerAccountSummarySnapshot> accounts)
	{
		var builder = new StringBuilder(accounts.Count * 80);
		builder.Append(accounts.Count);
		for (int i = 0; i < accounts.Count; i++)
		{
			ServerAccountSummarySnapshot account = accounts[i];
			builder.Append('|')
				.Append(account.AccountId)
				.Append('\u001f')
				.Append(account.DisplayName)
				.Append('\u001f')
				.Append(account.IsOnline ? '1' : '0')
				.Append('\u001f')
				.Append(account.IsBanned ? '1' : '0')
				.Append('\u001f')
				.Append(account.AccessLevel)
				.Append('\u001f')
				.Append(account.LastLoginUtc?.ToUnixTimeSeconds() ?? 0L)
				.Append('\u001f')
				.Append(account.TotalPlayTime.Ticks)
				.Append('\u001f')
				.Append(JoinKeys(account.DirectGrants))
				.Append('\u001f')
				.Append(JoinKeys(account.DirectDenies))
				.Append('\u001f')
				.Append(JoinKeys(account.KnownRemotes))
				.Append('\u001f')
				.Append(account.Detail);
		}

		return builder.ToString();
	}

	private static string TrimForRow(string? value, int maxLength)
	{
		string text = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
		return text.Length <= maxLength ? text : text[..Math.Max(0, maxLength - 3)] + "...";
	}

	private static int CompareText(string? left, string? right)
	{
		return StringComparer.OrdinalIgnoreCase.Compare(left ?? string.Empty, right ?? string.Empty);
	}

	private static string FormatDuration(TimeSpan value)
	{
		if (value <= TimeSpan.Zero)
			return "00:00:00";

		return value.TotalDays >= 1d
			? string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalDays}d {value:hh\\:mm}")
			: value.ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture);
	}

	private static string JoinKeys(IReadOnlyList<string> values)
	{
		if (values.Count == 0)
			return string.Empty;

		var builder = new StringBuilder(values.Count * 16);
		for (int i = 0; i < values.Count; i++)
		{
			if (i > 0)
				builder.Append(',');
			builder.Append(values[i]);
		}

		return builder.ToString();
	}

	private static string FirstKnownRemote(ServerAccountSummarySnapshot account)
	{
		return account.KnownRemotes.Count == 0 ? string.Empty : account.KnownRemotes[0];
	}

	private static string FormatDate(DateTimeOffset? value)
	{
		return value.HasValue
			? value.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			: "-";
	}

	private static string AccountNameOrId(ServerPlayerSessionSnapshot player)
	{
		return string.IsNullOrWhiteSpace(player.AccountName) ? player.AccountId : player.AccountName;
	}

	private static string OnlineStateText(ServerPlayerSessionSnapshot player)
	{
		return player.IsOnline ? "Online" : "Offline";
	}

	private void RefreshSettingsTab()
	{
		if (_settingsList is null)
			return;

		GodotServerShellOptions options = _options ?? GodotServerShellOptions.Default;
		UltraSimRuntimeRootLayout runtimeRoot = UltraSimRuntimeRootResolver.Resolve(options.RuntimeRootPath);
		IReadOnlyList<ServerSettingDescriptor> settings = BuildSettingsInventory(options, runtimeRoot);
		if (_settingsTitlePathLabel is not null)
			_settingsTitlePathLabel.Text = FormatRuntimeRelativePath(runtimeRoot, GetSettingsConfigPath(runtimeRoot));
		_settingsDescriptors = settings;
		_settingsRows.Clear();
		_settingsEditors.Clear();
		_settingsDirtyMarkers.Clear();
		ClearSettingsList();
		AppendSettingsSection("Changeable While Running", settings, ServerSettingApplyMode.ApplyWithoutRestart);
		AppendSettingsSection("Restart Required", settings, ServerSettingApplyMode.RestartRequired);
		UpdateSettingsRestartBanner();
		SetSettingsDefaultNote(settings, runtimeRoot);

		UpdateSettingsActionState();
	}

	private static IReadOnlyList<ServerSettingDescriptor> BuildSettingsInventory(
		GodotServerShellOptions options,
		UltraSimRuntimeRootLayout runtimeRoot)
	{
		return BuildSettingsRegistry(options, runtimeRoot).Snapshot();
	}

	private static ServerSettingsRegistry BuildSettingsRegistry(
		GodotServerShellOptions options,
		UltraSimRuntimeRootLayout runtimeRoot)
	{
		var registry = new ServerSettingsRegistry();
		registry.RegisterRestartRequired("server.name", "Server name", options.ProfileName);
		registry.RegisterRestartRequired("server.network.port", "Network port", options.NetworkPort.ToString(CultureInfo.InvariantCulture), editorKind: ServerSettingEditorKind.Integer);
		registry.RegisterRestartRequired("server.network.bind_address", "Bind address", options.BindAddress);
		registry.RegisterRestartRequired("server.network.max_clients", "Max clients", options.MaxClients.ToString(CultureInfo.InvariantCulture), editorKind: ServerSettingEditorKind.Integer);
		registry.RegisterRestartRequired(
			"server.save.autosave_interval_seconds",
			"Autosave interval",
			options.AutoSaveIntervalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
			editorKind: ServerSettingEditorKind.Choice,
			choices: CreateAutoSaveIntervalChoices(options.AutoSaveIntervalSeconds));
		registry.RegisterRestartRequired("server.save.on_stop", "Save on stop", FormatBool(options.SaveOnStop), editorKind: ServerSettingEditorKind.Boolean);
		registry.RegisterRestartRequired("server.session.auth_timeout_seconds", "Session auth timeout seconds", options.SessionAuthTimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture), editorKind: ServerSettingEditorKind.Decimal);
		registry.RegisterRestartRequired("server.session.keepalive_timeout_seconds", "Session keepalive timeout seconds", options.SessionKeepAliveTimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture), editorKind: ServerSettingEditorKind.Decimal);
		registry.RegisterRestartRequired("server.accounts.max_auto_created_per_ip", "Max auto-created accounts per IP", options.MaxAutoCreatedAccountsPerRemoteAddress.ToString(CultureInfo.InvariantCulture), editorKind: ServerSettingEditorKind.Integer);
		registry.RegisterRestartRequired(
			"server.persistence.backup_storage",
			"Backup compression",
			FormatBackupStorageMode(options.BackupStorageMode),
			editorKind: ServerSettingEditorKind.Choice,
			choices:
			[
				new ServerSettingChoice("off", "off"),
				new ServerSettingChoice("zip", "zip"),
				new ServerSettingChoice("zstd", "zstd")
			]);
		registry.RegisterRestartRequired(
			"server.persistence.backup_count",
			"Backup count",
			options.BackupCount.ToString(CultureInfo.InvariantCulture),
			editorKind: ServerSettingEditorKind.Choice,
			choices:
			[
				new ServerSettingChoice("0", "0"),
				new ServerSettingChoice("1", "1"),
				new ServerSettingChoice("2", "2"),
				new ServerSettingChoice("3", "3")
			]);
		registry.RegisterRestartRequired("server.persistence.backup_zstd_level", "Backup ZSTD level", options.BackupZstdCompressionLevel.ToString(CultureInfo.InvariantCulture), editorKind: ServerSettingEditorKind.Integer);
		registry.RegisterRestartRequired("server.chat.command_prefix", "Command prefix", options.CommandPrefix);
		registry.RegisterRestartRequired("server.discovery.enabled", "Discovery enabled", FormatBool(options.DiscoveryEnabled), editorKind: ServerSettingEditorKind.Boolean);
		registry.RegisterRestartRequired("server.discovery.registry_url", "Discovery registry URL", options.DiscoveryRegistryUrl);
		registry.RegisterRestartRequired(
			"server.discovery.publish_mode",
			"Discovery publish mode",
			options.DiscoveryPublishMode,
			editorKind: ServerSettingEditorKind.Choice,
			choices:
			[
				new ServerSettingChoice("disabled", "disabled"),
				new ServerSettingChoice("unverified", "unverified"),
				new ServerSettingChoice("verified", "verified")
			]);
		registry.RegisterRestartRequired("server.discovery.advertised_host", "Discovery advertised host", options.DiscoveryAdvertisedHost);
		registry.RegisterRestartRequired("server.discovery.server_id", "Discovery server ID", options.DiscoveryServerId);
		registry.RegisterRestartRequired("server.discovery.description", "Discovery description", options.DiscoveryDescription);
		registry.RegisterRestartRequired("server.discovery.region", "Discovery region", options.DiscoveryRegion);
		registry.RegisterRestartRequired("server.discovery.language", "Discovery language", options.DiscoveryLanguage);
		registry.RegisterRestartRequired("server.discovery.tags", "Discovery tags", options.DiscoveryTags);
		registry.RegisterRestartRequired("server.discovery.heartbeat_interval_seconds", "Discovery heartbeat interval", options.DiscoveryHeartbeatIntervalSeconds.ToString("0.###", CultureInfo.InvariantCulture), editorKind: ServerSettingEditorKind.Decimal);
		registry.RegisterRestartRequired("server.discovery.unpublish_on_stop", "Discovery unpublish on stop", FormatBool(options.DiscoveryUnpublishOnStop), editorKind: ServerSettingEditorKind.Boolean);
		registry.RegisterRestartRequired("server.discovery.status_endpoint_url", "Discovery status endpoint URL", options.DiscoveryStatusEndpointUrl);
		registry.RegisterRestartRequired("server.discovery.lan_enabled", "LAN discovery enabled", FormatBool(options.DiscoveryLanEnabled), editorKind: ServerSettingEditorKind.Boolean);
		registry.RegisterRestartRequired("server.discovery.lan_port", "LAN discovery port", options.DiscoveryLanPort.ToString(CultureInfo.InvariantCulture), editorKind: ServerSettingEditorKind.Integer);
		registry.RegisterRestartRequired("server.discovery.lan_bind_address", "LAN discovery bind address", options.DiscoveryLanBindAddress);
		return registry;
	}

	private void AppendSettingsSection(
		string title,
		IReadOnlyList<ServerSettingDescriptor> settings,
		ServerSettingApplyMode applyMode)
	{
		if (_settingsList is null)
			return;

		_settingsList.AddChild(CreateSettingsSectionLabel(title));
		bool hasSettings = false;
		for (int i = 0; i < settings.Count; i++)
		{
			ServerSettingDescriptor setting = settings[i];
			if (setting.ApplyMode != applyMode)
			{
				continue;
			}

			_settingsList.AddChild(CreateSettingRow(setting));
			hasSettings = true;
		}

		if (!hasSettings)
		{
			var empty = new Label();
			empty.Text = "No settings registered.";
			empty.AddThemeColorOverride("font_color", _settingsMutedColor);
			_settingsList.AddChild(empty);
		}
	}

	private void ClearSettingsList()
	{
		if (_settingsList is null)
			return;

		foreach (Node child in _settingsList.GetChildren())
		{
			_settingsList.RemoveChild(child);
			child.QueueFree();
		}
	}

	private Label CreateSettingsSectionLabel(string title)
	{
		var label = CreateSectionLabel(title, _settingsTextColor);
		label.CustomMinimumSize = new Vector2(0, 24);
		return label;
	}

	private Control CreateSettingRow(ServerSettingDescriptor setting)
	{
		var row = new GridContainer();
		row.Columns = 4;
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddThemeConstantOverride("h_separation", 10);
		row.AddThemeConstantOverride("v_separation", 4);
		row.TooltipText = BuildSettingTooltip(setting);

		var name = new Label();
		name.Text = setting.ApplyMode == ServerSettingApplyMode.RestartRequired
			? setting.DisplayName + "**"
			: setting.DisplayName;
		name.CustomMinimumSize = new Vector2(340, 0);
		name.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		name.ClipText = true;
		name.TooltipText = row.TooltipText;
		name.AddThemeColorOverride("font_color", _settingsMutedColor);
		row.AddChild(name);

		Control editor = CreateSettingEditor(setting);
		editor.CustomMinimumSize = new Vector2(GetSettingEditorMinimumWidth(setting.EditorKind), 30);
		editor.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		editor.TooltipText = row.TooltipText;
		row.AddChild(editor);
		_settingsEditors[setting.Key] = editor;

		var kind = new Label();
		kind.Text = FormatSettingEditorKind(setting.EditorKind);
		kind.CustomMinimumSize = new Vector2(86, 0);
		kind.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		kind.HorizontalAlignment = HorizontalAlignment.Right;
		kind.TooltipText = row.TooltipText;
		kind.AddThemeColorOverride("font_color", _settingsMutedColor);
		row.AddChild(kind);

		var marker = new Label();
		marker.Text = string.Empty;
		marker.CustomMinimumSize = new Vector2(68, 0);
		marker.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		marker.HorizontalAlignment = HorizontalAlignment.Right;
		marker.TooltipText = row.TooltipText;
		marker.AddThemeColorOverride("font_color", _settingsAccentSoftColor.Lightened(0.25f));
		row.AddChild(marker);
		_settingsRows[setting.Key] = row;
		_settingsDirtyMarkers[setting.Key] = marker;
		return row;
	}

	private Control CreateSettingEditor(ServerSettingDescriptor setting)
	{
		return setting.EditorKind switch
		{
			ServerSettingEditorKind.Boolean => CreateBooleanSettingEditor(setting),
			ServerSettingEditorKind.Choice => CreateChoiceSettingEditor(setting),
			_ => CreateTextSettingEditor(setting)
		};
	}

	private LineEdit CreateTextSettingEditor(ServerSettingDescriptor setting)
	{
		var editor = new LineEdit();
		editor.Text = setting.Value;
		editor.Editable = true;
		bool updatingText = false;
		editor.TextChanged += value =>
		{
			if (!updatingText)
			{
				string filtered = FilterSettingInput(setting.EditorKind, value);
				if (!string.Equals(filtered, value, StringComparison.Ordinal))
				{
					int caretColumn = editor.CaretColumn;
					updatingText = true;
					editor.Text = filtered;
					editor.CaretColumn = Math.Clamp(caretColumn - (value.Length - filtered.Length), 0, filtered.Length);
					updatingText = false;
				}
			}

			UpdateSettingsActionState();
		};
		editor.TextSubmitted += _ => CommitSettingEditorValue(setting, editor);
		editor.FocusExited += () => CommitSettingEditorValue(setting, editor);
		editor.AddThemeColorOverride("font_color", _settingsTextColor);
		editor.AddThemeStyleboxOverride("normal", CreatePanelStyle(_settingsPanelSoftColor, 6));
		editor.AddThemeStyleboxOverride("read_only", CreatePanelStyle(_settingsPanelSoftColor, 6, _settingsAccentSoftColor));
		return editor;
	}

	private CheckBox CreateBooleanSettingEditor(ServerSettingDescriptor setting)
	{
		var editor = new CheckBox();
		editor.Text = setting.Value;
		editor.ButtonPressed = string.Equals(setting.Value, "on", StringComparison.OrdinalIgnoreCase);
		editor.Disabled = false;
		editor.Toggled += enabled =>
		{
			editor.Text = enabled ? "on" : "off";
			CommitSettingEditorValue(setting, editor);
			UpdateSettingsActionState();
		};
		editor.AddThemeColorOverride("font_color", _settingsTextColor);
		return editor;
	}

	private OptionButton CreateChoiceSettingEditor(ServerSettingDescriptor setting)
	{
		var editor = new OptionButton();
		int selectedIndex = 0;
		for (int i = 0; i < setting.Choices.Count; i++)
		{
			ServerSettingChoice choice = setting.Choices[i];
			editor.AddItem(choice.DisplayName);
			if (string.Equals(choice.Value, setting.Value, StringComparison.Ordinal))
				selectedIndex = i;
		}

		editor.Select(selectedIndex);
		editor.Disabled = false;
		editor.ItemSelected += _ =>
		{
			CommitSettingEditorValue(setting, editor);
			UpdateSettingsActionState();
		};
		return editor;
	}

	private void HandleApplySettingsPressed()
	{
		ServerSettingsChangeSetValidationResult result = ValidatePendingSettingsChanges();
		if (!result.IsValid)
		{
			ReportSettingsValidationErrors(result);
			return;
		}

		if (result.Changes.Count == 0)
		{
			SetSettingsNote("No pending setting changes.");
			UpdateSettingsActionState(updateNote: false);
			return;
		}

		GodotServerShellOptions options = _options ?? GodotServerShellOptions.Default;
		UltraSimRuntimeRootLayout runtimeRoot = UltraSimRuntimeRootResolver.Resolve(options.RuntimeRootPath);
		string path = GetSettingsConfigPath(runtimeRoot);
		var store = new ServerSettingsConfigStore();
		ServerSettingsConfigDocument document = store.LoadOrEmpty(path);
		document.ApplyChanges(result.Changes);
		store.Save(path, document);

		int restartRequired = CountSettingsChanges(result.Changes, ServerSettingApplyMode.RestartRequired);
		if (restartRequired > 0 && IsServerActivelyRunning(_lastServerState))
			_settingsRestartRequiredPending = true;

		_options = ApplyValidatedSettingsToOptions(options, result.Changes);
		RefreshSettingsTab();
		SetSettingsNote($"{result.Changes.Count} Setting{(result.Changes.Count == 1 ? string.Empty : "s")} Applied.");
		AppendOutput("SETTINGS", $"Saved {result.Changes.Count} setting change(s) to {path}.");
	}

	private ServerSettingsChangeSetValidationResult ValidatePendingSettingsChanges()
	{
		IReadOnlyList<ServerSettingProposedValue> proposed = CollectSettingsProposedValues(onlyChanged: true);
		ServerSettingsRegistry registry = BuildSettingsRegistryFromCurrentDescriptors();
		return registry.ValidateChanges(proposed);
	}

	private void ReportSettingsValidationErrors(ServerSettingsChangeSetValidationResult result)
	{
		var builder = new StringBuilder();
		builder.AppendLine("Settings validation failed:");
		for (int i = 0; i < result.Errors.Count; i++)
		{
			ServerSettingChangeError error = result.Errors[i];
			builder.Append("- ");
			builder.Append(error.Key);
			builder.Append(": ");
			builder.AppendLine(error.Error);
		}

		SetSettingsNote(builder.ToString().TrimEnd());
	}

	private IReadOnlyList<ServerSettingProposedValue> CollectSettingsProposedValues(bool onlyChanged)
	{
		var proposed = new List<ServerSettingProposedValue>();
		for (int i = 0; i < _settingsDescriptors.Count; i++)
		{
			ServerSettingDescriptor descriptor = _settingsDescriptors[i];
			if (_settingsRows.TryGetValue(descriptor.Key, out Control? row) && !row.Visible)
				continue;

			if (!_settingsEditors.TryGetValue(descriptor.Key, out Control? editor))
				continue;

			string value = GetSettingEditorValue(descriptor, editor);
			if (onlyChanged && string.Equals(value, descriptor.Value, StringComparison.Ordinal))
				continue;

			proposed.Add(new ServerSettingProposedValue(descriptor.Key, value));
		}

		return proposed;
	}

	private static string GetSettingEditorValue(ServerSettingDescriptor descriptor, Control editor)
	{
		return editor switch
		{
			CheckBox checkBox => checkBox.ButtonPressed ? "on" : "off",
			OptionButton optionButton => descriptor.Choices.Count == 0
				? string.Empty
				: descriptor.Choices[Math.Clamp(optionButton.Selected, 0, descriptor.Choices.Count - 1)].Value,
			LineEdit lineEdit => lineEdit.Text,
			_ => string.Empty
		};
	}

	private string GetChoiceEditorValue(string settingKey, OptionButton optionButton)
	{
		for (int i = 0; i < _settingsDescriptors.Count; i++)
		{
			ServerSettingDescriptor descriptor = _settingsDescriptors[i];
			if (!string.Equals(descriptor.Key, settingKey, StringComparison.Ordinal) || descriptor.Choices.Count == 0)
				continue;

			int selected = Math.Clamp(optionButton.Selected, 0, descriptor.Choices.Count - 1);
			return descriptor.Choices[selected].Value;
		}

		return string.Empty;
	}

	private void CommitSettingEditorValue(ServerSettingDescriptor descriptor, Control editor)
	{
		string value = GetSettingEditorValue(descriptor, editor);
		ServerSettingValidationResult validation = ServerSettingValueValidator.Validate(descriptor, value);
		if (!validation.IsValid)
		{
			SetSettingEditorValue(descriptor, editor, descriptor.Value);
			SetSettingsNote($"{descriptor.DisplayName} reverted: {validation.Error}");
			UpdateSettingsActionState(updateNote: false);
			return;
		}

		SetSettingEditorValue(descriptor, editor, validation.NormalizedValue);
		UpdateSettingsActionState();
	}

	private static void SetSettingEditorValue(ServerSettingDescriptor descriptor, Control editor, string value)
	{
		switch (editor)
		{
			case CheckBox checkBox:
				bool enabled = string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
				checkBox.ButtonPressed = enabled;
				checkBox.Text = enabled ? "on" : "off";
				break;
			case OptionButton optionButton:
				for (int i = 0; i < descriptor.Choices.Count; i++)
				{
					if (string.Equals(descriptor.Choices[i].Value, value, StringComparison.Ordinal))
					{
						optionButton.Select(i);
						break;
					}
				}
				break;
			case LineEdit lineEdit:
				if (!string.Equals(lineEdit.Text, value, StringComparison.Ordinal))
				{
					lineEdit.Text = value;
					lineEdit.CaretColumn = value.Length;
				}
				break;
		}
	}

	private static string FilterSettingInput(ServerSettingEditorKind kind, string value)
	{
		return kind switch
		{
			ServerSettingEditorKind.Integer => FilterNumberInput(value, allowDecimalPoint: false),
			ServerSettingEditorKind.Decimal => FilterNumberInput(value, allowDecimalPoint: true),
			_ => value
		};
	}

	private static string FilterNumberInput(string value, bool allowDecimalPoint)
	{
		var builder = new StringBuilder(value.Length);
		bool hasDecimalPoint = false;
		for (int i = 0; i < value.Length; i++)
		{
			char c = value[i];
			if (char.IsDigit(c))
			{
				builder.Append(c);
				continue;
			}

			if (c == '-' && builder.Length == 0)
			{
				builder.Append(c);
				continue;
			}

			if (allowDecimalPoint && c == '.' && !hasDecimalPoint)
			{
				builder.Append(c);
				hasDecimalPoint = true;
			}
		}

		return builder.ToString();
	}

	private ServerSettingsRegistry BuildSettingsRegistryFromCurrentDescriptors()
	{
		var registry = new ServerSettingsRegistry();
		for (int i = 0; i < _settingsDescriptors.Count; i++)
		{
			registry.RegisterOrReplace(_settingsDescriptors[i]);
		}

		return registry;
	}

	private void UpdateSettingsActionState(bool updateNote = true)
	{
		UpdateSettingsConditionalVisibility();
		IReadOnlyList<ServerSettingProposedValue> pendingChanges = CollectSettingsProposedValues(onlyChanged: true);
		bool hasChanges = pendingChanges.Count > 0;
		if (_cancelSettingsButton is not null)
			_cancelSettingsButton.Disabled = !hasChanges;
		if (_applySettingsButton is not null)
			_applySettingsButton.Disabled = !hasChanges;

		UpdateSettingsDirtyMarkers(pendingChanges);

		if (updateNote)
			SetSettingsPendingNote(pendingChanges);
	}

	private void UpdateSettingsConditionalVisibility()
	{
		if (!_settingsRows.TryGetValue("server.persistence.backup_zstd_level", out Control? zstdLevelRow))
			return;

		if (!_settingsEditors.TryGetValue("server.persistence.backup_storage", out Control? backupStorageEditor))
			return;

		string backupStorage = backupStorageEditor switch
		{
			OptionButton optionButton => GetChoiceEditorValue("server.persistence.backup_storage", optionButton),
			_ => string.Empty
		};
		bool showZstdLevel = string.Equals(backupStorage, "zstd", StringComparison.OrdinalIgnoreCase);
		zstdLevelRow.Visible = showZstdLevel;
		if (!showZstdLevel && _settingsDirtyMarkers.TryGetValue("server.persistence.backup_zstd_level", out Label? marker))
			marker.Text = string.Empty;
	}

	private void UpdateSettingsDirtyMarkers(IReadOnlyList<ServerSettingProposedValue> pendingChanges)
	{
		var changedKeys = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < pendingChanges.Count; i++)
			changedKeys.Add(pendingChanges[i].Key);

		foreach (KeyValuePair<string, Label> pair in _settingsDirtyMarkers)
			pair.Value.Text = changedKeys.Contains(pair.Key) ? "modified" : string.Empty;
	}

	private void SetSettingsNote(string text)
	{
		if (_settingsNote is not null)
			_settingsNote.Text = text;
	}

	private void UpdateSettingsRestartBanner()
	{
		if (_restartRequiredBannerPanel is null || _restartRequiredBannerText is null)
			return;

		bool visible = _settingsRestartRequiredPending && IsServerActivelyRunning(_lastServerState);
		_restartRequiredBannerPanel.Visible = visible;
		_restartRequiredBannerText.Text = visible
			? "Server restart required to apply new settings."
			: string.Empty;
	}

	private void SetSettingsDefaultNote(
		IReadOnlyList<ServerSettingDescriptor> settings,
		UltraSimRuntimeRootLayout runtimeRoot)
	{
		SetSettingsNote($"{settings.Count} Settings Applied.");
	}

	private void SetSettingsPendingNote(IReadOnlyList<ServerSettingProposedValue> pendingChanges)
	{
		if (pendingChanges.Count == 0)
		{
			GodotServerShellOptions options = _options ?? GodotServerShellOptions.Default;
			UltraSimRuntimeRootLayout runtimeRoot = UltraSimRuntimeRootResolver.Resolve(options.RuntimeRootPath);
			SetSettingsDefaultNote(_settingsDescriptors, runtimeRoot);
			return;
		}

		ServerSettingsRegistry registry = BuildSettingsRegistryFromCurrentDescriptors();
		ServerSettingsChangeSetValidationResult validation = registry.ValidateChanges(pendingChanges);
		if (!validation.IsValid)
		{
			SetSettingsNote($"{pendingChanges.Count} setting change(s) pending - press Apply to save changes. Note: '**' changes take effect on next start.");
			return;
		}

		SetSettingsNote(
			$"{pendingChanges.Count} setting change(s) pending - press Apply to save changes. Note: '**' changes take effect on next start.");
	}

	private static string BuildSettingTooltip(ServerSettingDescriptor setting)
	{
		return string.IsNullOrWhiteSpace(setting.Description)
			? setting.Key
			: $"{setting.Key}\n{setting.Description}";
	}

	private static float GetSettingEditorMinimumWidth(ServerSettingEditorKind kind)
	{
		return 170f;
	}

	private static int CountSettingsChanges(
		IReadOnlyList<ServerSettingValidatedChange> changes,
		ServerSettingApplyMode applyMode)
	{
		int count = 0;
		for (int i = 0; i < changes.Count; i++)
		{
			if (changes[i].ApplyMode == applyMode)
				count++;
		}

		return count;
	}

	private static GodotServerShellOptions ApplyValidatedSettingsToOptions(
		GodotServerShellOptions options,
		IReadOnlyList<ServerSettingValidatedChange> changes)
	{
		string profileName = options.ProfileName;
		int networkPort = options.NetworkPort;
		string bindAddress = options.BindAddress;
		int maxClients = options.MaxClients;
		double autoSaveIntervalSeconds = options.AutoSaveIntervalSeconds;
		bool saveOnStop = options.SaveOnStop;
		double sessionAuthTimeoutSeconds = options.SessionAuthTimeoutSeconds;
		double sessionKeepAliveTimeoutSeconds = options.SessionKeepAliveTimeoutSeconds;
		string commandPrefix = options.CommandPrefix;
		int maxAutoCreatedAccountsPerRemoteAddress = options.MaxAutoCreatedAccountsPerRemoteAddress;
		ServerPersistenceBackupStorageMode backupStorageMode = options.BackupStorageMode;
		int backupCount = options.BackupCount;
		int backupZstdCompressionLevel = options.BackupZstdCompressionLevel;
		bool discoveryEnabled = options.DiscoveryEnabled;
		string discoveryRegistryUrl = options.DiscoveryRegistryUrl;
		string discoveryPublishMode = options.DiscoveryPublishMode;
		string discoveryAdvertisedHost = options.DiscoveryAdvertisedHost;
		string discoveryServerId = options.DiscoveryServerId;
		string discoveryDescription = options.DiscoveryDescription;
		string discoveryRegion = options.DiscoveryRegion;
		string discoveryLanguage = options.DiscoveryLanguage;
		string discoveryTags = options.DiscoveryTags;
		double discoveryHeartbeatIntervalSeconds = options.DiscoveryHeartbeatIntervalSeconds;
		bool discoveryUnpublishOnStop = options.DiscoveryUnpublishOnStop;
		string discoveryStatusEndpointUrl = options.DiscoveryStatusEndpointUrl;
		bool discoveryLanEnabled = options.DiscoveryLanEnabled;
		int discoveryLanPort = options.DiscoveryLanPort;
		string discoveryLanBindAddress = options.DiscoveryLanBindAddress;

		for (int i = 0; i < changes.Count; i++)
		{
			ServerSettingValidatedChange change = changes[i];
			switch (change.Key)
			{
				case "server.name":
					if (!string.IsNullOrWhiteSpace(change.NormalizedValue))
						profileName = change.NormalizedValue;
					break;
				case "server.network.port":
					if (int.TryParse(change.NormalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort) && parsedPort > 0)
						networkPort = parsedPort;
					break;
				case "server.network.bind_address":
					if (!string.IsNullOrWhiteSpace(change.NormalizedValue))
						bindAddress = change.NormalizedValue;
					break;
				case "server.network.max_clients":
					if (int.TryParse(change.NormalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMaxClients))
						maxClients = Math.Max(0, parsedMaxClients);
					break;
				case "server.save.autosave_interval_seconds":
					if (double.TryParse(change.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedAutoSaveIntervalSeconds))
						autoSaveIntervalSeconds = Math.Max(0.0, parsedAutoSaveIntervalSeconds);
					break;
				case "server.save.on_stop":
					if (TryParseSettingBool(change.NormalizedValue, out bool parsedSaveOnStop))
						saveOnStop = parsedSaveOnStop;
					break;
				case "server.session.auth_timeout_seconds":
					if (double.TryParse(change.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedAuthTimeout) && parsedAuthTimeout >= 0.0)
						sessionAuthTimeoutSeconds = parsedAuthTimeout;
					break;
				case "server.session.keepalive_timeout_seconds":
					if (double.TryParse(change.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedKeepAliveTimeout) && parsedKeepAliveTimeout >= 0.0)
						sessionKeepAliveTimeoutSeconds = parsedKeepAliveTimeout;
					break;
				case "server.chat.command_prefix":
					if (!string.IsNullOrWhiteSpace(change.NormalizedValue))
						commandPrefix = change.NormalizedValue;
					break;
				case "server.accounts.max_auto_created_per_ip":
					if (int.TryParse(change.NormalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedAccountLimit) && parsedAccountLimit >= 0)
						maxAutoCreatedAccountsPerRemoteAddress = parsedAccountLimit;
					break;
				case "server.persistence.backup_storage":
					if (ServerPersistenceBackupCompressorFactory.TryParseMode(change.NormalizedValue, out ServerPersistenceBackupStorageMode parsedBackupStorageMode))
						backupStorageMode = parsedBackupStorageMode;
					break;
				case "server.persistence.backup_count":
					if (int.TryParse(change.NormalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBackupCount))
						backupCount = Math.Clamp(parsedBackupCount, 0, 3);
					break;
				case "server.persistence.backup_zstd_level":
					if (int.TryParse(change.NormalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBackupZstdLevel))
						backupZstdCompressionLevel = parsedBackupZstdLevel;
					break;
				case "server.discovery.enabled":
					if (TryParseSettingBool(change.NormalizedValue, out bool parsedDiscoveryEnabled))
						discoveryEnabled = parsedDiscoveryEnabled;
					break;
				case "server.discovery.registry_url":
					discoveryRegistryUrl = change.NormalizedValue;
					break;
				case "server.discovery.publish_mode":
					discoveryPublishMode = change.NormalizedValue;
					break;
				case "server.discovery.advertised_host":
					discoveryAdvertisedHost = change.NormalizedValue;
					break;
				case "server.discovery.server_id":
					discoveryServerId = change.NormalizedValue;
					break;
				case "server.discovery.description":
					discoveryDescription = change.NormalizedValue;
					break;
				case "server.discovery.region":
					discoveryRegion = change.NormalizedValue;
					break;
				case "server.discovery.language":
					discoveryLanguage = change.NormalizedValue;
					break;
				case "server.discovery.tags":
					discoveryTags = change.NormalizedValue;
					break;
				case "server.discovery.heartbeat_interval_seconds":
					if (double.TryParse(change.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDiscoveryHeartbeat) && parsedDiscoveryHeartbeat >= 1.0)
						discoveryHeartbeatIntervalSeconds = parsedDiscoveryHeartbeat;
					break;
				case "server.discovery.unpublish_on_stop":
					if (TryParseSettingBool(change.NormalizedValue, out bool parsedDiscoveryUnpublishOnStop))
						discoveryUnpublishOnStop = parsedDiscoveryUnpublishOnStop;
					break;
				case "server.discovery.status_endpoint_url":
					discoveryStatusEndpointUrl = change.NormalizedValue;
					break;
				case "server.discovery.lan_enabled":
					if (TryParseSettingBool(change.NormalizedValue, out bool parsedDiscoveryLanEnabled))
						discoveryLanEnabled = parsedDiscoveryLanEnabled;
					break;
				case "server.discovery.lan_port":
					if (int.TryParse(change.NormalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDiscoveryLanPort) &&
						parsedDiscoveryLanPort >= 0 &&
						parsedDiscoveryLanPort <= ushort.MaxValue)
					{
						discoveryLanPort = parsedDiscoveryLanPort;
					}
					break;
				case "server.discovery.lan_bind_address":
					if (!string.IsNullOrWhiteSpace(change.NormalizedValue))
						discoveryLanBindAddress = change.NormalizedValue;
					break;
			}
		}

		return options with
		{
			ProfileName = profileName,
			NetworkPort = networkPort,
			BindAddress = bindAddress,
			MaxClients = maxClients,
			AutoSaveIntervalSeconds = autoSaveIntervalSeconds,
			SaveOnStop = saveOnStop,
			SessionAuthTimeoutSeconds = sessionAuthTimeoutSeconds,
			SessionKeepAliveTimeoutSeconds = sessionKeepAliveTimeoutSeconds,
			CommandPrefix = commandPrefix,
			MaxAutoCreatedAccountsPerRemoteAddress = maxAutoCreatedAccountsPerRemoteAddress,
			BackupStorageMode = backupStorageMode,
			BackupCount = backupCount,
			BackupZstdCompressionLevel = backupZstdCompressionLevel,
			DiscoveryEnabled = discoveryEnabled,
			DiscoveryRegistryUrl = discoveryRegistryUrl,
			DiscoveryPublishMode = discoveryPublishMode,
			DiscoveryAdvertisedHost = discoveryAdvertisedHost,
			DiscoveryServerId = discoveryServerId,
			DiscoveryDescription = discoveryDescription,
			DiscoveryRegion = discoveryRegion,
			DiscoveryLanguage = discoveryLanguage,
			DiscoveryTags = discoveryTags,
			DiscoveryHeartbeatIntervalSeconds = discoveryHeartbeatIntervalSeconds,
			DiscoveryUnpublishOnStop = discoveryUnpublishOnStop,
			DiscoveryStatusEndpointUrl = discoveryStatusEndpointUrl,
			DiscoveryLanEnabled = discoveryLanEnabled,
			DiscoveryLanPort = discoveryLanPort,
			DiscoveryLanBindAddress = discoveryLanBindAddress
		};
	}

	private static bool TryParseSettingBool(string value, out bool result)
	{
		if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "1", StringComparison.Ordinal))
		{
			result = true;
			return true;
		}

		if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "0", StringComparison.Ordinal))
		{
			result = false;
			return true;
		}

		result = false;
		return false;
	}

	private static string GetSettingsConfigPath(UltraSimRuntimeRootLayout runtimeRoot)
	{
		return ServerSettingsConfigPaths.GetPath(runtimeRoot.RootPath);
	}

	private static string FormatRuntimeRelativePath(UltraSimRuntimeRootLayout runtimeRoot, string path)
	{
		string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(runtimeRoot.RootPath));
		string fullPath = Path.GetFullPath(path);
		if (string.Equals(fullPath, root, StringComparison.Ordinal))
			return "/";

		string rootWithSeparator = root + Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
			return fullPath;

		string relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/').TrimStart('/');
		return "/" + relative;
	}

	private static string FormatSettingEditorKind(ServerSettingEditorKind kind)
	{
		return kind switch
		{
			ServerSettingEditorKind.Integer => "integer",
			ServerSettingEditorKind.Decimal => "decimal",
			ServerSettingEditorKind.Boolean => "boolean",
			ServerSettingEditorKind.Choice => "choice",
			ServerSettingEditorKind.Path => "path",
			_ => "text"
		};
	}

	private static string FormatBool(bool value) => value ? "on" : "off";

	private static string FormatBackupStorageMode(ServerPersistenceBackupStorageMode mode)
	{
		return mode switch
		{
			ServerPersistenceBackupStorageMode.Directory => "off",
			ServerPersistenceBackupStorageMode.Zip => "zip",
			ServerPersistenceBackupStorageMode.Zstd => "zstd",
			_ => mode.ToString()
		};
	}

	private static ServerSettingChoice[] CreateAutoSaveIntervalChoices(double currentValue)
	{
		string current = currentValue.ToString("0.###", CultureInfo.InvariantCulture);
		ServerSettingChoice[] presets =
		[
			new ServerSettingChoice("0", "off"),
			new ServerSettingChoice("60", "1m"),
			new ServerSettingChoice("300", "5m"),
			new ServerSettingChoice("900", "15m"),
			new ServerSettingChoice("1800", "30m"),
			new ServerSettingChoice("3600", "60m")
		];
		for (int i = 0; i < presets.Length; i++)
		{
			if (string.Equals(presets[i].Value, current, StringComparison.Ordinal))
				return presets;
		}

		return [.. presets, new ServerSettingChoice(current, $"{current}s")];
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
		_lastServerState = state;
		UpdateSettingsRestartBanner();

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

	private static bool IsServerActivelyRunning(ServerApplicationState state)
	{
		return state is
			ServerApplicationState.Initializing or
			ServerApplicationState.Starting or
			ServerApplicationState.Loading or
			ServerApplicationState.Running or
			ServerApplicationState.Saving or
			ServerApplicationState.Restarting;
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
