using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    public static class SystemScheduleUtil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDue(uint worldTick, int periodTicks, int startOffset)
        {
            if (periodTicks <= 0)
                return false;

            uint offset = (uint)startOffset;
            return (worldTick - offset) % (uint)periodTicks == 0;
        }
    }
}
