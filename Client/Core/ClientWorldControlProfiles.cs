using UltraSim.Game.Input;

namespace UltraSim.Client.Core;

/// <summary>Engine-neutral horizontal camera basis expressed in authoritative world X/Z axes.</summary>
public readonly record struct ClientHorizontalCameraBasis(
    float RightX,
    float RightZ,
    float ForwardX,
    float ForwardZ)
{
    public bool IsValid =>
        float.IsFinite(RightX) && float.IsFinite(RightZ) &&
        float.IsFinite(ForwardX) && float.IsFinite(ForwardZ) &&
        (double)RightX * RightX + (double)RightZ * RightZ > 0.25d &&
        (double)ForwardX * ForwardX + (double)ForwardZ * ForwardZ > 0.25d;

    public static ClientHorizontalCameraBasis WorldAxes { get; } = new(1, 0, 0, -1);
}

/// <summary>Canonical world-X/Z point nominated by a client raycast; authority validates it.</summary>
public readonly record struct ClientControlWorldPoint(long XMillimeters, long ZMillimeters);

/// <summary>
/// One raw OrbitCommand profile observation. The engine reports button edges and a world point;
/// Core owns hold/double-click/cancel interpretation and produces neutral command intent.
/// </summary>
public readonly record struct ClientOrbitCommandSample(
    float MoveX,
    float MoveZ,
    GamePlayerActions HeldActions,
    bool HasFocus,
    ClientHorizontalCameraBasis CameraBasis,
    long ActorXMillimeters,
    long ActorZMillimeters,
    bool PrimaryDown,
    bool PrimaryPressed,
    bool PrimaryReleased,
    bool SecondaryPressed,
    bool CancelPressed,
    bool PointerWorldPointValid,
    ClientControlWorldPoint PointerWorldPoint,
    long TimestampMilliseconds)
{
    public bool IsValid =>
        float.IsFinite(MoveX) && float.IsFinite(MoveZ) &&
        (HeldActions & ~GamePlayerCommand.KnownActions) == 0 &&
        CameraBasis.IsValid && TimestampMilliseconds >= 0;
}

/// <summary>One profile decision; selection/look remain client semantics adjacent to Game commands.</summary>
public readonly record struct ClientControlProfileDecision(
    ClientWorldCommandIntent Command,
    bool SelectionRequested,
    ClientControlWorldPoint SelectionPoint,
    bool LookRequested,
    ClientControlWorldPoint LookPoint);

/// <summary>Concrete polished T0.10 orbital command grammar; not a universal controller framework.</summary>
public sealed class OrbitCommandProfile
{
    public const int HoldThresholdMilliseconds = 140;
    public const int DoubleClickThresholdMilliseconds = 350;
    public const long PointerSlopMillimeters = 500;

    private bool _primaryCandidate;
    private bool _mouseSteering;
    private bool _keyboardSteering;
    private bool _suppressRelease;
    private long _primaryStartedAt;
    private ClientControlWorldPoint _primaryStart;
    private bool _lastTapValid;
    private long _lastTapAt;
    private ClientControlWorldPoint _lastTap;

    public ClientControlProfileDecision Evaluate(in ClientOrbitCommandSample sample)
    {
        if (!sample.IsValid)
            throw new ArgumentException("A valid OrbitCommand observation is required.", nameof(sample));

        GameLocomotionUpdate locomotion = GameLocomotionUpdate.Unchanged;
        bool selection = false;
        ClientControlWorldPoint selectionPoint = default;
        bool look = sample.PointerWorldPointValid;

        if (!sample.HasFocus || sample.SecondaryPressed || sample.CancelPressed)
        {
            bool wasActive = _mouseSteering || _keyboardSteering || _primaryCandidate;
            ResetPointer();
            _keyboardSteering = false;
            locomotion = wasActive || sample.SecondaryPressed || sample.CancelPressed
                ? GameLocomotionUpdate.Stop
                : GameLocomotionUpdate.Unchanged;
            return Decision(locomotion, false, default, look, sample.PointerWorldPoint, sample);
        }

        if (TryCameraRelativeDirect(
                sample.MoveX, sample.MoveZ, sample.CameraBasis, out GameLocomotionUpdate keyboard))
        {
            if (keyboard.Kind == GameLocomotionUpdateKind.Direct)
            {
                _keyboardSteering = true;
                _mouseSteering = false;
                _primaryCandidate = false;
                locomotion = keyboard;
            }
            else if (_keyboardSteering)
            {
                _keyboardSteering = false;
                locomotion = GameLocomotionUpdate.Stop;
            }
        }

        if (sample.PrimaryPressed)
        {
            bool doubleClick = sample.PointerWorldPointValid && _lastTapValid &&
                sample.TimestampMilliseconds >= _lastTapAt &&
                sample.TimestampMilliseconds - _lastTapAt <= DoubleClickThresholdMilliseconds &&
                WithinSlop(sample.PointerWorldPoint, _lastTap);
            if (doubleClick)
            {
                ResetPointer();
                _lastTapValid = false;
                _suppressRelease = true;
                _keyboardSteering = false;
                locomotion = GameLocomotionUpdate.CreateDestination(
                    sample.PointerWorldPoint.XMillimeters,
                    sample.PointerWorldPoint.ZMillimeters);
            }
            else
            {
                _primaryCandidate = sample.PointerWorldPointValid;
                _primaryStartedAt = sample.TimestampMilliseconds;
                _primaryStart = sample.PointerWorldPoint;
            }
        }

        if (sample.PrimaryDown && _primaryCandidate && sample.PointerWorldPointValid &&
            (sample.TimestampMilliseconds - _primaryStartedAt >= HoldThresholdMilliseconds ||
             !WithinSlop(sample.PointerWorldPoint, _primaryStart)))
        {
            _primaryCandidate = false;
            _mouseSteering = true;
        }

        if (_mouseSteering && sample.PrimaryDown && sample.PointerWorldPointValid &&
            TryWorldPointDirect(
                sample.ActorXMillimeters,
                sample.ActorZMillimeters,
                sample.PointerWorldPoint,
                out GameLocomotionUpdate pointerDirect))
        {
            _keyboardSteering = false;
            locomotion = pointerDirect;
        }

        if (sample.PrimaryReleased)
        {
            if (_suppressRelease)
            {
                _suppressRelease = false;
            }
            else if (_mouseSteering)
            {
                locomotion = GameLocomotionUpdate.Stop;
            }
            else if (_primaryCandidate && sample.PointerWorldPointValid)
            {
                selection = true;
                selectionPoint = sample.PointerWorldPoint;
                _lastTap = sample.PointerWorldPoint;
                _lastTapAt = sample.TimestampMilliseconds;
                _lastTapValid = true;
            }
            _primaryCandidate = false;
            _mouseSteering = false;
        }

        return Decision(locomotion, selection, selectionPoint, look,
            sample.PointerWorldPoint, sample);
    }

