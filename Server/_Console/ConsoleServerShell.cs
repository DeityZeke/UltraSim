using System.Collections.Concurrent;
using System.Diagnostics;
using UltraSim.Server.Hosting;
using UltraSim.Server.Network;
using UltraSim.Server.Scripting;

namespace UltraSim.Server.Console;

public sealed class ConsoleServerShell
{
    private readonly Func<ConsoleServerShellOptions, IServerApplication> _applicationFactory;
    private readonly TextReader _stdin;
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly ConcurrentQueue<ConsoleShellDirective> _pendingDirectives = new();

    public ConsoleServerShell(
        ConsoleServerShellOptions options,
        TextReader? stdin = null,
        TextWriter? stdout = null,
        TextWriter? stderr = null,
        Func<ConsoleServerShellOptions, IServerApplication>? applicationFactory = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _stdin = stdin ?? global::System.Console.In;
        _stdout = stdout ?? global::System.Console.Out;
        _stderr = stderr ?? global::System.Console.Error;
        _applicationFactory = applicationFactory ?? CreateDefaultApplication;
    }

    public ConsoleServerShellOptions Options { get; }
    public DefaultUltraSimScriptContext? ScriptContext { get; private set; }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        UltraSimRuntimeRootLayout runtimeRoot = UltraSimRuntimeRootResolver.Resolve(Options.RuntimeRootPath);
        UltraSimRuntimeTemplate.Ensure(runtimeRoot);
        WriteStartupPreamble(_stdout);
        UltraSimScriptStartupResult scriptStartup = RunScriptStartup(runtimeRoot, _stdout);
        ScriptContext = scriptStartup.Context;
        if (!scriptStartup.Succeeded)
            return 1;

        IServerApplication application = _applicationFactory(Options);

        using var runtime = new HostedServerRuntime(
            application,
            new ServerHostLoopOptions(
                TimeSpan.FromMilliseconds(Options.TickMilliseconds),
                Options.MaxCatchUpTicksPerPump),
            new IServerHostEventSink[] { new ConsoleServerEventSink(_stdout, _stderr) });

        if (Options.AutoStart)
            runtime.StartImmediately();

        Task? inputTask = null;
        if (Options.EnableInput)
            inputTask = Task.Run(ReadInputLoop, CancellationToken.None);

        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeSpan previousElapsed = stopwatch.Elapsed;
        bool observedRunning = runtime.State == ServerApplicationState.Running;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TimeSpan currentElapsed = stopwatch.Elapsed;
                TimeSpan delta = currentElapsed - previousElapsed;
                previousElapsed = currentElapsed;

                DrainPendingDirectives(runtime);
                runtime.Pump(delta);

                if (runtime.State == ServerApplicationState.Running)
                    observedRunning = true;

                if (runtime.State == ServerApplicationState.Faulted)
                    break;

                if (Options.RunFor.HasValue && currentElapsed >= Options.RunFor.Value)
                    break;

                if (observedRunning && runtime.State == ServerApplicationState.Stopped)
                    break;

                if (!Options.EnableInput && Options.AutoStart && runtime.State == ServerApplicationState.Stopped)
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

        if (inputTask is not null && inputTask.IsCompleted)
            await inputTask.ConfigureAwait(false);

        return runtime.State == ServerApplicationState.Faulted ? 1 : 0;
    }

    private void ReadInputLoop()
    {
        while (true)
        {
            string? line = _stdin.ReadLine();
            if (line is null)
                break;

            _pendingDirectives.Enqueue(ConsoleShellCommandParser.Parse(line));
        }
    }

    private void DrainPendingDirectives(HostedServerRuntime runtime)
    {
        while (_pendingDirectives.TryDequeue(out ConsoleShellDirective directive))
        {
            switch (directive.Kind)
            {
                case ConsoleShellDirectiveKind.None:
                    break;

                case ConsoleShellDirectiveKind.Help:
                    _stdout.WriteLine(ConsoleServerArgumentParser.GetUsage());
                    _stdout.Flush();
                    break;

                case ConsoleShellDirectiveKind.Invalid:
                    _stderr.WriteLine(directive.Message);
                    _stderr.Flush();
                    break;

                case ConsoleShellDirectiveKind.ServerCommand:
                    runtime.TryExecuteCommand(directive.Command, out ServerCommandResult result);
                    _stdout.WriteLine($"> {directive.Command.Kind}: {result.Kind} - {result.Message}");
                    _stdout.Flush();
                    break;
            }
        }
    }

    private IServerApplication CreateDefaultApplication(ConsoleServerShellOptions options) =>
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
                PersistenceRoot: ScriptContext!.RuntimeRoot.SavesPath,
                PersistenceBackupsRoot: ScriptContext.RuntimeRoot.BackupsPath,
                PersistenceBackupStorageMode: options.BackupStorageMode,
                PersistenceBackupCount: options.BackupCount,
                PersistenceBackupZstdCompressionLevel: options.BackupZstdCompressionLevel))
            : new ServerApplication(new ServerApplicationOptions(
                options.ProfileName,
                SaveOnStop: options.SaveOnStop));

    private static void WriteStartupPreamble(TextWriter writer)
    {
        foreach (string line in ServerHostStartupBanner.Create("Console Server"))
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
