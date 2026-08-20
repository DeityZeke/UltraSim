namespace UltraSim.Server;

public sealed class InMemoryServerCommandAuditSink : IServerCommandAuditSink
{
    private readonly List<ServerCommandAuditEntry> _entries = new();

    public IReadOnlyList<ServerCommandAuditEntry> Entries => _entries;

    public void Record(ServerCommandAuditEntry entry)
    {
        _entries.Add(entry ?? throw new ArgumentNullException(nameof(entry)));
    }
}
