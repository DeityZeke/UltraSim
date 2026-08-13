using UltraSim.Server.Runtime;

namespace UltraSim.Server.Hosting;

/// <summary>
/// Console/headless shell for the ordinary authoritative ServerRuntime.
/// </summary>
/// <remarks>
/// The host owns process-facing startup, diagnostics, fixed-step driving, and
/// graceful shutdown. It does not own simulation policy or a separate server
/// implementation. All lifecycle, observation, and tick calls remain bound to
/// the thread that constructed the host, matching <see cref="ServerRuntime"/>.
/// </remarks>
public sealed class HeadlessHost : IDisposable
{
    private readonly HostedRuntime _host;

    public HeadlessHost(
        IServerComposition composition,
        ServerRuntimeOptions runtimeOptions,
        HostCapabilities capabilities = default)
    {
        _host = new HostedRuntime(
            "headless",
            composition,
            runtimeOptions,
            capabilities.LogSink is null
                ? new HostCapabilities(ConsoleHostLogSink.Instance)
                : capabilities);
    }

    public ServerRuntimeState State => _host.Runtime.State;

    public ulong ActiveGenerationId => _host.Runtime.ActiveGenerationId;

    public ulong SimulationTick => _host.Runtime.SimulationTick;

    public long WorldTimeMicroseconds => _host.Runtime.WorldTimeMicroseconds;

    public int PlaybackRate => _host.Runtime.PlaybackRate;

    public void Start() => _host.Start();

    public void Tick() => _host.Tick();

    public int Tick(TimeSpan hostElapsed) => _host.Tick(hostElapsed);

    public void SetPlaybackRate(int playbackRate) => _host.SetPlaybackRate(playbackRate);

    /// <summary>
    /// Drives a deterministic fixed-step headless run until completion or
    /// cancellation. The caller remains responsible for graceful shutdown.
    /// </summary>
    public int RunFixedSteps(int maximumSteps, CancellationToken cancellationToken = default)
    {
        if (maximumSteps < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSteps));
        _host.EnsureRunning(nameof(RunFixedSteps));

        int executed = 0;
        while (executed < maximumSteps && !cancellationToken.IsCancellationRequested)
        {
            _host.Tick();
            executed++;
        }
        return executed;
    }

    /// <summary>Stops, disposes, and logs the headless process shutdown.</summary>
    public void Shutdown() => _host.Shutdown();

    public void Stop() => _host.Stop();

    public void Dispose() => _host.Dispose();

    internal ServerRuntime RuntimeForTesting => _host.Runtime;
}
