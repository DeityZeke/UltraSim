using UltraSim.Server.Hosting;

namespace UltraSim.Server.Console;

public sealed class ConsoleServerEventSink : IServerHostEventSink
{
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;

    public ConsoleServerEventSink(TextWriter stdout, TextWriter stderr)
    {
        _stdout = stdout ?? throw new ArgumentNullException(nameof(stdout));
        _stderr = stderr ?? throw new ArgumentNullException(nameof(stderr));
    }

    public void OnEvent(ServerApplicationEvent evt)
    {
        string line = $"[{evt.TimestampUtc:O}] {evt.Kind}: {evt.Message}";
        TextWriter writer = evt.Kind == ServerApplicationEventKind.Faulted ? _stderr : _stdout;
        writer.WriteLine(line);
        writer.Flush();
    }
}
