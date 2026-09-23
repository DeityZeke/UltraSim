using UltraSim.Game.Input;
using UltraSim.Game.World;

namespace UltraSim.Client.Core;

/// <summary>
/// Client-local, finite presentation approximation. This is not an authenticated movement
/// entitlement and never determines whether Game accepts motion. The current stock value is
/// calibrated to the Concept Demo's human-scale locomotion; it can later be supplied as an
/// explicit authenticated presentation hint without changing this predictor's ownership.
/// </summary>
public readonly record struct ClientControlledMotionPolicy(
    int NominalHorizontalMillimetersPerSecond,
    int MaximumLeadMillimeters,
    int MaximumUnobservedMilliseconds)
{
    public static ClientControlledMotionPolicy ConceptDemo { get; } = new(6_000, 900, 200);

    public bool IsValid => NominalHorizontalMillimetersPerSecond is > 0 and <= 12_000 &&
        MaximumLeadMillimeters is > 0 and <= 2_000 &&
        MaximumUnobservedMilliseconds is > 0 and <= 500;
}

/// <summary>
/// Bounded horizontal presentation lead over exact owner truth. It deliberately does not resolve
/// collision, support, gravity, jump or Game actions. Times are monotonic microseconds.
/// </summary>
public sealed class ClientControlledMotionPresentation
{
    public const int HistoryCapacity = 128;
    public const int CorrectionDurationMilliseconds = 180;
    public const int CorrectionDeadbandMillimeters = 20;
    public const int DiscontinuityMillimeters = 4_000;

    private readonly ClientControlledMotionPolicy _policy;
    private readonly CommandEntry[] _history = new CommandEntry[HistoryCapacity];
    private int _head;
    private int _count;
    private int _highWater;
    private ulong _applicationEpoch;
    private GameControlSessionEpoch _controlEpoch;
    private ulong _serverGeneration;
    private WorldPublicEntityId _actor;
    private ulong _stateSequence;
    private ulong _lastAcknowledged;
    private ulong _lastSubmittedMotionSequence;
    private ClientMotionPose _authority;
    private ClientMotionPose _previousAuthority;
    private GameLocomotionUpdate _activeLocomotion;
    private GameLocomotionUpdate _suppressedLocomotion;
    private bool _suppressed;
    private bool _available;
    private bool _snapPending;
    private int _stagnantOwnerStates;
    private long _lastAdvanceMicroseconds;
    private long _lastOwnerMicroseconds;
    private long _lastLocalMotionMicroseconds;
    private long _firstPresentedMotionMicroseconds;
    private long _firstOwnerMotionMicroseconds;
    private long _leadX;
    private long _leadZ;
    private long _correctionX;
    private long _correctionZ;
    private long _correctionStartMicroseconds;
    private ulong _predictedCommands;
    private ulong _acknowledgedCommands;
    private ulong _clamps;
    private ulong _smoothCorrections;
    private ulong _snapCorrections;
    private ulong _lineageResets;
    private ulong _discontinuityResets;
    private long _maxLead;
    private long _maxCorrectionError;
    private long _sumCorrectionError;
    private ulong _correctionSamples;

    public ClientControlledMotionPresentation(ClientControlledMotionPolicy? policy = null)
    {
        _policy = policy ?? ClientControlledMotionPolicy.ConceptDemo;
        if (!_policy.IsValid)
            throw new ArgumentOutOfRangeException(nameof(policy));
    }

    public ClientControlledMotionSnapshot GetSnapshot() => new(
        _available, HistoryCapacity, _count, _highWater, _predictedCommands,
        _acknowledgedCommands, _clamps, _smoothCorrections, _snapCorrections,
        _lineageResets, _discontinuityResets, _maxLead, _maxCorrectionError,
        _correctionSamples == 0 ? 0 : _sumCorrectionError / (long)_correctionSamples,
        _lastLocalMotionMicroseconds == 0 || _firstPresentedMotionMicroseconds == 0
            ? -1 : _firstPresentedMotionMicroseconds - _lastLocalMotionMicroseconds,
        _lastLocalMotionMicroseconds == 0 || _firstOwnerMotionMicroseconds == 0
            ? -1 : _firstOwnerMotionMicroseconds - _lastLocalMotionMicroseconds,
        checked((long)HistoryCapacity *
            System.Runtime.CompilerServices.Unsafe.SizeOf<CommandEntry>()), _policy);

