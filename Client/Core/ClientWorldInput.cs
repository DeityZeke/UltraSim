using UltraSim.Game.Input;

namespace UltraSim.Client.Core;

/// <summary>
/// One engine-neutral raw gameplay sample. Engine adapters report independent signed movement axes,
/// current held semantic actions, and focus; normalization and edge detection happen in Client Core.
/// </summary>
public readonly record struct ClientWorldInputSample(
    float MoveX,
    float MoveZ,
    GamePlayerActions HeldActions,
    bool HasFocus)
{
    public bool IsValid => float.IsFinite(MoveX) && float.IsFinite(MoveZ) &&
        (HeldActions & ~GamePlayerCommand.KnownActions) == 0;
}

/// <summary>
/// Engine-neutral control-profile output ready for sequencing. The profile decides whether this
/// command leaves locomotion unchanged, stops it, or replaces it with Direct/Destination state.
/// </summary>
public readonly record struct ClientWorldCommandIntent(
    GameLocomotionUpdate Locomotion,
    GamePlayerActions HeldActions,
    bool HasFocus)
{
    public bool IsValid => Locomotion.IsValid &&
        (HeldActions & ~GamePlayerCommand.KnownActions) == 0;
}

public readonly record struct ClientWorldInputSnapshot(
    GameControlSessionEpoch ControlSession,
    ulong CommandsProduced,
    ulong LastSequence,
    GamePlayerActions LastHeldActions,
    bool LastSampleHadFocus);

/// <summary>Shared deterministic input-vector canonicalization for concrete client profiles.</summary>
public static class ClientWorldInputMath
{
    public static bool TryCreateDirect(float rawX, float rawZ, out GameLocomotionUpdate locomotion)
    {
        if (!float.IsFinite(rawX) || !float.IsFinite(rawZ))
        {
            locomotion = default;
            return false;
        }
        NormalizeAxes(rawX, rawZ, out short x, out short z);
        if (x == 0 && z == 0)
        {
            locomotion = GameLocomotionUpdate.Stop;
            return true;
        }
        locomotion = GameLocomotionUpdate.CreateDirect(x, z);
        return true;
    }

    internal static void NormalizeAxes(float rawX, float rawZ, out short moveX, out short moveZ)
    {
        double x = rawX;
        double z = rawZ;
        double magnitudeSquared = x * x + z * z;
        if (magnitudeSquared > 1d)
        {
            double inverse = 1d / Math.Sqrt(magnitudeSquared);
            x *= inverse;
            z *= inverse;
        }

        moveX = QuantizeAxis(x);
        moveZ = QuantizeAxis(z);
        ClampToQuantizedUnitCircle(ref moveX, ref moveZ);
    }

    private static void ClampToQuantizedUnitCircle(ref short x, ref short z)
    {
        long maximumSquared =
            (long)GamePlayerCommand.MaximumAxisMagnitude * GamePlayerCommand.MaximumAxisMagnitude;
        while ((long)x * x + (long)z * z > maximumSquared)
        {
            int absoluteX = Math.Abs((int)x);
            int absoluteZ = Math.Abs((int)z);
            if (absoluteX >= absoluteZ)
                x = checked((short)(x - Math.Sign(x)));
            if (absoluteZ >= absoluteX)
                z = checked((short)(z - Math.Sign(z)));
        }
    }

    private static short QuantizeAxis(double value)
    {
        value = Math.Clamp(value, -1d, 1d);
        int quantized = checked((int)Math.Round(
            value * GamePlayerCommand.MaximumAxisMagnitude,
            MidpointRounding.AwayFromZero));
        return checked((short)quantized);
    }
}

/// <summary>
/// Owner-thread input normalizer and sequencer. Physical bindings and raw device capture remain in
/// the engine adapter; both OfflineDirect and future remote paths consume the same command value.
/// </summary>
public sealed class ClientWorldInputSequencer
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly GameControlSessionEpoch _controlSession;
    private ulong _nextSequence;
    private ulong _commandsProduced;
    private GamePlayerActions _lastHeld;
    private bool _lastFocused;
    private bool _hasSample;

    public ClientWorldInputSequencer(ulong firstSequence = 1)
        : this(GameControlSessionEpoch.Create(), firstSequence)
    {
    }

    public ClientWorldInputSequencer(
        GameControlSessionEpoch controlSession,
        ulong firstSequence = 1)
    {
        if (!controlSession.IsValid)
            throw new ArgumentException("A valid control-session epoch is required.", nameof(controlSession));
        if (firstSequence == 0)
            throw new ArgumentOutOfRangeException(nameof(firstSequence));
        _controlSession = controlSession;
        _nextSequence = firstSequence;
    }

    public GameControlSessionEpoch ControlSession => _controlSession;

    public bool TryCreateCommand(
        in ClientWorldInputSample sample,
        out GamePlayerCommand command)
    {
        EnsureOwner();
        if (!sample.IsValid || _nextSequence == 0)
        {
            command = default;
            return false;
        }

        ClientWorldInputMath.NormalizeAxes(
            sample.HasFocus ? sample.MoveX : 0,
            sample.HasFocus ? sample.MoveZ : 0,
            out short moveX,
            out short moveZ);
        GameLocomotionUpdate locomotion = moveX == 0 && moveZ == 0
            ? GameLocomotionUpdate.Stop
            : GameLocomotionUpdate.CreateDirect(moveX, moveZ);
        return TryCreateCommandCore(
            locomotion, sample.HeldActions, sample.HasFocus, out command);
    }

    public bool TryCreateSemanticCommand(
        in ClientWorldCommandIntent intent,
        out GamePlayerCommand command)
    {
        EnsureOwner();
        if (!intent.IsValid || _nextSequence == 0)
        {
            command = default;
            return false;
        }
        return TryCreateCommandCore(
            intent.Locomotion, intent.HeldActions, intent.HasFocus, out command);
    }

    private bool TryCreateCommandCore(
        in GameLocomotionUpdate requestedLocomotion,
        GamePlayerActions requestedHeld,
        bool hasFocus,
        out GamePlayerCommand command)
    {
        GamePlayerActions held = hasFocus ? requestedHeld : GamePlayerActions.None;
        GameLocomotionUpdate locomotion = hasFocus
            ? requestedLocomotion
            : GameLocomotionUpdate.Stop;
        // Initial focus may report an already-held key as the first edge, but a
        // refocus must only seed the held baseline. Otherwise alt-tab/focus
        // restoration can manufacture an interaction without a fresh press.
        GamePlayerActions pressed = _hasSample && !_lastFocused && hasFocus
            ? GamePlayerActions.None
            : held & ~_lastHeld;

        command = new GamePlayerCommand(
            _controlSession, _nextSequence, locomotion, held, pressed);
        _nextSequence = unchecked(_nextSequence + 1);
        _commandsProduced = checked(_commandsProduced + 1);
        _lastHeld = held;
        _lastFocused = hasFocus;
        _hasSample = true;
        return true;
    }

    public ClientWorldInputSnapshot Snapshot()
    {
        EnsureOwner();
        return new ClientWorldInputSnapshot(
            _controlSession,
            _commandsProduced,
            _commandsProduced == 0 ? 0 : unchecked(_nextSequence - 1),
            _lastHeld,
            _lastFocused);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Client world input sequencing is owner-thread bound.");
    }
}
