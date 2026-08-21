using System.Runtime.InteropServices;

namespace UltraSim.Game.Input;

/// <summary>
/// Operational identity of one actor-control binding. It is session lineage, not durable world state.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct GameControlSessionEpoch :
    IEquatable<GameControlSessionEpoch>,
    IComparable<GameControlSessionEpoch>
{
    private static long s_next;

    public GameControlSessionEpoch(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;

    public static GameControlSessionEpoch Create()
    {
        long value = Interlocked.Increment(ref s_next);
        if (value <= 0)
        {
            throw new InvalidOperationException(
                "The process control-session epoch identity space is exhausted.");
        }
        return new GameControlSessionEpoch(checked((ulong)value));
    }

    public int CompareTo(GameControlSessionEpoch other) => Value.CompareTo(other.Value);
    public bool Equals(GameControlSessionEpoch other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is GameControlSessionEpoch other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid ? $"ControlEpoch({Value})" : "ControlEpoch(Invalid)";
    public static bool operator ==(GameControlSessionEpoch left, GameControlSessionEpoch right) =>
        left.Equals(right);
    public static bool operator !=(GameControlSessionEpoch left, GameControlSessionEpoch right) =>
        !left.Equals(right);
}

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
/// Movement axes are a complete, superseding world-space Direct intent in signed Q15. Their vector
/// must remain inside the unit circle. Zero/zero means no current Direct locomotion request.
/// </summary>
public readonly record struct GamePlayerCommand(
    GameControlSessionEpoch ControlSession,
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

    public bool IsValid => ControlSession.IsValid && Sequence != 0 &&
        MoveX >= MinimumAxisMagnitude && MoveZ >= MinimumAxisMagnitude &&
        (long)MoveX * MoveX + (long)MoveZ * MoveZ <=
            (long)MaximumAxisMagnitude * MaximumAxisMagnitude &&
        (HeldActions & ~KnownActions) == 0 &&
        (PressedActions & ~KnownActions) == 0 &&
        (PressedActions & ~HeldActions) == 0;
}
