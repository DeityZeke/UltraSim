namespace UltraSim.Server.Accounting;

public interface IAccountRegistry
{
    int Count { get; }

    ServerAccountRecord CreateAccount(
        string username,
        string passwordHash,
        DateTimeOffset nowUtc,
        AccountStatus status = AccountStatus.Active);

    bool TryGetByUsername(string username, out ServerAccountRecord account);

    bool TryGetById(ulong accountId, out ServerAccountRecord account);

    IReadOnlyList<ServerAccountRecord> GetAccountsSnapshot();

    void LoadSnapshot(IEnumerable<ServerAccountRecord> accounts);

    void Update(ServerAccountRecord account);
}
