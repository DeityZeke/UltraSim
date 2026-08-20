using System.Diagnostics;

namespace UltraSim.Network
{
    public readonly struct ProtocolStatsSnapshot
    {
        public readonly long DecodedPackets;
        public readonly long DecodedBytes;
        public readonly long DecodeFailures;
        public readonly long UnknownPackets;
        public readonly long UnknownBytes;
        public readonly long DecodeTicks;

        public ProtocolStatsSnapshot(
            long decodedPackets,
            long decodedBytes,
            long decodeFailures,
            long unknownPackets,
            long unknownBytes,
            long decodeTicks)
        {
            DecodedPackets = decodedPackets;
            DecodedBytes = decodedBytes;
            DecodeFailures = decodeFailures;
            UnknownPackets = unknownPackets;
            UnknownBytes = unknownBytes;
            DecodeTicks = decodeTicks;
        }

        public double DecodeMilliseconds =>
            DecodeTicks <= 0 ? 0d : (DecodeTicks * 1000d) / Stopwatch.Frequency;
    }
}
