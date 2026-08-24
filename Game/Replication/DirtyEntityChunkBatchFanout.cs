using UltraSim.Network;

namespace UltraSim.Game.Replication;

/// <summary>
/// Concrete Entity Dirty Push profile whose semantic unit, recipient knowledge, wire records,
/// and application proofs remain entity-granular while entrant discovery is coalesced by the
/// entity's current world chunk. Existing semantic rows supply the complete old audience.
/// </summary>
/// <remarks>
/// This profile deliberately reuses the accepted dirty-entity semantic store and remote codec.
/// It adds neither chunk publication state nor a retained Chunk-to-Recipient reverse graph.
/// </remarks>
public sealed class DirtyEntityChunkBatchFanout : DirtyEntitySpatialFanout
{
    public new static GamePublicReplicationProfileDescriptor PublicReplicationProfile =>
        GamePublicReplicationProfiles.DirtyEntityChunkBatchFanout;

    public DirtyEntityChunkBatchFanout(
        StateEpoch stateEpoch,
        DirtyEntitySpatialFanoutLimits limits = default,
        GameInterestProfile profile = default)
        : base(stateEpoch, limits, profile, chunkBatchedDiscovery: true)
    {
    }
}
