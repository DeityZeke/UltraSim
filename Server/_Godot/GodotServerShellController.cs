using UltraSim.Server.Hosting;
using UltraSim.Server.Discovery;
using UltraSim.Server.Network;
using UltraSim.Server.Scripting;

namespace UltraSim.Server.Godot;

public sealed class GodotServerShellController : IDisposable
{
	private readonly HostedServerRuntime _runtime;
	private readonly UltraSimRuntimeRootLayout _runtimeRoot;
	private readonly List<ServerApplicationEvent> _startupEvents = new();
	private readonly HttpClient? _discoveryHttpClient;
	private readonly ServerDiscoveryPublisherCoordinator? _discovery;
	private readonly SemaphoreSlim _discoveryGate = new(1, 1);
	private bool _disposed;

	public GodotServerShellController(
		GodotServerShellOptions options,
		Func<GodotServerShellOptions, IServerApplication>? applicationFactory = null)
	{
		Options = options ?? throw new ArgumentNullException(nameof(options));
		UltraSimRuntimeRootLayout runtimeRoot = UltraSimRuntimeRootResolver.Resolve(options.RuntimeRootPath);
		_runtimeRoot = runtimeRoot;
		UltraSimRuntimeTemplate.Ensure(runtimeRoot);
		foreach (string line in ServerHostStartupBanner.Create("Godot Server Shell"))
			AddStartupLog(line);
		AddStartupLog($"Runtime: Root path {runtimeRoot.RootPath}");
		UltraSimScriptStartupResult scriptStartup = new UltraSimScriptStartupService().Run(new UltraSimScriptStartupOptions(runtimeRoot));
		ScriptContext = scriptStartup.Context;
		_startupEvents.AddRange(UltraSimScriptStartupLog.CreateEvents(scriptStartup));
		if (!scriptStartup.Succeeded)
			throw new UltraSimScriptStartupException(scriptStartup);

		Func<GodotServerShellOptions, IServerApplication> factory = applicationFactory ?? CreateDefaultApplication;
		IServerApplication application = factory(options);

		_runtime = new HostedServerRuntime(
			application,
			new ServerHostLoopOptions(
				TimeSpan.FromMilliseconds(options.TickMilliseconds),
				options.MaxCatchUpTicksPerPump),
			new IServerHostEventSink[] { new ForwardingSink(HandleEvent) });
		_discoveryHttpClient = options.DiscoveryEnabled
			? new HttpClient { Timeout = TimeSpan.FromSeconds(5) }
			: null;
		_discovery = CreateDiscoveryCoordinator(_discoveryHttpClient);
	}

	public GodotServerShellOptions Options { get; }
	public DefaultUltraSimScriptContext? ScriptContext { get; }
	public IReadOnlyList<ServerApplicationEvent> StartupEvents => _startupEvents;
	public ServerApplicationState State => _runtime.State;
	public bool IsRunning => _runtime.IsRunning;

	public event Action<ServerApplicationEvent>? EventRaised;

	public void Start()
	{
		_runtime.StartImmediately();
		QueueDiscoveryPublishIfDue();
	}

	public void TickFrame(double deltaSeconds)
	{
		if (deltaSeconds < 0d)
			throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Frame delta must be non-negative.");

		_runtime.Pump(TimeSpan.FromSeconds(deltaSeconds));
		QueueDiscoveryPublishIfDue();
	}

	public ServerCommandResult Execute(ServerControlCommand command)
	{
		_runtime.TryExecuteCommand(command, out ServerCommandResult result);
		if (_runtime.State == ServerApplicationState.Running)
			QueueDiscoveryPublishIfDue();
		else if (_runtime.State == ServerApplicationState.Stopped)
			PublishDiscoveryShutdown();

		return result;
	}

	public ServerStatusSnapshot GetStatusSnapshot()
	{
		return _runtime.GetStatusSnapshot();
	}

	public ServerMetricsSnapshot GetMetricsSnapshot()
	{
		return _runtime.GetMetricsSnapshot();
	}

	public IReadOnlyList<ServerSystemRuntimeSnapshot> GetSystemRuntimeSnapshots()
	{
		return _runtime.Application.GetSystemRuntimeSnapshots();
	}

	public IReadOnlyList<ServerPlayerSessionSnapshot> GetPlayerSessionsSnapshot()
	{
		return _runtime.Application.GetPlayerSessionsSnapshot();
	}

	public IReadOnlyList<ServerAccountSummarySnapshot> GetAccountSummariesSnapshot()
	{
		return _runtime.Application.GetAccountSummariesSnapshot();
	}

	public ServerPlayerSessionActionResult KickPlayerSession(string sessionId)
	{
		return _runtime.Application.KickPlayerSession(sessionId);
	}

	public ServerAccountActionResult BanAccount(string accountId)
	{
		return _runtime.Application.BanAccount(accountId);
	}

	public ServerAccountActionResult UnbanAccount(string accountId)
	{
		return _runtime.Application.UnbanAccount(accountId);
	}

	public ServerAccountAccessUpdateResult SetAccountAccessLevel(string accountId, int accessLevel)
	{
		return _runtime.Application.SetAccountAccessLevel(accountId, accessLevel);
	}

	public ServerAccountAccessUpdateResult ReplaceAccountAccess(
		string accountId,
		int accessLevel,
		IReadOnlyList<string> directPermissionGrants,
		IReadOnlyList<string> directPermissionDenies)
	{
		return _runtime.Application.ReplaceAccountAccess(
			accountId,
			accessLevel,
			directPermissionGrants,
			directPermissionDenies);
	}

