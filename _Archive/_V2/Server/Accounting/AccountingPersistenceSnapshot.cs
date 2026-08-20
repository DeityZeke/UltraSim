namespace UltraSim.Server.Accounting;

public sealed record AccountingPersistenceSnapshot(
    IReadOnlyList<ServerAccountRecord> Accounts,
    IReadOnlyList<ServerCharacterRecord> Characters)
{
    public static AccountingPersistenceSnapshot Empty { get; } = new([], []);

    public static AccountingPersistenceSnapshot Capture(
        IAccountRegistry accounts,
        ICharacterRegistry characters)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(characters);
        return new AccountingPersistenceSnapshot(
            accounts.GetAccountsSnapshot(),
            characters.GetCharactersSnapshot()).Normalize();
    }

    public AccountingPersistenceSnapshot Normalize()
    {
        ServerAccountRecord[] normalizedAccounts = Accounts
            .Select(static account => account.Normalize())
            .OrderBy(static account => account.AccountId)
            .ToArray();
        ServerCharacterRecord[] normalizedCharacters = Characters
            .Select(static character => character.Normalize())
            .OrderBy(static character => character.AccountId)
            .ThenBy(static character => character.SlotIndex)
            .ThenBy(static character => character.CharacterId)
            .ToArray();

        ValidateAccounts(normalizedAccounts);
        ValidateCharacters(normalizedAccounts, normalizedCharacters);

        return new AccountingPersistenceSnapshot(normalizedAccounts, normalizedCharacters);
    }

    private static void ValidateAccounts(IReadOnlyList<ServerAccountRecord> accounts)
    {
        var ids = new HashSet<ulong>();
        var usernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < accounts.Count; i++)
        {
            ServerAccountRecord account = accounts[i];
            if (!ids.Add(account.AccountId))
                throw new InvalidOperationException($"Duplicate account id '{account.AccountId}' in accounting snapshot.");
            if (!usernames.Add(account.Username))
                throw new InvalidOperationException($"Duplicate account username '{account.Username}' in accounting snapshot.");
        }
    }

    private static void ValidateCharacters(
        IReadOnlyList<ServerAccountRecord> accounts,
        IReadOnlyList<ServerCharacterRecord> characters)
    {
        var accountIds = accounts.Select(static account => account.AccountId).ToHashSet();
        var characterIds = new HashSet<ulong>();
        for (int i = 0; i < characters.Count; i++)
        {
            ServerCharacterRecord character = characters[i];
            if (!characterIds.Add(character.CharacterId))
                throw new InvalidOperationException($"Duplicate character id '{character.CharacterId}' in accounting snapshot.");
            if (!accountIds.Contains(character.AccountId))
                throw new InvalidOperationException($"Character '{character.CharacterId}' references missing account '{character.AccountId}'.");
        }

        foreach (ServerAccountRecord account in accounts)
        {
            for (int i = 0; i < account.CharacterSlots.Length; i++)
            {
                ulong characterId = account.CharacterSlots[i];
                if (characterId == 0)
                    continue;
                if (!characterIds.Contains(characterId))
                    throw new InvalidOperationException($"Account '{account.AccountId}' slot '{i}' references missing character '{characterId}'.");
            }
        }
    }
}
