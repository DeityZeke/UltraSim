#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using UltraSim.ECS.Systems;

namespace UltraSim.ECS.Threading
{
    /// <summary>
    /// Executes system batches in parallel using the shared ManualThreadPool.
    /// Systems within a batch run concurrently; batches run sequentially.
    /// Includes optional callbacks for profiling/UI.
    /// </summary>
    public static class ParallelSystemScheduler
    {
        private static readonly ManualThreadPool _threadPool = new(Environment.ProcessorCount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RunBatches(
            List<List<BaseSystem>> batches,
            World world,
            double delta,
            Action<BaseSystem>? onSystemStarted = null,
            Action<BaseSystem>? onSystemCompleted = null)
        {
            var batchSpan = CollectionsMarshal.AsSpan(batches);

            foreach (ref var batch in batchSpan)
            {
                var batchList = batch;
                int enabledCount = 0;
                for (int i = 0; i < batchList.Count; i++)
                {
                    if (batchList[i].IsEnabled)
                        enabledCount++;
                }

                if (enabledCount == 0)
                    continue;

                _threadPool.ParallelFor(batchList.Count, i =>
                {
                    var sys = batchList[i];
                    if (!sys.IsEnabled)
                        return;

                    try
                    {
                        onSystemStarted?.Invoke(sys);
                        sys.UpdateWithTiming(world, delta);
                    }
                    finally
                    {
                        onSystemCompleted?.Invoke(sys);
                    }
                });
            }
        }
    }
}
