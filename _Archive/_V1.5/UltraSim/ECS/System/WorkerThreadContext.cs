using System;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    internal static class WorkerThreadContext
    {
        [ThreadStatic]
        private static int _workerIndexPlusOne;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetWorkerIndex(int workerIndex)
        {
            _workerIndexPlusOne = workerIndex + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearWorkerIndex()
        {
            _workerIndexPlusOne = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetWorkerIndex(out int workerIndex)
        {
            int value = _workerIndexPlusOne;
            if (value <= 0)
            {
                workerIndex = -1;
                return false;
            }

            workerIndex = value - 1;
            return true;
        }
    }
}
