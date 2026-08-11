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
        IReadOnlyList<ServerHostLogLine> lines = ServerHostLogFormatter.Format(evt);
        for (int i = 0; i < lines.Count; i++)
        {
            ServerHostLogLine line = lines[i];
            TextWriter writer = line.IsError ? _stderr : _stdout;
            writer.WriteLine(line.Message);
            writer.Flush();
        }
    }
}