    public bool ObserveAdmittedCommand(
        ulong sequence, in GameLocomotionUpdate locomotion, long nowMicroseconds)
    {
        if (!_available || sequence == 0 || !locomotion.IsValid || nowMicroseconds <= 0)
            return false;
        Advance(nowMicroseconds);
        if (_count != 0 && sequence <= _history[(_head + _count - 1) % HistoryCapacity].Sequence)
            return false;
        if (_count == HistoryCapacity)
        {
            _activeLocomotion = GameLocomotionUpdate.Stop;
            Increment(ref _clamps);
            return false;
        }
        _history[(_head + _count) % HistoryCapacity] = new CommandEntry(sequence, locomotion,
            nowMicroseconds);
        _count++;
        _highWater = Math.Max(_highWater, _count);
        if (locomotion.Kind != GameLocomotionUpdateKind.Unchanged)
        {
            if (_suppressed && locomotion != _suppressedLocomotion)
                _suppressed = false;
            _activeLocomotion = locomotion;
            _lastSubmittedMotionSequence = sequence;
            if (locomotion.Kind is GameLocomotionUpdateKind.Direct or
                GameLocomotionUpdateKind.Destination && _lastLocalMotionMicroseconds == 0)
                _lastLocalMotionMicroseconds = nowMicroseconds;
        }
        Increment(ref _predictedCommands);
        return true;
    }

