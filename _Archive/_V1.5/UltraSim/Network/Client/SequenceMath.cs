using System.Runtime.CompilerServices;

namespace UltraSim.Network
{
    public static class SequenceMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNewer(ushort sequence, ushort baseline) =>
            (short)(sequence - baseline) > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ForwardDistance(ushort from, ushort to) =>
            (ushort)(to - from);
    }
}
