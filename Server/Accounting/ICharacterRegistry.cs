namespace UltraSim.Server.Accounting;

public interface ICharacterRegistry
{
    int CharacterCount { get; }

    ServerCharacterRecord CreateCharacter(
        ulong accountId,
        int slotIndex,
        string name,
        string prefabKey,
        DateTimeOffset nowUtc,
        CharacterPositionSnapshot position);

    bool TryGetCharacter(ulong characterId, out ServerCharacterRecord character);

    IReadOnlyList<ServerCharacterRecord> GetCharactersForAccount(ulong accountId, IReadOnlyList<ulong> slotOrder);

    IReadOnlyList<ServerCharacterRecord> GetCharactersSnapshot();

    void LoadSnapshot(IEnumerable<ServerCharacterRecord> characters);

    void UpdateCharacter(ServerCharacterRecord character);

    bool DeleteCharacter(ulong characterId);
}
