using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    public struct EcsStats
    {
        public int CreatesEnqueued;
        public int SpawnsEnqueued;
        public int AddsEnqueued;
        public int RemovesEnqueued;
        public int DestroysEnqueued;
        public int CreatesApplied;
        public int SpawnsApplied;
        public int AddsApplied;
        public int RemovesApplied;
        public int DestroysApplied;

        public int DirtyMarkCalls;
        public int DirtyMarkBatchCalls;
        public int DirtyMarkTrustedBatchCalls;
        public long DirtyMarkedEntities;
        public long DirtyTrustedMarkedEntities;

        public long LastStructuralTicks;
        public long LastSystemsTicks;
        public long LastTickTicks;

        public long TotalStructuralTicks;
        public long TotalSystemsTicks;
        public long TotalTickTicks;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordStructural(long ticks)
        {
            LastStructuralTicks = ticks;
            TotalStructuralTicks += ticks;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordSystems(long ticks)
        {
            LastSystemsTicks = ticks;
            TotalSystemsTicks += ticks;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordTick(long ticks)
        {
            LastTickTicks = ticks;
            TotalTickTicks += ticks;
        }
    }

    public readonly struct ValueStopwatch
    {
        private readonly long _start;

        private ValueStopwatch(long start) => _start = start;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ElapsedTicks() => Stopwatch.GetTimestamp() - _start;
    }
}
