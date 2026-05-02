namespace UltraSim.Server.Accounting;

public sealed record ServerCharacterRecord(
    ulong CharacterId,
    ulong AccountId,
    int SlotIndex,
    string Name,
    string PrefabKey,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastPlayedUtc,
    CharacterPositionSnapshot Position)
{
    public ServerCharacterRecord Normalize()
    {
        if (CharacterId == 0)
            throw new ArgumentException("Character id is required.", nameof(CharacterId));
        if (AccountId == 0)
            throw new ArgumentException("Character account id is required.", nameof(AccountId));
        if (SlotIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(SlotIndex));

        string name = string.IsNullOrWhiteSpace(Name)
            ? throw new ArgumentException("Character name is required.", nameof(Name))
            : Name.Trim();
        string prefabKey = string.IsNullOrWhiteSpace(PrefabKey)
            ? throw new ArgumentException("Character prefab key is required.", nameof(PrefabKey))
            : PrefabKey.Trim();

        return this with
        {
            Name = name,
            PrefabKey = prefabKey
        };
    }

    public ServerCharacterRecord WithSnapshot(
        DateTimeOffset nowUtc,
        string prefabKey,
        CharacterPositionSnapshot position)
    {
        return (this with
        {
            PrefabKey = prefabKey,
            LastPlayedUtc = nowUtc,
            Position = position
        }).Normalize();
    }
}
