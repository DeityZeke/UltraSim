using System.Collections.Generic;

namespace UltraSim.Server.Hosting;

public sealed class ServerHostLoop : IDisposable
{
    private readonly Queue<ServerControlCommand> _pendingCommands = new();
    private readonly List<IServerHostEventSink> _sinks = new();
    private readonly double _tickIntervalSeconds;
    private double _accumulatorSeconds;
    private bool _disposed;

    public ServerHostLoop(
        IServerApplication application,
        ServerHostLoopOptions? options = null,
        IEnumerable<IServerHostEventSink>? sinks = null)
    {
        Application = application ?? throw new ArgumentNullException(nameof(application));
        Options = options ?? ServerHostLoopOptions.Default;
        if (Options.TickInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "TickInterval must be positive.");
        if (Options.MaxCatchUpTicksPerPump <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxCatchUpTicksPerPump must be positive.");

        _tickIntervalSeconds = Options.TickInterval.TotalSeconds;
        if (sinks is not null)
            _sinks.AddRange(sinks);

        Application.EventRaised += HandleApplicationEvent;
    }

    public IServerApplication Application { get; }
    public ServerHostLoopOptions Options { get; }

    public void AddSink(IServerHostEventSink sink)
    {
        if (sink is null)
            throw new ArgumentNullException(nameof(sink));

        _sinks.Add(sink);
    }

    public void EnqueueCommand(ServerControlCommand command)
    {
        _pendingCommands.Enqueue(command);
    }

    public ServerHostPumpResult Pump(TimeSpan elapsed)
    {
        ThrowIfDisposed();

        int commandsHandled = 0;
        while (_pendingCommands.Count > 0)
        {
            ServerControlCommand command = _pendingCommands.Dequeue();
            Application.TryExecuteCommand(command, out _);
            commandsHandled++;
        }

        if (elapsed > TimeSpan.Zero)
            _accumulatorSeconds += elapsed.TotalSeconds;

        int ticksProcessed = 0;
        while (Application.IsRunning &&
               _accumulatorSeconds >= _tickIntervalSeconds &&
               ticksProcessed < Options.MaxCatchUpTicksPerPump)
        {
            Application.Tick((float)_tickIntervalSeconds);
            _accumulatorSeconds -= _tickIntervalSeconds;
            ticksProcessed++;
        }

        int droppedCatchUpTicks = 0;
        if (Application.IsRunning && _accumulatorSeconds >= _tickIntervalSeconds)
        {
            droppedCatchUpTicks = (int)(_accumulatorSeconds / _tickIntervalSeconds);
            _accumulatorSeconds -= droppedCatchUpTicks * _tickIntervalSeconds;
        }

        return new ServerHostPumpResult(commandsHandled, ticksProcessed, droppedCatchUpTicks);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Application.EventRaised -= HandleApplicationEvent;
        _disposed = true;
    }

    private void HandleApplicationEvent(ServerApplicationEvent evt)
    {
        for (int i = 0; i < _sinks.Count; i++)
            _sinks[i].OnEvent(evt);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ServerHostLoop));
    }
}
