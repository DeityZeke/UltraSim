using System;

namespace UltraSim.Network
{
    internal static class ReplicationDeltaSentKey
    {
        public static ulong Compute(
            long tick,
            NetworkEntityId entityId,
            ushort streamId,
            ReplicationLane lane,
            ulong coalesceKey,
            ReadOnlySpan<byte> payload)
        {
            // FNV-1a 64-bit over stable metadata + payload. This lets replay/deferred paths
            // reuse a precomputed key instead of hashing the payload again in hot loops.
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong h = offset;
            h = (h ^ (ulong)tick) * prime;
            h = (h ^ (ulong)(tick >> 32)) * prime;
            ulong entity = entityId.Value;
            h = (h ^ entity) * prime;
            h = (h ^ (entity >> 32)) * prime;
            h = (h ^ streamId) * prime;
            h = (h ^ (uint)lane) * prime;
            h = (h ^ coalesceKey) * prime;
            h = (h ^ (coalesceKey >> 32)) * prime;
            h = (h ^ (uint)payload.Length) * prime;
            for (int i = 0; i < payload.Length; i++)
                h = (h ^ payload[i]) * prime;
            return h;
        }
    }
}