	public void Shutdown()
	{
		_runtime.StopSafely();
		PublishDiscoveryShutdown();
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		_runtime.StopSafely();
		PublishDiscoveryShutdown();
		_runtime.Dispose();
		_discoveryHttpClient?.Dispose();
		_discoveryGate.Dispose();
	}

	private IServerApplication CreateDefaultApplication(GodotServerShellOptions options) =>
		options.EnableMovementWorld
			? ServerMovementWorldApplicationFactory.Create(new ServerMovementWorldApplicationOptions(
				options.ProfileName,
				Port: options.NetworkPort,
				BindAddress: options.BindAddress,
				SaveOnStop: options.SaveOnStop,
				MaxClients: options.MaxClients,
				AutoSaveIntervalSeconds: options.AutoSaveIntervalSeconds,
				SessionAuthTimeoutSeconds: options.SessionAuthTimeoutSeconds,
				SessionKeepAliveTimeoutSeconds: options.SessionKeepAliveTimeoutSeconds,
				CommandPrefix: options.CommandPrefix,
				MaxAutoCreatedAccountsPerRemoteAddress: options.MaxAutoCreatedAccountsPerRemoteAddress,
				ScriptContext: ScriptContext,
				PersistenceRoot: _runtimeRoot.SavesPath,
				PersistenceBackupsRoot: _runtimeRoot.BackupsPath,
				PersistenceBackupStorageMode: options.BackupStorageMode,
				PersistenceBackupCount: options.BackupCount,
				PersistenceBackupZstdCompressionLevel: options.BackupZstdCompressionLevel))
			: new ServerApplication(new ServerApplicationOptions(
				options.ProfileName,
				SaveOnStop: options.SaveOnStop));

	private ServerDiscoveryPublisherCoordinator? CreateDiscoveryCoordinator(HttpClient? httpClient)
	{
		if (!Options.DiscoveryEnabled || httpClient is null)
			return null;

		try
		{
			ServerDiscoveryPublicationPlan plan = ServerDiscoveryPublicationFactory.CreateOfficialRegistryPlan(
				new ServerDiscoveryPublicationSettings(
					Enabled: Options.DiscoveryEnabled,
					RegistryUrl: Options.DiscoveryRegistryUrl,
					PublishMode: Options.DiscoveryPublishMode,
					ServerId: Options.DiscoveryServerId,
					DisplayName: Options.ProfileName,
					AdvertisedHost: Options.DiscoveryAdvertisedHost,
					Port: Options.NetworkPort,
					MaxUsers: Options.MaxClients,
					Description: Options.DiscoveryDescription,
					Region: Options.DiscoveryRegion,
					PrimaryLanguage: Options.DiscoveryLanguage,
					Tags: Options.DiscoveryTags,
					HeartbeatIntervalSeconds: Options.DiscoveryHeartbeatIntervalSeconds,
					UnpublishOnStop: Options.DiscoveryUnpublishOnStop,
					StatusEndpointUrl: Options.DiscoveryStatusEndpointUrl),
				httpClient);

			return new ServerDiscoveryPublisherCoordinator(
				plan.Publisher,
				plan.AdvertisedSettings,
				_runtime.GetStatusSnapshot,
				plan.PublisherOptions);
		}
		catch (Exception ex)
		{
			EmitLog($"Discovery disabled: {ex.Message}");
			return null;
		}
	}

	private void QueueDiscoveryPublishIfDue()
	{
		if (_discovery is null || _runtime.State != ServerApplicationState.Running)
			return;

		if (!_discoveryGate.Wait(0))
			return;

		_ = PublishDiscoveryIfDueAsync();
	}

	private async Task PublishDiscoveryIfDueAsync()
	{
		try
		{
			if (_discovery is null)
				return;

			if (_discovery.HasPublished)
				await _discovery.PublishHeartbeatIfDueAsync().ConfigureAwait(false);
			else
				await _discovery.PublishStartupAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			EmitLog($"Discovery publish failed: {ex.Message}");
		}
		finally
		{
			_discoveryGate.Release();
		}
	}

	private void PublishDiscoveryShutdown()
	{
		if (_discovery is null || !_discovery.HasPublished)
			return;

		try
		{
			_discoveryGate.Wait();
			try
			{
				_discovery.PublishShutdownAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
			}
			finally
			{
				_discoveryGate.Release();
			}
		}
		catch (Exception ex)
		{
			EmitLog($"Discovery shutdown publish failed: {ex.Message}");
		}
	}

	private void HandleEvent(ServerApplicationEvent evt)
	{
		EventRaised?.Invoke(evt);
	}

	private void AddStartupLog(string message)
	{
		_startupEvents.Add(new ServerApplicationEvent(
			ServerApplicationEventKind.Log,
			DateTimeOffset.UtcNow,
			message));
	}

	private void EmitLog(string message)
	{
		var evt = new ServerApplicationEvent(
			ServerApplicationEventKind.Log,
			DateTimeOffset.UtcNow,
			message);
		if (EventRaised is null)
			_startupEvents.Add(evt);
		else
			EventRaised.Invoke(evt);
	}

	private sealed class ForwardingSink : IServerHostEventSink
	{
		private readonly Action<ServerApplicationEvent> _handler;

		public ForwardingSink(Action<ServerApplicationEvent> handler)
		{
			_handler = handler ?? throw new ArgumentNullException(nameof(handler));
		}

		public void OnEvent(ServerApplicationEvent evt)
		{
			_handler(evt);
		}
	}
}
