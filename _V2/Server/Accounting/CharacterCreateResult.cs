namespace UltraSim.Server.Accounting;

public sealed record CharacterCreateResult(
    bool Succeeded,
    ServerAccountRecord? Account,
    ServerCharacterRecord? Character,
    string Error)
{
    public static CharacterCreateResult Success(ServerAccountRecord account, ServerCharacterRecord character)
    {
        return new CharacterCreateResult(true, account, character, string.Empty);
    }

    public static CharacterCreateResult Reject(string error)
    {
        return new CharacterCreateResult(false, null, null, string.IsNullOrWhiteSpace(error) ? "Character creation rejected." : error);
    }
}
