namespace UltraSim.Server.Hosting;

public sealed class HostedServerRuntime : IDisposable
{
    private readonly ServerHostLoop _hostLoop;

    public HostedServerRuntime(
        IServerApplication application,
        ServerHostLoopOptions? options = null,
        IEnumerable<IServerHostEventSink>? sinks = null)
    {
        Application = application ?? throw new ArgumentNullException(nameof(application));
        _hostLoop = new ServerHostLoop(application, options, sinks);
    }

    public IServerApplication Application { get; }
    public ServerApplicationState State => Application.State;
    public bool IsRunning => Application.IsRunning;

    public void AddSink(IServerHostEventSink sink)
    {
        _hostLoop.AddSink(sink);
    }

    public void EnqueueCommand(ServerControlCommand command)
    {
        _hostLoop.EnqueueCommand(command);
    }

    public ServerHostPumpResult Pump(TimeSpan elapsed)
    {
        return _hostLoop.Pump(elapsed);
    }

    public bool TryExecuteCommand(ServerControlCommand command, out ServerCommandResult result)
    {
        return Application.TryExecuteCommand(command, out result);
    }

    public ServerStatusSnapshot GetStatusSnapshot()
    {
        return Application.GetStatusSnapshot();
    }

    public ServerMetricsSnapshot GetMetricsSnapshot()
    {
        return Application.GetMetricsSnapshot();
    }

    public void StartImmediately()
    {
        _hostLoop.EnqueueCommand(ServerControlCommand.Start());
        _hostLoop.Pump(TimeSpan.Zero);
    }

    public void StopSafely()
    {
        if (State == ServerApplicationState.Stopped ||
            State == ServerApplicationState.Created)
        {
            Application.Stop();
            return;
        }

        try
        {
            Application.Stop();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    public void Dispose()
    {
        _hostLoop.Dispose();
    }
}