    public void ObserveOwner(
        ulong applicationEpoch, GameControlSessionEpoch controlEpoch, ulong serverGeneration,
        WorldPublicEntityId actor, ulong stateSequence, ulong lastProcessedCommand,
        in WorldPosition position, WorldYaw orientation, bool locomotionBlocked,
        long nowMicroseconds)
    {
        if (applicationEpoch == 0 || !controlEpoch.IsValid || serverGeneration == 0 ||
            !actor.IsValid || stateSequence == 0 || nowMicroseconds <= 0)
            throw new ArgumentException("A fresh, valid owner lineage is required.");
        ClientMotionPose newAuthority = ClientMotionPose.FromAuthority(position, orientation);
        if (!_available || applicationEpoch != _applicationEpoch || controlEpoch != _controlEpoch ||
            serverGeneration != _serverGeneration || actor != _actor)
        {
            Reset();
            _applicationEpoch = applicationEpoch;
            _controlEpoch = controlEpoch;
            _serverGeneration = serverGeneration;
            _actor = actor;
            _stateSequence = stateSequence;
            _lastAcknowledged = lastProcessedCommand;
            _authority = _previousAuthority = newAuthority;
            _lastAdvanceMicroseconds = _lastOwnerMicroseconds = nowMicroseconds;
            _available = true;
            return;
        }
        if (stateSequence <= _stateSequence) return;
        Advance(nowMicroseconds);
        ClientResolvedMotionPose before = Resolve(nowMicroseconds);
        long priorCorrectionX = CorrectionAt(_correctionX, nowMicroseconds);
        long priorCorrectionZ = CorrectionAt(_correctionZ, nowMicroseconds);
        long spatialDistance = ClientMotionPose.SpatialDistanceMillimeters(
            _authority, newAuthority);
        long authorityDistance = ClientMotionPose.HorizontalDistanceMillimeters(
            _authority, newAuthority);
        if (spatialDistance > DiscontinuityMillimeters)
        {
            ResetState(countLineageReset: false);
            Increment(ref _discontinuityResets);
            _applicationEpoch = applicationEpoch;
            _controlEpoch = controlEpoch;
            _serverGeneration = serverGeneration;
            _actor = actor;
            _stateSequence = stateSequence;
            _lastAcknowledged = lastProcessedCommand;
            _authority = _previousAuthority = newAuthority;
            _lastAdvanceMicroseconds = _lastOwnerMicroseconds = nowMicroseconds;
            _available = true;
            _snapPending = true;
            Increment(ref _snapCorrections);
            return;
        }
        _previousAuthority = _authority;
        _authority = newAuthority;
        _stateSequence = stateSequence;
        _lastOwnerMicroseconds = nowMicroseconds;
        if (_suppressed && !locomotionBlocked &&
            authorityDistance > CorrectionDeadbandMillimeters)
            _suppressed = false;
        bool acknowledgedStop = false;
        while (_count != 0 && _history[_head].Sequence <= lastProcessedCommand)
        {
            acknowledgedStop |= _history[_head].Locomotion.Kind == GameLocomotionUpdateKind.Stop;
            _history[_head] = default;
            _head = (_head + 1) % HistoryCapacity;
            _count--;
            Increment(ref _acknowledgedCommands);
        }
        _lastAcknowledged = lastProcessedCommand;
        ConsumeLeadFromAuthorityDisplacement(
            newAuthority.GlobalXMillimeters - _previousAuthority.GlobalXMillimeters,
            newAuthority.GlobalZMillimeters - _previousAuthority.GlobalZMillimeters);
        ClampLead();
        if ((acknowledgedStop && _activeLocomotion.Kind == GameLocomotionUpdateKind.Stop) ||
            (_activeLocomotion.Kind == GameLocomotionUpdateKind.Destination &&
            ClientMotionPose.HorizontalDistanceMillimeters(newAuthority,
                new ClientMotionPose(_activeLocomotion.DestinationXMillimeters,
                    newAuthority.YMillimeters, _activeLocomotion.DestinationZMillimeters,
                    newAuthority.Orientation)) <= CorrectionDeadbandMillimeters))
            _leadX = _leadZ = 0;
        bool activeIntentAcknowledged = _lastSubmittedMotionSequence != 0 &&
            lastProcessedCommand >= _lastSubmittedMotionSequence;
        if (activeIntentAcknowledged &&
            authorityDistance <= CorrectionDeadbandMillimeters &&
            _activeLocomotion.Kind is GameLocomotionUpdateKind.Direct or
                GameLocomotionUpdateKind.Destination)
            _stagnantOwnerStates++;
        else
            _stagnantOwnerStates = 0;
        if (activeIntentAcknowledged && (locomotionBlocked || _stagnantOwnerStates >= 3))
        {
            _suppressed = true;
            _suppressedLocomotion = _activeLocomotion;
            _activeLocomotion = GameLocomotionUpdate.Stop;
            _leadX = _leadZ = 0;
        }
        ClientMotionPose target = BasePose();
        long error = ClientMotionPose.HorizontalDistanceMillimeters(before.Pose, target);
        _maxCorrectionError = Math.Max(_maxCorrectionError, error);
        _sumCorrectionError = Math.Min(long.MaxValue - error, _sumCorrectionError) + error;
        Increment(ref _correctionSamples);
        if (error >= DiscontinuityMillimeters)
        {
            _leadX = _leadZ = _correctionX = _correctionZ = 0;
            _activeLocomotion = GameLocomotionUpdate.Stop;
            _snapPending = true;
            Increment(ref _snapCorrections);
        }
        else if (Math.Abs(before.Pose.GlobalXMillimeters - target.GlobalXMillimeters -
                priorCorrectionX) > CorrectionDeadbandMillimeters ||
            Math.Abs(before.Pose.GlobalZMillimeters - target.GlobalZMillimeters -
                priorCorrectionZ) > CorrectionDeadbandMillimeters)
        {
            _correctionX = before.Pose.GlobalXMillimeters - target.GlobalXMillimeters;
            _correctionZ = before.Pose.GlobalZMillimeters - target.GlobalZMillimeters;
            _correctionStartMicroseconds = nowMicroseconds;
            Increment(ref _smoothCorrections);
        }
        if (_lastLocalMotionMicroseconds != 0 && _firstOwnerMotionMicroseconds == 0 &&
            authorityDistance > CorrectionDeadbandMillimeters)
            _firstOwnerMotionMicroseconds = nowMicroseconds;
    }

