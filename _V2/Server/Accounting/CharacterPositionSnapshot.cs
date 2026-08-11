namespace UltraSim.Server.Accounting;

public readonly record struct CharacterPositionSnapshot(
    float X,
    float Y,
    float Z,
    float YawRadians)
{
    public static CharacterPositionSnapshot Origin { get; } = new(0f, 0f, 0f, 0f);
}
