namespace UltraSim.Server.Accounting;

public sealed class InMemoryAccountAccessAuditSink : IAccountAccessAuditSink
{
    private readonly List<AccountAccessAuditEntry> _entries = new();

    public IReadOnlyList<AccountAccessAuditEntry> Entries => _entries;

    public void Record(AccountAccessAuditEntry entry)
    {
        _entries.Add(entry ?? throw new ArgumentNullException(nameof(entry)));
    }
}
