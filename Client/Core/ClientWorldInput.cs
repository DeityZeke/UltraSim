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

public readonly record struct ClientWorldInputSnapshot(
    GameControlSessionEpoch ControlSession,
    ulong CommandsProduced,
    ulong LastSequence,
    GamePlayerActions LastHeldActions,
    bool LastSampleHadFocus);

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

        GamePlayerActions held = sample.HasFocus ? sample.HeldActions : GamePlayerActions.None;
        // Initial focus may report an already-held key as the first edge, but a
        // refocus must only seed the held baseline. Otherwise alt-tab/focus
        // restoration can manufacture an interaction without a fresh press.
        GamePlayerActions pressed = _hasSample && !_lastFocused && sample.HasFocus
            ? GamePlayerActions.None
            : held & ~_lastHeld;
        NormalizeAxes(
            sample.HasFocus ? sample.MoveX : 0,
            sample.HasFocus ? sample.MoveZ : 0,
            out short moveX,
            out short moveZ);

        command = new GamePlayerCommand(
            _controlSession, _nextSequence, moveX, moveZ, held, pressed);
        _nextSequence = unchecked(_nextSequence + 1);
        _commandsProduced = checked(_commandsProduced + 1);
        _lastHeld = held;
        _lastFocused = sample.HasFocus;
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

    private static void NormalizeAxes(float rawX, float rawZ, out short moveX, out short moveZ)
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

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Client world input sequencing is owner-thread bound.");
    }
}