    public ClientResolvedMotionPose Sample(long nowMicroseconds)
    {
        if (!_available || nowMicroseconds <= 0)
            return default;
        Advance(nowMicroseconds);
        ClientResolvedMotionPose value = Resolve(nowMicroseconds);
        if (_snapPending)
        {
            _snapPending = false;
            value = value with { Provenance = ClientMotionPoseProvenance.DiscontinuitySnap };
        }
        if (_lastLocalMotionMicroseconds != 0 && _firstPresentedMotionMicroseconds == 0 &&
            ClientMotionPose.HorizontalDistanceMillimeters(value.Pose, _authority) >
                CorrectionDeadbandMillimeters)
            _firstPresentedMotionMicroseconds = nowMicroseconds;
        return value;
    }

    public void Reset()
        => ResetState(countLineageReset: true);

    private void ResetState(bool countLineageReset)
    {
        if (_available && countLineageReset) Increment(ref _lineageResets);
        _history.AsSpan().Clear();
        _head = _count = _stagnantOwnerStates = 0;
        _available = _suppressed = _snapPending = false;
        _activeLocomotion = _suppressedLocomotion = default;
        _applicationEpoch = _serverGeneration = _stateSequence = _lastAcknowledged =
            _lastSubmittedMotionSequence = 0;
        _controlEpoch = default;
        _actor = default;
        _leadX = _leadZ = _correctionX = _correctionZ = 0;
        _lastAdvanceMicroseconds = _lastOwnerMicroseconds = 0;
        _lastLocalMotionMicroseconds = _firstPresentedMotionMicroseconds =
            _firstOwnerMotionMicroseconds = 0;
    }

    private void Advance(long nowMicroseconds)
    {
        if (!_available || nowMicroseconds <= _lastAdvanceMicroseconds) return;
        long duration = Math.Min(nowMicroseconds - _lastAdvanceMicroseconds,
            _policy.MaximumUnobservedMilliseconds * 1_000L);
        if (duration < nowMicroseconds - _lastAdvanceMicroseconds) Increment(ref _clamps);
        _lastAdvanceMicroseconds = nowMicroseconds;
        if (_suppressed || _activeLocomotion.Kind is not
            (GameLocomotionUpdateKind.Direct or GameLocomotionUpdateKind.Destination)) return;
        double magnitude = _policy.NominalHorizontalMillimetersPerSecond * duration / 1_000_000d;
        double x, z;
        if (_activeLocomotion.Kind == GameLocomotionUpdateKind.Direct)
        {
            x = _activeLocomotion.DirectX / (double)GamePlayerCommand.MaximumAxisMagnitude;
            z = _activeLocomotion.DirectZ / (double)GamePlayerCommand.MaximumAxisMagnitude;
        }
        else
        {
            x = (double)_activeLocomotion.DestinationXMillimeters -
                (_authority.GlobalXMillimeters + _leadX);
            z = (double)_activeLocomotion.DestinationZMillimeters -
                (_authority.GlobalZMillimeters + _leadZ);
            double length = Math.Sqrt(x * x + z * z);
            if (length <= 1d) return;
            x /= length;
            z /= length;
            magnitude = Math.Min(magnitude, length);
        }
        _leadX += (long)Math.Round(x * magnitude);
        _leadZ += (long)Math.Round(z * magnitude);
        ClampLead();
    }

