using UltraSim.Server.Runtime;

namespace UltraSim.Server.Hosting;

/// <summary>Shared thin lifecycle adapter used by the two S0.2 host shells.</summary>
internal sealed class HostedRuntime : IDisposable
{
    private readonly HostCapabilities _capabilities;
    private readonly string _hostName;

    internal HostedRuntime(
        string hostName,
        IServerComposition composition,
        ServerRuntimeOptions runtimeOptions,
        HostCapabilities capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(runtimeOptions);

        _hostName = hostName;
        _capabilities = capabilities;
        Runtime = new ServerRuntime(composition, runtimeOptions);
    }

    internal ServerRuntime Runtime { get; }

    internal void Start()
    {
        try
        {
            Runtime.Initialize();
            Runtime.Start();
            TryLog(HostLogLevel.Information, "started");
        }
        catch (Exception exception)
        {
            TryLog(HostLogLevel.Error, $"start failed: {exception.Message}");
            throw;
        }
    }

    internal void Tick()
    {
        Runtime.Tick();
    }

    internal int Tick(TimeSpan hostElapsed)
    {
        return Runtime.Tick(hostElapsed);
    }

    internal void SetPlaybackRate(int playbackRate)
    {
        Runtime.SetPlaybackRate(playbackRate);
    }

    internal void Stop()
    {
        ServerRuntimeState state = Runtime.State;
        if (state == ServerRuntimeState.Running)
        {
            Runtime.Stop();
            TryLog(HostLogLevel.Information, "stopped");
            return;
        }

        if (state is ServerRuntimeState.Stopped or ServerRuntimeState.Disposed)
            return;

        // Preserve ServerRuntime's lifecycle rejection instead of turning an
        // invalid host transition into a silent no-op.
        Runtime.Stop();
    }

    internal void Shutdown()
    {
        try
        {
            // Process shutdown is cleanup, not an ordinary lifecycle command:
            // it remains safe before start, after stop, and after a terminal
            // runtime fault. Public Stop still preserves Runtime's rejections.
            if (Runtime.State == ServerRuntimeState.Running)
            {
                Runtime.Stop();
                TryLog(HostLogLevel.Information, "stopped");
            }
        }
        finally
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        if (Runtime.State == ServerRuntimeState.Disposed)
            return;

        try
        {
            Runtime.Dispose();
        }
        finally
        {
            TryLog(HostLogLevel.Information, "disposed");
        }
    }

    /// <summary>
    /// Emits best-effort host diagnostics without allowing an optional sink to
    /// change authoritative lifecycle or simulation behavior.
    /// </summary>
    internal void TryLog(HostLogLevel level, string message)
    {
        try
        {
            _capabilities.LogSink?.Write(level, $"{_hostName}: {message}");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Diagnostics are not authority. Hosts that require durable/audited
            // logging must implement that policy outside this simulation shell.
        }
    }

    internal void EnsureRunning(string operation)
    {
        if (Runtime.State != ServerRuntimeState.Running)
            throw new InvalidOperationException($"{operation} requires a running server runtime.");
    }
}
