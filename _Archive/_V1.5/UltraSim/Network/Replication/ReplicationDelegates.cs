using System;

namespace UltraSim.Network
{
    public delegate int ReplicationPayloadEncoder(in ReplicationCandidate candidate, Span<byte> destination);

    public delegate byte[] AllocatingReplicationPayloadEncoder(in ReplicationCandidate candidate);

    public delegate bool ReplicationInterestFilter(int connectionId, in EncodedReplicationDelta delta);

    // Runs after AoI/interest filtering and before packet send/budget consumption.
    // This is intended for per-(connection, delta) gates such as replication LOD/cadence.
    public delegate bool ReplicationLodFilter(int connectionId, in EncodedReplicationDelta delta);

    public delegate uint ReplicationDeltaGroupSelector(in EncodedReplicationDelta delta);

    // Returns the number of recipient connection IDs for the delta.
    // If the return value exceeds destination.Length, the caller should grow and retry.
    // Return a negative value to indicate "fallback to normal pipeline dispatch path".
    public delegate int ReplicationRecipientSelector(in EncodedReplicationDelta delta, Span<int> destination);
    // Optional cache key for recipient selector results. Return 0 to use pipeline default keying.
    public delegate ulong ReplicationRecipientSelectionKeySelector(in EncodedReplicationDelta delta);

    public delegate void ReplicationPacketSink(in ReplicationPacket packet);

    public delegate void ReplicationPacketBatchSink(ReplicationPacket[] packets, int count);
}
