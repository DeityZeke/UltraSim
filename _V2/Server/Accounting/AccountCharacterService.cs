namespace UltraSim.Server.Accounting;

public sealed class AccountCharacterService
{
    private readonly IAccountRegistry _accounts;
    private readonly ICharacterRegistry _characters;
    private readonly AccountCharacterOptions _options;

    public AccountCharacterService(
        IAccountRegistry accounts,
        ICharacterRegistry characters,
        AccountCharacterOptions? options = null)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        _options = options ?? new AccountCharacterOptions();
    }

    public IReadOnlyList<ServerCharacterRecord> GetCharactersForAccount(ulong accountId)
    {
        return !_accounts.TryGetById(accountId, out ServerAccountRecord account)
            ? []
            : _characters.GetCharactersForAccount(accountId, account.CharacterSlots);
    }

    public bool TryGetAutoJoinCharacter(ulong accountId, out ServerCharacterRecord character)
    {
        character = default!;
        if (!_options.AutoJoinSingleCharacter || _options.ClampedMaxCharacters != 1)
            return false;

        IReadOnlyList<ServerCharacterRecord> characters = GetCharactersForAccount(accountId);
        if (characters.Count != 1)
            return false;

        character = characters[0];
        return true;
    }

    public CharacterCreateResult TryCreateCharacter(
        ulong accountId,
        string name,
        string prefabKey,
        DateTimeOffset nowUtc,
        CharacterPositionSnapshot? position = null,
        string biologicalSex = "male")
    {
        if (!_accounts.TryGetById(accountId, out ServerAccountRecord account))
            return CharacterCreateResult.Reject("Account not found.");

        if (account.Status != AccountStatus.Active)
            return CharacterCreateResult.Reject("Account is not active.");

        if (!TryValidateCharacterName(name, out string error))
            return CharacterCreateResult.Reject(error);

        if (!TryValidatePrefabKey(prefabKey, out error))
            return CharacterCreateResult.Reject(error);

        int maxCharacters = _options.ClampedMaxCharacters;
        int slotIndex = account.FirstEmptySlot(maxCharacters);
        if (slotIndex < 0)
            return CharacterCreateResult.Reject("Character slots full.");

        ServerCharacterRecord character = _characters.CreateCharacter(
            account.AccountId,
            slotIndex,
            name.Trim(),
            prefabKey.Trim(),
            nowUtc,
            position ?? CharacterPositionSnapshot.Origin,
            biologicalSex);
        ServerAccountRecord updatedAccount = account.WithCharacterInSlot(
            slotIndex,
            character.CharacterId,
            maxCharacters);
        _accounts.Update(updatedAccount);
        return CharacterCreateResult.Success(updatedAccount, character);
    }

    public bool TryDeleteCharacter(ulong accountId, ulong characterId, out ServerAccountRecord? updatedAccount)
    {
        updatedAccount = null;
        if (!_options.AllowCharacterDeletion)
            return false;

        if (!_accounts.TryGetById(accountId, out ServerAccountRecord account))
            return false;

        if (!account.TryGetSlotIndex(characterId, out _))
            return false;

        if (!_characters.DeleteCharacter(characterId))
            return false;

        updatedAccount = account.WithCharacterRemoved(characterId);
        _accounts.Update(updatedAccount);
        return true;
    }

    public bool TryUpdateCharacterSnapshot(
        ulong characterId,
        DateTimeOffset nowUtc,
        string prefabKey,
        CharacterPositionSnapshot position,
        out ServerCharacterRecord updatedCharacter)
    {
        updatedCharacter = default!;
        if (!_characters.TryGetCharacter(characterId, out ServerCharacterRecord character))
            return false;

        updatedCharacter = character.WithSnapshot(nowUtc, prefabKey, position);
        _characters.UpdateCharacter(updatedCharacter);
        return true;
    }

    private static bool TryValidateCharacterName(string name, out string error)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length < 3)
        {
            error = "Character name must be a minimum of 3 characters.";
            return false;
        }

        if (trimmed.Length > 32)
        {
            error = "Character name cannot exceed 32 characters.";
            return false;
        }

        for (int i = 0; i < trimmed.Length; i++)
        {
            if (!char.IsLetterOrDigit(trimmed[i]) && trimmed[i] != ' ')
            {
                error = "Character name cannot contain special characters.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidatePrefabKey(string prefabKey, out string error)
    {
        string trimmed = prefabKey?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            error = "Character prefab key is required.";
            return false;
        }

        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            bool valid =
                c is >= 'a' and <= 'z' ||
                c is >= 'A' and <= 'Z' ||
                c is >= '0' and <= '9' ||
                c is '.' or '_' or '-' or ':' or '/';
            if (!valid)
            {
                error = "Character prefab key is invalid.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
