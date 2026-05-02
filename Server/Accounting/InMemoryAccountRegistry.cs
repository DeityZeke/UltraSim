namespace UltraSim.Server.Accounting;

public sealed class InMemoryAccountRegistry : IAccountRegistry
{
    private readonly Dictionary<string, ServerAccountRecord> _accountsByUsername = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ulong, ServerAccountRecord> _accountsById = new();
    private ulong _nextAccountId = 1;

    public int Count => _accountsById.Count;

    public ServerAccountRecord CreateAccount(
        string username,
        string passwordHash,
        DateTimeOffset nowUtc,
        AccountStatus status = AccountStatus.Active)
    {
        var account = new ServerAccountRecord(
            _nextAccountId++,
            username,
            passwordHash,
            status,
            nowUtc,
            LastLoginUtc: DateTimeOffset.MinValue,
            KnownRemoteAddresses: []).Normalize();
        if (_accountsByUsername.ContainsKey(account.Username))
            throw new InvalidOperationException($"Account username '{account.Username}' already exists.");

        _accountsByUsername.Add(account.Username, account);
        _accountsById.Add(account.AccountId, account);
        return account;
    }

    public bool TryGetByUsername(string username, out ServerAccountRecord account)
    {
        account = default!;
        if (string.IsNullOrWhiteSpace(username))
            return false;

        return _accountsByUsername.TryGetValue(username.Trim(), out account!);
    }

    public bool TryGetById(ulong accountId, out ServerAccountRecord account)
    {
        return _accountsById.TryGetValue(accountId, out account!);
    }

    public void Update(ServerAccountRecord account)
    {
        ArgumentNullException.ThrowIfNull(account);
        ServerAccountRecord normalized = account.Normalize();
        if (_accountsByUsername.TryGetValue(normalized.Username, out ServerAccountRecord? existing) &&
            existing.AccountId != normalized.AccountId)
        {
            throw new InvalidOperationException($"Account username '{normalized.Username}' already belongs to account {existing.AccountId}.");
        }

        if (_accountsById.TryGetValue(normalized.AccountId, out ServerAccountRecord? previous))
        {
            _accountsByUsername.Remove(previous.Username);
        }

        _accountsByUsername[normalized.Username] = normalized;
        _accountsById[normalized.AccountId] = normalized;
    }
}
