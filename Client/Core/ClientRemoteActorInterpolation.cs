using UltraSim.Game.World;

namespace UltraSim.Client.Core;

/// <summary>
/// Fixed-capacity receipt-time interpolation of authoritative public actor poses. Receipt time is
/// client monotonic time, not a claimed server simulation tick. No extrapolation is performed.
/// </summary>
public sealed class ClientRemoteActorInterpolation
{
    public const int MaximumActors = 64;
    // At 60 Hz, a 100 ms delay already spans about six publications. Twelve samples leave
    // bounded room for cadence and receipt jitter without holding merely because the ring
    // discarded the pair surrounding the presentation timestamp.
    public const int SamplesPerActor = 12;
    public const int DelayMilliseconds = 100;
    public const int MaximumSampleGapMilliseconds = 500;
    public const int TeleportMillimeters = 4_000;

    private readonly ActorBuffer[] _actors = new ActorBuffer[MaximumActors];
    private readonly Sample[] _samples = new Sample[MaximumActors * SamplesPerActor];
    private ulong _serverGeneration;
    private int _active;
    private int _highWater;
    private int _sampleHighWater;
    private ulong _received;
    private ulong _interpolatedFrames;
    private ulong _heldFrames;
    private ulong _gapResets;
    private ulong _teleportResets;
    private ulong _identityResets;
    private ulong _staleHeldFrames;
    private long _maximumNewestSampleAgeMicroseconds;
    private long _maximumReceiptGapMicroseconds;

    public void ResetGeneration(ulong serverGeneration)
    {
        if (serverGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(serverGeneration));
        if (_serverGeneration == serverGeneration) return;
        if (_serverGeneration != 0) _identityResets += (ulong)_active;
        _actors.AsSpan().Clear();
        _samples.AsSpan().Clear();
        _active = 0;
        _serverGeneration = serverGeneration;
    }

    public bool Observe(
        ulong serverGeneration, WorldPublicEntityId id, ulong publicVersion,
        in ClientMotionPose pose, long receiptMicroseconds)
    {
        if (serverGeneration == 0 || !id.IsValid || publicVersion == 0 ||
            receiptMicroseconds <= 0 || !pose.TryGetWorldPosition(out _))
            return false;
        ResetGeneration(serverGeneration);
        int index = Find(id);
        if (index < 0)
        {
            index = FindFree();
            if (index < 0) return false;
            _actors[index].Id = id;
            _active++;
            _highWater = Math.Max(_highWater, _active);
        }
        ref ActorBuffer actor = ref _actors[index];
        if (actor.Count != 0)
        {
            Sample previous = GetSample(index, actor.Count - 1);
            if (publicVersion <= previous.Version) return false;
            long gap = receiptMicroseconds - previous.ReceivedMicroseconds;
            if (gap > 0)
                _maximumReceiptGapMicroseconds = Math.Max(_maximumReceiptGapMicroseconds, gap);
            if (gap <= 0 || gap > MaximumSampleGapMilliseconds * 1_000L)
            {
                ClearSamples(index);
                actor.SnapPending = true;
                Increment(ref _gapResets);
            }
            else if (ClientMotionPose.SpatialDistanceMillimeters(previous.Pose, pose) >
                TeleportMillimeters)
            {
                ClearSamples(index);
                actor.SnapPending = true;
                Increment(ref _teleportResets);
            }
        }
        AddSample(index, new Sample(publicVersion, receiptMicroseconds, pose));
        _sampleHighWater = Math.Max(_sampleHighWater, actor.Count);
        Increment(ref _received);
        return true;
    }

    public void Remove(WorldPublicEntityId id)
    {
        int index = Find(id);
        if (index < 0) return;
        ClearSamples(index);
        _actors[index] = default;
        _active--;
        Increment(ref _identityResets);
    }

    public bool TrySample(WorldPublicEntityId id, long nowMicroseconds,
        out ClientResolvedMotionPose result)
    {
        bool available = TrySampleTemporal(id, nowMicroseconds, out var temporal);
        result = temporal.Resolved;
        return available;
    }