    private void ClampLead()
    {
        double length = Math.Sqrt((double)_leadX * _leadX + (double)_leadZ * _leadZ);
        if (length > _policy.MaximumLeadMillimeters)
        {
            double ratio = _policy.MaximumLeadMillimeters / length;
            _leadX = (long)Math.Round(_leadX * ratio);
            _leadZ = (long)Math.Round(_leadZ * ratio);
            Increment(ref _clamps);
        }
        _maxLead = Math.Max(_maxLead,
            (long)Math.Sqrt((double)_leadX * _leadX + (double)_leadZ * _leadZ));
        Int128 x = (Int128)_authority.GlobalXMillimeters + _leadX;
        Int128 z = (Int128)_authority.GlobalZMillimeters + _leadZ;
        if (x < long.MinValue || x > long.MaxValue ||
            z < long.MinValue || z > long.MaxValue ||
            !WorldPosition.TryFromGlobalMillimeters((long)x, _authority.YMillimeters,
                (long)z, out _))
        {
            _leadX = _leadZ = 0;
            Increment(ref _clamps);
        }
    }

    private void ConsumeLeadFromAuthorityDisplacement(long dx, long dz)
    {
        // Authority can consume a lead that was actually speculated in its direction. It must
        // never manufacture a negative lead from unrelated movement, including an idle actor's
        // ordinary relocation or movement after a blocked prediction has been suppressed.
        if (_leadX == 0 && _leadZ == 0) return;
        double leadSquared = (double)_leadX * _leadX + (double)_leadZ * _leadZ;
        double aligned = (double)dx * _leadX + (double)dz * _leadZ;
        if (aligned <= 0) return;
        double fraction = Math.Min(1d, aligned / leadSquared);
        _leadX -= (long)Math.Round(_leadX * fraction);
        _leadZ -= (long)Math.Round(_leadZ * fraction);
    }

    private ClientResolvedMotionPose Resolve(long nowMicroseconds)
    {
        long correctionX = CorrectionAt(_correctionX, nowMicroseconds);
        long correctionZ = CorrectionAt(_correctionZ, nowMicroseconds);
        if (correctionX == 0 && correctionZ == 0)
            _correctionX = _correctionZ = _correctionStartMicroseconds = 0;
        ClientMotionPose basePose = BasePose();
        ClientMotionPose pose = basePose with
        {
            GlobalXMillimeters = checked(basePose.GlobalXMillimeters + correctionX),
            GlobalZMillimeters = checked(basePose.GlobalZMillimeters + correctionZ),
        };
        ClientMotionPoseProvenance provenance = correctionX != 0 || correctionZ != 0
            ? ClientMotionPoseProvenance.ReconciledControlled
            : _leadX != 0 || _leadZ != 0
                ? ClientMotionPoseProvenance.PredictedControlled
                : ClientMotionPoseProvenance.Authoritative;
        return new ClientResolvedMotionPose(pose, provenance);
    }

    private ClientMotionPose BasePose() => _authority with
    {
        GlobalXMillimeters = checked(_authority.GlobalXMillimeters + _leadX),
        GlobalZMillimeters = checked(_authority.GlobalZMillimeters + _leadZ),
    };

    private long CorrectionAt(long offset, long nowMicroseconds)
    {
        if (_correctionStartMicroseconds == 0) return 0;
        long duration = CorrectionDurationMilliseconds * 1_000L;
        long remaining = Math.Max(0, duration - Math.Max(0,
            nowMicroseconds - _correctionStartMicroseconds));
        return offset * remaining / duration;
    }

    private static void Increment(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private readonly record struct CommandEntry(
        ulong Sequence, GameLocomotionUpdate Locomotion, long AcceptedMicroseconds);
}

public readonly record struct ClientControlledMotionSnapshot(
    bool Enabled,
    int HistoryCapacity,
    int HistoryCount,
    int HistoryHighWater,
    ulong CommandsPredicted,
    ulong CommandsAcknowledged,
    ulong PredictionClamps,
    ulong SmoothCorrections,
    ulong SnapCorrections,
    ulong LineageResets,
    ulong DiscontinuityResets,
    long MaximumSpeculativeLeadMillimeters,
    long MaximumCorrectionErrorMillimeters,
    long MeanCorrectionErrorMillimeters,
    long InputToPresentedMicroseconds,
    long InputToOwnerMotionMicroseconds,
    long ReservedHistoryBytes,
    ClientControlledMotionPolicy Policy);
