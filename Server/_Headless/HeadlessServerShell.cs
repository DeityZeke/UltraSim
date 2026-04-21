using System.Diagnostics;
using UltraSim.Server.Hosting;

namespace UltraSim.Server.Headless;

public sealed class HeadlessServerShell
{
    private readonly Func<HeadlessServerShellOptions, IServerApplication> _applicationFactory;
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;

    public HeadlessServerShell(
        HeadlessServerShellOptions options,
        TextWriter? stdout = null,
        TextWriter? stderr = null,
        Func<HeadlessServerShellOptions, IServerApplication>? applicationFactory = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _stdout = stdout ?? Console.Out;
        _stderr = stderr ?? Console.Error;
        _applicationFactory = applicationFactory ?? CreateDefaultApplication;
    }

    public HeadlessServerShellOptions Options { get; }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        IServerApplication application = _applicationFactory(Options);
        IServerHostEventSink[] sinks = Options.Quiet
            ? Array.Empty<IServerHostEventSink>()
            : new IServerHostEventSink[] { new HeadlessServerEventSink(_stdout, _stderr) };

        using var host = new ServerHostLoop(
            application,
            new ServerHostLoopOptions(
                TimeSpan.FromMilliseconds(Options.TickMilliseconds),
                Options.MaxCatchUpTicksPerPump),
            sinks);

        if (Options.AutoStart)
            host.EnqueueCommand(ServerControlCommand.Start());

        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeSpan previousElapsed = stopwatch.Elapsed;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TimeSpan currentElapsed = stopwatch.Elapsed;
                TimeSpan delta = currentElapsed - previousElapsed;
                previousElapsed = currentElapsed;

                host.Pump(delta);

                if (Options.RunFor.HasValue && currentElapsed >= Options.RunFor.Value)
                    break;

                if (Options.AutoStart && application.State == ServerApplicationState.Stopped)
                    break;

                if (Options.IdleDelay > TimeSpan.Zero)
                    await Task.Delay(Options.IdleDelay, cancellationToken).ConfigureAwait(false);
                else
                    await Task.Yield();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _stderr.WriteLine(ex);
            _stderr.Flush();
            TryStop(application);
            return 1;
        }

        TryStop(application);
        return application.State == ServerApplicationState.Faulted ? 1 : 0;
    }

    private static IServerApplication CreateDefaultApplication(HeadlessServerShellOptions options) =>
        new ServerApplication(new ServerApplicationOptions(
            options.ProfileName,
            SaveOnStop: options.SaveOnStop));

    private static void TryStop(IServerApplication application)
    {
        if (application.State == ServerApplicationState.Stopped ||
            application.State == ServerApplicationState.Created)
        {
            application.Stop();
            return;
        }

        try
        {
            application.Stop();
        }
        catch
        {
        }
    }
}
