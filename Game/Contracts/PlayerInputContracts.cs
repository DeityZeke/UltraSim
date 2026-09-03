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
    Jump = 1 << 2,
}

/// <summary>Command-level mutation of the one retained authoritative locomotion state.</summary>
public enum GameLocomotionUpdateKind : byte
{
    Unchanged = 0,
    Stop,
    Direct,
    Destination,
}

/// <summary>
/// Canonical engine-neutral locomotion update. <see cref="Unchanged"/> is command vocabulary,
/// not a fourth retained locomotion mode.
/// </summary>
public readonly record struct GameLocomotionUpdate(
    GameLocomotionUpdateKind Kind,
    short DirectX,
    short DirectZ,
    long DestinationXMillimeters,
    long DestinationZMillimeters)
{
    public static GameLocomotionUpdate Unchanged => default;
    public static GameLocomotionUpdate Stop => new(GameLocomotionUpdateKind.Stop, 0, 0, 0, 0);

    public static GameLocomotionUpdate CreateDirect(short x, short z) =>
        new(GameLocomotionUpdateKind.Direct, x, z, 0, 0);

    public static GameLocomotionUpdate CreateDestination(
        long globalXMillimeters,
        long globalZMillimeters) =>
        new(GameLocomotionUpdateKind.Destination, 0, 0,
            globalXMillimeters, globalZMillimeters);

    public bool IsValid => Kind switch
    {
        GameLocomotionUpdateKind.Unchanged or GameLocomotionUpdateKind.Stop =>
            DirectX == 0 && DirectZ == 0 &&
            DestinationXMillimeters == 0 && DestinationZMillimeters == 0,
        GameLocomotionUpdateKind.Direct =>
            (DirectX != 0 || DirectZ != 0) &&
            DirectX >= GamePlayerCommand.MinimumAxisMagnitude &&
            DirectZ >= GamePlayerCommand.MinimumAxisMagnitude &&
            (long)DirectX * DirectX + (long)DirectZ * DirectZ <=
                (long)GamePlayerCommand.MaximumAxisMagnitude *
                GamePlayerCommand.MaximumAxisMagnitude &&
            DestinationXMillimeters == 0 && DestinationZMillimeters == 0,
        GameLocomotionUpdateKind.Destination => DirectX == 0 && DirectZ == 0,
        _ => false,
    };
}

/// <summary>
/// One sequenced, normalized player command shared by OfflineDirect and future remote formation.
/// Locomotion is an explicit command mutation: unchanged, stop, world-space signed-Q15 Direct, or
/// canonical global-X/Z Destination. Adjacent actions do not implicitly mutate locomotion.
/// </summary>
public readonly record struct GamePlayerCommand(
    GameControlSessionEpoch ControlSession,
    ulong Sequence,
    GameLocomotionUpdate Locomotion,
    GamePlayerActions HeldActions,
    GamePlayerActions PressedActions)
{
    public const short MaximumAxisMagnitude = short.MaxValue;
    public const short MinimumAxisMagnitude = -short.MaxValue;
    public const GamePlayerActions KnownActions =
        GamePlayerActions.Interact | GamePlayerActions.Primary | GamePlayerActions.Jump;

    public bool IsValid => ControlSession.IsValid && Sequence != 0 && Locomotion.IsValid &&
        (HeldActions & ~KnownActions) == 0 &&
        (PressedActions & ~KnownActions) == 0 &&
        (PressedActions & ~HeldActions) == 0;
}
