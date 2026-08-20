using System;

namespace UltraSim.Network
{
    public readonly struct InboundRateLimitPolicy
    {
        public readonly int MaxDatagramsPerWindow;
        public readonly long WindowTicks;
        public readonly int StrikeThreshold;
        public readonly long StrikeResetTicks;

        public InboundRateLimitPolicy(
            int maxDatagramsPerWindow,
            long windowTicks,
            int strikeThreshold = 0,
            long strikeResetTicks = 0)
        {
            if (maxDatagramsPerWindow <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDatagramsPerWindow));
            if (windowTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(windowTicks));
            if (strikeThreshold < 0)
                throw new ArgumentOutOfRangeException(nameof(strikeThreshold));
            if (strikeResetTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(strikeResetTicks));

            MaxDatagramsPerWindow = maxDatagramsPerWindow;
            WindowTicks = windowTicks;
            StrikeThreshold = strikeThreshold;
            StrikeResetTicks = strikeResetTicks;
        }
    }
}
