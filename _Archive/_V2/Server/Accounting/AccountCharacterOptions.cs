namespace UltraSim.Server.Accounting;

public sealed record AccountCharacterOptions(
    int MaxCharactersPerAccount = 5,
    bool AutoJoinSingleCharacter = false,
    bool AllowCharacterDeletion = true)
{
    public int ClampedMaxCharacters => Math.Clamp(MaxCharactersPerAccount, 1, 16);
}
