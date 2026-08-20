using System;

namespace UltraSim.Network
{
    public enum NetworkStatsProfile : byte
    {
        Development = 0,
        Production = 1,
    }

    public readonly struct NetworkStatsProfileSettings
    {
        public readonly bool CollectDecodeTimings;
        public readonly int DecodeTimingSampleRate;
        public readonly bool TrackDecodeFailures;
        public readonly int DisconnectReasonTopN;

        public NetworkStatsProfileSettings(
            bool collectDecodeTimings,
            int decodeTimingSampleRate,
            bool trackDecodeFailures,
            int disconnectReasonTopN)
        {
            if (decodeTimingSampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(decodeTimingSampleRate));
            if (disconnectReasonTopN <= 0)
                throw new ArgumentOutOfRangeException(nameof(disconnectReasonTopN));

            CollectDecodeTimings = collectDecodeTimings;
            DecodeTimingSampleRate = decodeTimingSampleRate;
            TrackDecodeFailures = trackDecodeFailures;
            DisconnectReasonTopN = disconnectReasonTopN;
        }
    }

    public static class NetworkStatsProfiles
    {
        // Development keeps maximum detail for debugging and correctness checks.
        public static readonly NetworkStatsProfileSettings Development = new NetworkStatsProfileSettings(
            collectDecodeTimings: true,
            decodeTimingSampleRate: 1,
            trackDecodeFailures: true,
            disconnectReasonTopN: 8);

        // Production keeps critical signals while reducing runtime overhead and output cardinality.
        public static readonly NetworkStatsProfileSettings Production = new NetworkStatsProfileSettings(
            collectDecodeTimings: true,
            decodeTimingSampleRate: 64,
            trackDecodeFailures: true,
            disconnectReasonTopN: 3);

        public static NetworkStatsProfileSettings Resolve(NetworkStatsProfile profile)
        {
            return profile switch
            {
                NetworkStatsProfile.Development => Development,
                NetworkStatsProfile.Production => Production,
                _ => Development,
            };
        }
    }
}
