using UltraSim.Server.Runtime;

namespace UltraSim.Server.Hosting;

/// <summary>
/// In-process host for local/bootstrap execution of the ordinary ServerRuntime.
/// </summary>
/// <remarks>
/// This host does not subclass or replace the authoritative runtime and does not
/// create a packet/network loopback. It drives the same semantic runtime in the
/// current process. All lifecycle, observation, and tick calls remain bound to
/// the thread that constructed the host, matching <see cref="ServerRuntime"/>.
/// </remarks>
public sealed class InternalHost : IDisposable
{
    private readonly HostedRuntime _host;

    public InternalHost(
        IServerComposition composition,
        ServerRuntimeOptions runtimeOptions,
        HostCapabilities capabilities = default)
    {
        _host = new HostedRuntime("internal", composition, runtimeOptions, capabilities);
    }

    public ServerRuntimeState State => _host.Runtime.State;

    public ulong ActiveGenerationId => _host.Runtime.ActiveGenerationId;

    public ulong SimulationTick => _host.Runtime.SimulationTick;

    public long WorldTimeMicroseconds => _host.Runtime.WorldTimeMicroseconds;

    public int SimulationFrequencyHz => _host.Runtime.SimulationFrequencyHz;

    public double SimulationDeltaSeconds => _host.Runtime.SimulationDeltaSeconds;

    public int PlaybackRate => _host.Runtime.PlaybackRate;

    public void Start() => _host.Start();

    public void Tick() => _host.Tick();

    public int Tick(TimeSpan hostElapsed) => _host.Tick(hostElapsed);

    public void SetPlaybackRate(int playbackRate) => _host.SetPlaybackRate(playbackRate);

    public void SetSimulationFrequency(int simulationFrequencyHz) =>
        _host.SetSimulationFrequency(simulationFrequencyHz);

    public void Stop() => _host.Stop();

    public void Dispose() => _host.Dispose();

    internal ServerRuntime RuntimeForTesting => _host.Runtime;
}
