using UltraSim.Server.Hosting;
using UltraSim.Server.Network;
using UltraSim.Server.Scripting;

namespace UltraSim.Server.Godot;

public sealed class GodotServerShellController : IDisposable
{
	private readonly HostedServerRuntime _runtime;
	private readonly List<ServerApplicationEvent> _startupEvents = new();

	public GodotServerShellController(
		GodotServerShellOptions options,
		Func<GodotServerShellOptions, IServerApplication>? applicationFactory = null)
	{
		Options = options ?? throw new ArgumentNullException(nameof(options));
		UltraSimRuntimeRootLayout runtimeRoot = UltraSimRuntimeRootResolver.Resolve(options.RuntimeRootPath);
		UltraSimRuntimeTemplate.Ensure(runtimeRoot);
		foreach (string line in ServerHostStartupBanner.Create("Godot Server Shell"))
			AddStartupLog(line);
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
	}

	public void TickFrame(double deltaSeconds)
	{
		if (deltaSeconds < 0d)
			throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Frame delta must be non-negative.");

		_runtime.Pump(TimeSpan.FromSeconds(deltaSeconds));
	}

	public ServerCommandResult Execute(ServerControlCommand command)
	{
		_runtime.TryExecuteCommand(command, out ServerCommandResult result);
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

	public void Shutdown()
	{
		_runtime.StopSafely();
	}

	public void Dispose()
	{
		_runtime.StopSafely();
		_runtime.Dispose();
	}

	private IServerApplication CreateDefaultApplication(GodotServerShellOptions options) =>
		options.EnableMovementWorld
			? ServerMovementWorldApplicationFactory.Create(new ServerMovementWorldApplicationOptions(
				options.ProfileName,
				Port: options.NetworkPort,
				SaveOnStop: options.SaveOnStop,
				SessionAuthTimeoutSeconds: options.SessionAuthTimeoutSeconds,
				SessionKeepAliveTimeoutSeconds: options.SessionKeepAliveTimeoutSeconds,
				ScriptContext: ScriptContext))
			: new ServerApplication(new ServerApplicationOptions(
				options.ProfileName,
				SaveOnStop: options.SaveOnStop));

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
