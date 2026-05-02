namespace UltraSim.Server.Accounting;

public sealed record ServerAccountRecord(
    ulong AccountId,
    string Username,
    string PasswordHash,
    AccountStatus Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastLoginUtc,
    string[] KnownRemoteAddresses,
    bool IsAdmin = false,
    string? StatusReason = null)
{
    public ServerAccountRecord Normalize()
    {
        string username = string.IsNullOrWhiteSpace(Username)
            ? throw new ArgumentException("Account username is required.", nameof(Username))
            : Username.Trim();
        string hash = string.IsNullOrWhiteSpace(PasswordHash)
            ? throw new ArgumentException("Account password hash is required.", nameof(PasswordHash))
            : PasswordHash.Trim();
        string[] known = KnownRemoteAddresses
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        return this with
        {
            Username = username,
            PasswordHash = hash,
            KnownRemoteAddresses = known
        };
    }

    public ServerAccountRecord WithLoginSuccess(DateTimeOffset nowUtc, string remoteAddress)
    {
        string[] known = AppendKnownRemoteAddress(remoteAddress);
        return this with
        {
            LastLoginUtc = nowUtc,
            KnownRemoteAddresses = known
        };
    }

    private string[] AppendKnownRemoteAddress(string remoteAddress)
    {
        if (string.IsNullOrWhiteSpace(remoteAddress))
            return KnownRemoteAddresses.ToArray();

        string normalized = remoteAddress.Trim();
        var values = new List<string>(KnownRemoteAddresses.Length + 1) { normalized };
        for (int i = 0; i < KnownRemoteAddresses.Length && values.Count < 8; i++)
        {
            string existing = KnownRemoteAddresses[i];
            if (!string.Equals(existing, normalized, StringComparison.Ordinal))
                values.Add(existing);
        }

        return values.ToArray();
    }
}
