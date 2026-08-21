namespace UltraSim.Game.Input;

/// <summary>Closed engine-neutral action set emitted by the first playable world input path.</summary>
[Flags]
public enum GamePlayerActions : ushort
{
    None = 0,
    Interact = 1 << 0,
    Primary = 1 << 1,
}

/// <summary>
/// One sequenced, normalized player command shared by OfflineDirect and future remote formation.
/// Movement axes use signed Q15 desired velocity: 32767 is full positive intent and -32767 is
/// full negative intent. Authoritative execution integrates that intent using its fixed delta.
/// </summary>
public readonly record struct GamePlayerCommand(
    ulong Sequence,
    short MoveX,
    short MoveZ,
    GamePlayerActions HeldActions,
    GamePlayerActions PressedActions)
{
    public const short MaximumAxisMagnitude = short.MaxValue;
    public const short MinimumAxisMagnitude = -short.MaxValue;
    public const GamePlayerActions KnownActions =
        GamePlayerActions.Interact | GamePlayerActions.Primary;

    public bool IsValid => Sequence != 0 &&
        MoveX >= MinimumAxisMagnitude && MoveZ >= MinimumAxisMagnitude &&
        (HeldActions & ~KnownActions) == 0 &&
        (PressedActions & ~KnownActions) == 0 &&
        (PressedActions & ~HeldActions) == 0;
}
