using UltraSim.Game.World;

namespace UltraSim.Client.Core;

/// <summary>Implemented presentation tiers, never authoritative actor state or protocol values.</summary>
public enum ClientActorRepresentationTier : byte { Detailed = 1, BatchedPrimitive = 2 }

/// <summary>Local world-space resource policy. Mandatory features may expand the soft detailed budget.</summary>
public readonly record struct ClientActorRepresentationPolicy(
    bool Enabled, int MaximumDetailedActors, int PromotionDistanceMillimeters,
    int DemotionDistanceMillimeters)
{
    public bool IsValid => MaximumDetailedActors is >= 1 and <= 256 &&
        PromotionDistanceMillimeters is >= 1 and <= 100_000 &&
        DemotionDistanceMillimeters > PromotionDistanceMillimeters &&
        DemotionDistanceMillimeters <= 200_000;
    public static ClientActorRepresentationPolicy ConceptDemo => new(true, 64, 20_000, 28_000);
    public static ClientActorRepresentationPolicy AllDetailed => new(false, 256, 20_000, 28_000);
}

public readonly record struct ClientActorRepresentationCandidate(
    WorldPublicEntityId Id, ClientMotionPose Pose, ClientActorRepresentationTier Current,
    bool MandatoryDetailed);

/// <summary>Bounded, deterministic, allocation-free selection; inputs are already-disclosed presented facts.</summary>
public sealed class ClientActorRepresentationSelector
{
    private readonly int[] _order;
    private readonly double[] _distanceSquared;
    public ClientActorRepresentationPolicy Policy { get; }

    public ClientActorRepresentationSelector(int capacity, ClientActorRepresentationPolicy policy)
    {
        if (capacity is < 1 or > 256 || !policy.IsValid)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _order = new int[capacity]; _distanceSquared = new double[capacity]; Policy = policy;
    }

    public void Select(ReadOnlySpan<ClientActorRepresentationCandidate> candidates,
        in ClientMotionPose viewer, Span<ClientActorRepresentationTier> result)
    {
        if (candidates.Length > _order.Length || result.Length < candidates.Length)
            throw new ArgumentException("Representation destinations must fit the bounded cohort.");
        int mandatory = 0, eligible = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            ref readonly var actor = ref candidates[i];
            if (!actor.Id.IsValid || actor.Current is not (ClientActorRepresentationTier.Detailed or
                ClientActorRepresentationTier.BatchedPrimitive) || !actor.Pose.TryGetWorldPosition(out _))
                throw new ArgumentException("Invalid representation candidate.");
            result[i] = !Policy.Enabled || actor.MandatoryDetailed
                ? ClientActorRepresentationTier.Detailed : ClientActorRepresentationTier.BatchedPrimitive;
            if (actor.MandatoryDetailed) { mandatory++; continue; }
            if (!Policy.Enabled) continue;
            double dx = (double)actor.Pose.GlobalXMillimeters - viewer.GlobalXMillimeters;
            double dy = (double)actor.Pose.YMillimeters - viewer.YMillimeters;
            double dz = (double)actor.Pose.GlobalZMillimeters - viewer.GlobalZMillimeters;
            double distance = dx * dx + dy * dy + dz * dz;
            _distanceSquared[i] = distance;
            int threshold = actor.Current == ClientActorRepresentationTier.Detailed
                ? Policy.DemotionDistanceMillimeters : Policy.PromotionDistanceMillimeters;
            if (distance > (double)threshold * threshold) continue;
            int position = eligible;
            while (position > 0)
            {
                int prior = _order[position - 1];
                if (_distanceSquared[prior] < distance || _distanceSquared[prior] == distance &&
                    candidates[prior].Id.CompareTo(actor.Id) < 0) break;
                _order[position] = prior; position--;
            }
            _order[position] = i; eligible++;
        }
        int discretionary = Math.Max(0, Policy.MaximumDetailedActors - mandatory);
        for (int i = 0; i < Math.Min(eligible, discretionary); i++)
            result[_order[i]] = ClientActorRepresentationTier.Detailed;
    }
}

/// <summary>
/// Actual receipt-time bracket selected by F1.8A, plus its CPU-resolved pose. Held/snap samples
/// have equal endpoints. This enables future realization without requiring GPU readback or
/// conflating the controlled predictor with remote interpolation. Timestamps are not server ticks.
/// </summary>
public readonly record struct ClientActorTemporalPresentation(
    ulong ServerGeneration, WorldPublicEntityId Id,
    ulong PreviousPublicVersion, ulong CurrentPublicVersion,
    ClientMotionPose Previous, ClientMotionPose Current,
    long PreviousReceiptMicroseconds, long CurrentReceiptMicroseconds,
    long TargetReceiptMicroseconds, ClientResolvedMotionPose Resolved);