    public void Reset()
    {
        ResetPointer();
        _keyboardSteering = false;
        _lastTapValid = false;
    }

    private static ClientControlProfileDecision Decision(
        in GameLocomotionUpdate locomotion,
        bool selection,
        in ClientControlWorldPoint selectionPoint,
        bool look,
        in ClientControlWorldPoint lookPoint,
        in ClientOrbitCommandSample sample) => new(
            new ClientWorldCommandIntent(locomotion, sample.HeldActions, sample.HasFocus),
            selection,
            selectionPoint,
            look,
            lookPoint);

    private static bool TryCameraRelativeDirect(
        float moveX,
        float moveZ,
        in ClientHorizontalCameraBasis basis,
        out GameLocomotionUpdate locomotion)
    {
        float worldX = basis.RightX * moveX - basis.ForwardX * moveZ;
        float worldZ = basis.RightZ * moveX - basis.ForwardZ * moveZ;
        return ClientWorldInputMath.TryCreateDirect(worldX, worldZ, out locomotion);
    }

    private static bool TryWorldPointDirect(
        long actorX,
        long actorZ,
        in ClientControlWorldPoint point,
        out GameLocomotionUpdate locomotion)
    {
        long dx = checked(point.XMillimeters - actorX);
        long dz = checked(point.ZMillimeters - actorZ);
        if (dx == 0 && dz == 0)
        {
            locomotion = GameLocomotionUpdate.Stop;
            return true;
        }
        double magnitude = Math.Sqrt((double)dx * dx + (double)dz * dz);
        return ClientWorldInputMath.TryCreateDirect(
            checked((float)(dx / magnitude)), checked((float)(dz / magnitude)), out locomotion);
    }

    private static bool WithinSlop(
        in ClientControlWorldPoint left,
        in ClientControlWorldPoint right)
    {
        Int128 dx = (Int128)left.XMillimeters - right.XMillimeters;
        Int128 dz = (Int128)left.ZMillimeters - right.ZMillimeters;
        Int128 maximum = PointerSlopMillimeters;
        return dx * dx + dz * dz <= maximum * maximum;
    }

    private void ResetPointer()
    {
        _primaryCandidate = false;
        _mouseSteering = false;
        _suppressRelease = false;
    }
}

/// <summary>Crude camera-relative Direct producer retained as a portability/control canary.</summary>
public sealed class ThirdPersonDirectProfile
{
    private bool _active;

    public GameLocomotionUpdate Evaluate(
        float moveX,
        float moveZ,
        in ClientHorizontalCameraBasis basis)
    {
        if (!basis.IsValid || !ClientWorldInputMath.TryCreateDirect(
                basis.RightX * moveX - basis.ForwardX * moveZ,
                basis.RightZ * moveX - basis.ForwardZ * moveZ,
                out GameLocomotionUpdate locomotion))
            throw new ArgumentException("A finite movement sample and camera basis are required.");
        if (locomotion.Kind == GameLocomotionUpdateKind.Direct)
        {
            _active = true;
            return locomotion;
        }
        if (_active)
        {
            _active = false;
            return GameLocomotionUpdate.Stop;
        }
        return GameLocomotionUpdate.Unchanged;
    }
}

/// <summary>Crude proof that movement and look can be produced independently in Client Core.</summary>
public sealed class IndependentMoveLookProfile
{
    private readonly ThirdPersonDirectProfile _movement = new();

    public ClientControlProfileDecision Evaluate(
        float moveX,
        float moveZ,
        GamePlayerActions heldActions,
        bool hasFocus,
        in ClientHorizontalCameraBasis basis,
        bool lookPointValid,
        in ClientControlWorldPoint lookPoint)
    {
        GameLocomotionUpdate locomotion = hasFocus
            ? _movement.Evaluate(moveX, moveZ, basis)
            : GameLocomotionUpdate.Stop;
        return new ClientControlProfileDecision(
            new ClientWorldCommandIntent(
                locomotion,
                hasFocus ? heldActions : GamePlayerActions.None,
                hasFocus),
            false,
            default,
            hasFocus && lookPointValid,
            lookPoint);
    }
}
