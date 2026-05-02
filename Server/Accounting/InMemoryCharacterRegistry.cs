namespace UltraSim.Server.Accounting;

public sealed class InMemoryCharacterRegistry : ICharacterRegistry
{
    private readonly Dictionary<ulong, ServerCharacterRecord> _charactersById = new();
    private readonly Dictionary<ulong, List<ulong>> _characterIdsByAccount = new();
    private ulong _nextCharacterId = 1;

    public int CharacterCount => _charactersById.Count;

    public ServerCharacterRecord CreateCharacter(
        ulong accountId,
        int slotIndex,
        string name,
        string prefabKey,
        DateTimeOffset nowUtc,
        CharacterPositionSnapshot position)
    {
        var character = new ServerCharacterRecord(
            _nextCharacterId++,
            accountId,
            slotIndex,
            name,
            prefabKey,
            nowUtc,
            nowUtc,
            position).Normalize();

        _charactersById.Add(character.CharacterId, character);
        if (!_characterIdsByAccount.TryGetValue(accountId, out List<ulong>? ids))
        {
            ids = [];
            _characterIdsByAccount.Add(accountId, ids);
        }

        ids.Add(character.CharacterId);
        return character;
    }

    public bool TryGetCharacter(ulong characterId, out ServerCharacterRecord character)
    {
        return _charactersById.TryGetValue(characterId, out character!);
    }

    public IReadOnlyList<ServerCharacterRecord> GetCharactersForAccount(ulong accountId, IReadOnlyList<ulong> slotOrder)
    {
        if (!_characterIdsByAccount.TryGetValue(accountId, out List<ulong>? ids))
            return [];

        var byId = new Dictionary<ulong, ServerCharacterRecord>(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            if (_charactersById.TryGetValue(ids[i], out ServerCharacterRecord? character))
                byId[character.CharacterId] = character;
        }

        var result = new List<ServerCharacterRecord>(byId.Count);
        for (int i = 0; i < slotOrder.Count; i++)
        {
            ulong characterId = slotOrder[i];
            if (characterId != 0 && byId.Remove(characterId, out ServerCharacterRecord? character))
                result.Add(character);
        }

        foreach (ServerCharacterRecord character in byId.Values.OrderBy(static value => value.SlotIndex))
        {
            result.Add(character);
        }

        return result;
    }

    public IReadOnlyList<ServerCharacterRecord> GetCharactersSnapshot()
    {
        return _charactersById.Values
            .OrderBy(static character => character.AccountId)
            .ThenBy(static character => character.SlotIndex)
            .ThenBy(static character => character.CharacterId)
            .ToArray();
    }

    public void UpdateCharacter(ServerCharacterRecord character)
    {
        ArgumentNullException.ThrowIfNull(character);
        ServerCharacterRecord normalized = character.Normalize();
        if (!_charactersById.ContainsKey(normalized.CharacterId))
            throw new InvalidOperationException($"Character id '{normalized.CharacterId}' is not registered.");

        _charactersById[normalized.CharacterId] = normalized;
    }

    public bool DeleteCharacter(ulong characterId)
    {
        if (!_charactersById.Remove(characterId, out ServerCharacterRecord? character))
            return false;

        if (_characterIdsByAccount.TryGetValue(character.AccountId, out List<ulong>? ids))
        {
            ids.Remove(characterId);
            if (ids.Count == 0)
                _characterIdsByAccount.Remove(character.AccountId);
        }

        return true;
    }

    public void LoadSnapshot(IEnumerable<ServerCharacterRecord> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        _charactersById.Clear();
        _characterIdsByAccount.Clear();
        _nextCharacterId = 1;

        foreach (ServerCharacterRecord character in characters)
        {
            ServerCharacterRecord normalized = character.Normalize();
            if (_charactersById.ContainsKey(normalized.CharacterId))
                throw new InvalidOperationException($"Duplicate character id '{normalized.CharacterId}' in character snapshot.");

            _charactersById.Add(normalized.CharacterId, normalized);
            if (!_characterIdsByAccount.TryGetValue(normalized.AccountId, out List<ulong>? ids))
            {
                ids = [];
                _characterIdsByAccount.Add(normalized.AccountId, ids);
            }

            ids.Add(normalized.CharacterId);
            _nextCharacterId = Math.Max(_nextCharacterId, normalized.CharacterId + 1);
        }
    }
}
