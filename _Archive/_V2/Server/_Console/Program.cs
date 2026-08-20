using UltraSim.Server.Console;

if (!ConsoleServerArgumentParser.TryParse(args, out ConsoleServerShellOptions options, out string? error, out bool showHelp))
{
    global::System.Console.Error.WriteLine(error);
    global::System.Console.Error.WriteLine();
    global::System.Console.Error.WriteLine(ConsoleServerArgumentParser.GetUsage());
    return 1;
}

if (showHelp)
{
    global::System.Console.WriteLine(ConsoleServerArgumentParser.GetUsage());
    return 0;
}

using var cts = new CancellationTokenSource();
global::System.Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

var shell = new ConsoleServerShell(options);
return await shell.RunAsync(cts.Token);
