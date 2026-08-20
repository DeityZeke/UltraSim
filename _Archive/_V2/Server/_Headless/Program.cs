using UltraSim.Server.Headless;

if (!HeadlessServerArgumentParser.TryParse(args, out HeadlessServerShellOptions options, out string? error, out bool showHelp))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine();
    Console.Error.WriteLine(HeadlessServerArgumentParser.GetUsage());
    return 1;
}

if (showHelp)
{
    Console.WriteLine(HeadlessServerArgumentParser.GetUsage());
    return 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

var shell = new HeadlessServerShell(options);
return await shell.RunAsync(cts.Token);
