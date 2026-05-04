using System.Diagnostics;
using UltraSim.Server.Hosting;
using UltraSim.Server.Network;
using UltraSim.Server.Scripting;

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
    public DefaultUltraSimScriptContext? ScriptContext { get; private set; }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        UltraSimRuntimeRootLayout runtimeRoot = UltraSimRuntimeRootResolver.Resolve(Options.RuntimeRootPath);
        UltraSimRuntimeTemplate.Ensure(runtimeRoot);

        if (!Options.Quiet)
        {
            WriteStartupPreamble(_stdout);
            UltraSimScriptStartupResult scriptStartup = RunScriptStartup(runtimeRoot, _stdout);
            ScriptContext = scriptStartup.Context;
            if (!scriptStartup.Succeeded)
                return 1;
        }
        else
        {
            UltraSimScriptStartupResult scriptStartup = RunScriptStartup(runtimeRoot, TextWriter.Null);
            ScriptContext = scriptStartup.Context;
            if (!scriptStartup.Succeeded)
                return 1;
        }

        IServerApplication application = _applicationFactory(Options);
        IServerHostEventSink[] sinks = Options.Quiet
            ? Array.Empty<IServerHostEventSink>()
            : new IServerHostEventSink[] { new HeadlessServerEventSink(_stdout, _stderr) };

        using var runtime = new HostedServerRuntime(
            application,
            new ServerHostLoopOptions(
                TimeSpan.FromMilliseconds(Options.TickMilliseconds),
                Options.MaxCatchUpTicksPerPump),
            sinks);

        if (Options.AutoStart)
            runtime.StartImmediately();

        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeSpan previousElapsed = stopwatch.Elapsed;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TimeSpan currentElapsed = stopwatch.Elapsed;
                TimeSpan delta = currentElapsed - previousElapsed;
                previousElapsed = currentElapsed;

                runtime.Pump(delta);

                if (Options.RunFor.HasValue && currentElapsed >= Options.RunFor.Value)
                    break;

                if (Options.AutoStart && runtime.State == ServerApplicationState.Stopped)
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
            runtime.StopSafely();
            return 1;
        }

        runtime.StopSafely();
        return runtime.State == ServerApplicationState.Faulted ? 1 : 0;
    }

    private IServerApplication CreateDefaultApplication(HeadlessServerShellOptions options) =>
        options.EnableMovementWorld
            ? ServerMovementWorldApplicationFactory.Create(new ServerMovementWorldApplicationOptions(
                options.ProfileName,
                Port: options.NetworkPort,
                SaveOnStop: options.SaveOnStop,
                SessionAuthTimeoutSeconds: options.SessionAuthTimeoutSeconds,
                SessionKeepAliveTimeoutSeconds: options.SessionKeepAliveTimeoutSeconds,
                MaxAutoCreatedAccountsPerRemoteAddress: options.MaxAutoCreatedAccountsPerRemoteAddress,
                ScriptContext: ScriptContext,
                PersistenceRoot: ScriptContext!.RuntimeRoot.SavesPath,
                PersistenceBackupsRoot: ScriptContext.RuntimeRoot.BackupsPath))
            : new ServerApplication(new ServerApplicationOptions(
                options.ProfileName,
                SaveOnStop: options.SaveOnStop));

    private static void WriteStartupPreamble(TextWriter writer)
    {
        foreach (string line in ServerHostStartupBanner.Create("Headless Server"))
            writer.WriteLine(line);
        writer.Flush();
    }

    private static UltraSimScriptStartupResult RunScriptStartup(UltraSimRuntimeRootLayout runtimeRoot, TextWriter writer)
    {
        var startup = new UltraSimScriptStartupService();
        UltraSimScriptStartupResult result = startup.Run(new UltraSimScriptStartupOptions(runtimeRoot));
        IReadOnlyList<ServerApplicationEvent> events = UltraSimScriptStartupLog.CreateEvents(result);
        for (int i = 0; i < events.Count; i++)
        {
            IReadOnlyList<ServerHostLogLine> lines = ServerHostLogFormatter.Format(events[i]);
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                writer.WriteLine(lines[lineIndex].Message);
        }

        writer.Flush();
        return result;
    }
}