    public bool TrySampleTemporal(WorldPublicEntityId id, long nowMicroseconds,
        out ClientActorTemporalPresentation temporal)
    {
        temporal = default;
        if (nowMicroseconds <= 0) return false;
        int index = Find(id);
        if (index < 0) return false;
        ref ActorBuffer actor = ref _actors[index];
        if (actor.Count == 0) return false;
        long newestAge = Math.Max(0,
            nowMicroseconds - GetSample(index, actor.Count - 1).ReceivedMicroseconds);
        _maximumNewestSampleAgeMicroseconds =
            Math.Max(_maximumNewestSampleAgeMicroseconds, newestAge);
        if (newestAge > MaximumSampleGapMilliseconds * 1_000L)
            Increment(ref _staleHeldFrames);
        long target = nowMicroseconds - DelayMilliseconds * 1_000L;
        Sample first = GetSample(index, 0);
        if (actor.Count == 1 || target <= first.ReceivedMicroseconds)
        {
            var result = new ClientResolvedMotionPose(first.Pose,
                actor.SnapPending ? ClientMotionPoseProvenance.DiscontinuitySnap :
                    ClientMotionPoseProvenance.HeldRemote);
            actor.SnapPending = false;
            Increment(ref _heldFrames);
            temporal = Describe(id, first, first, target, result);
            return true;
        }
        for (int sampleIndex = 1; sampleIndex < actor.Count; sampleIndex++)
        {
            Sample next = GetSample(index, sampleIndex);
            if (target > next.ReceivedMicroseconds) continue;
            Sample previous = GetSample(index, sampleIndex - 1);
            var result = new ClientResolvedMotionPose(
                ClientMotionPose.Interpolate(previous.Pose, next.Pose,
                    target - previous.ReceivedMicroseconds,
                    next.ReceivedMicroseconds - previous.ReceivedMicroseconds),
                actor.SnapPending ? ClientMotionPoseProvenance.DiscontinuitySnap :
                    ClientMotionPoseProvenance.InterpolatedRemote);
            actor.SnapPending = false;
            Increment(ref _interpolatedFrames);
            temporal = Describe(id, previous, next, target, result);
            return true;
        }
        Sample newest = GetSample(index, actor.Count - 1);
        var held = new ClientResolvedMotionPose(newest.Pose,
            actor.SnapPending ? ClientMotionPoseProvenance.DiscontinuitySnap :
                ClientMotionPoseProvenance.HeldRemote);
        actor.SnapPending = false;
        Increment(ref _heldFrames);
        temporal = Describe(id, newest, newest, target, held);
        return true;
    }

    private ClientActorTemporalPresentation Describe(WorldPublicEntityId id,
        in Sample previous, in Sample current, long target, in ClientResolvedMotionPose resolved) =>
        new(_serverGeneration, id, previous.Version, current.Version, previous.Pose, current.Pose,
            previous.ReceivedMicroseconds, current.ReceivedMicroseconds, target, resolved);

    public ClientRemoteActorInterpolationSnapshot GetSnapshot() => new(
        _serverGeneration, MaximumActors, SamplesPerActor, _active, _highWater,
        _sampleHighWater, _received, _interpolatedFrames, _heldFrames,
        _gapResets, _teleportResets, _identityResets, _staleHeldFrames,
        _maximumNewestSampleAgeMicroseconds, _maximumReceiptGapMicroseconds,
        DelayMilliseconds,
        checked((long)MaximumActors * SamplesPerActor *
            System.Runtime.CompilerServices.Unsafe.SizeOf<Sample>()),
        checked((long)MaximumActors *
            System.Runtime.CompilerServices.Unsafe.SizeOf<ActorBuffer>()));

    private int Find(WorldPublicEntityId id)
    {
        for (int index = 0; index < _actors.Length; index++)
            if (_actors[index].Count != 0 && _actors[index].Id == id) return index;
        return -1;
    }

    private int FindFree()
    {
        for (int index = 0; index < _actors.Length; index++)
            if (_actors[index].Count == 0) return index;
        return -1;
    }

    private Sample GetSample(int actorIndex, int logicalIndex)
    {
        ref readonly ActorBuffer actor = ref _actors[actorIndex];
        return _samples[actorIndex * SamplesPerActor +
            (actor.Head + logicalIndex) % SamplesPerActor];
    }

    private void AddSample(int actorIndex, in Sample sample)
    {
        ref ActorBuffer actor = ref _actors[actorIndex];
        if (actor.Count == SamplesPerActor)
        {
            actor.Head = (actor.Head + 1) % SamplesPerActor;
            actor.Count--;
        }
        _samples[actorIndex * SamplesPerActor +
            (actor.Head + actor.Count) % SamplesPerActor] = sample;
        actor.Count++;
    }

    private void ClearSamples(int actorIndex)
    {
        ref ActorBuffer actor = ref _actors[actorIndex];
        _samples.AsSpan(actorIndex * SamplesPerActor, SamplesPerActor).Clear();
        actor.Head = actor.Count = 0;
    }

    private static void Increment(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private readonly record struct Sample(
        ulong Version, long ReceivedMicroseconds, ClientMotionPose Pose);

    private struct ActorBuffer
    {
        public WorldPublicEntityId Id;
        public int Head;
        public int Count;
        public bool SnapPending;
    }
}

public readonly record struct ClientRemoteActorInterpolationSnapshot(
    ulong ServerGeneration,
    int ActorCapacity,
    int SamplesPerActor,
    int ActiveBuffers,
    int ActiveHighWater,
    int SampleHighWater,
    ulong SamplesReceived,
    ulong InterpolatedFrames,
    ulong HeldFrames,
    ulong GapResets,
    ulong TeleportResets,
    ulong IdentityResets,
    ulong StaleHeldFrames,
    long MaximumNewestSampleAgeMicroseconds,
    long MaximumReceiptGapMicroseconds,
    int DelayMilliseconds,
    long ReservedSampleBytes,
    long ReservedActorIndexBytes);
