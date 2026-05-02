namespace UltraSim.Server.Accounting;

public sealed class InMemoryAccountAuditSink : IAccountAuditSink
{
    private readonly List<AccountLoginAuditEntry> _entries = new();

    public IReadOnlyList<AccountLoginAuditEntry> Entries => _entries;

    public void Record(AccountLoginAuditEntry entry)
    {
        _entries.Add(entry ?? throw new ArgumentNullException(nameof(entry)));
    }
}
