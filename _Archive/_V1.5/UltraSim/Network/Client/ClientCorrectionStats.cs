using System;
using System.Threading;

namespace UltraSim.Network
{
    public sealed class ClientCorrectionStats
    {
        private long _rollbackCount;
        private long _replayedInputCount;
        private int _maxRollbackInputs;
        private double _totalCorrectionMagnitude;
        private long _correctionSamples;
        private long _applyLatencyTicksSum;
        private long _maxApplyLatencyTicks;
        private long _applyEvents;
        private long _queuedCorrections;
        private long _coalescedCorrections;
        private int _maxQueuedCorrectionsPerTick;
        private int _maxCoalescedCorrectionsPerTick;
        private int _maxQueueDepth;

        public void RecordRollback(int replayedInputs)
        {
            if (replayedInputs < 0)
                throw new ArgumentOutOfRangeException(nameof(replayedInputs));

            Interlocked.Increment(ref _rollbackCount);
            Interlocked.Add(ref _replayedInputCount, replayedInputs);
            UpdateMaxRollbackInputs(replayedInputs);
        }

        public void RecordCorrectionMagnitude(double magnitude)
        {
            if (magnitude < 0 || !double.IsFinite(magnitude))
                throw new ArgumentOutOfRangeException(nameof(magnitude));

            AddDouble(ref _totalCorrectionMagnitude, magnitude);
            Interlocked.Increment(ref _correctionSamples);
        }

        public void RecordApplyLatency(long queuedClientTick, long appliedClientTick)
        {
            long latency = appliedClientTick - queuedClientTick;
            if (latency < 0)
                latency = 0;

            Interlocked.Add(ref _applyLatencyTicksSum, latency);
            Interlocked.Increment(ref _applyEvents);
            UpdateMaxLatency(latency);
        }

        public void RecordQueuedCorrections(int queuedCorrections, int coalescedCorrections, int queueDepth)
        {
            if (queuedCorrections < 0)
                throw new ArgumentOutOfRangeException(nameof(queuedCorrections));
            if (coalescedCorrections < 0)
                throw new ArgumentOutOfRangeException(nameof(coalescedCorrections));
            if (queueDepth < 0)
                throw new ArgumentOutOfRangeException(nameof(queueDepth));

            Interlocked.Add(ref _queuedCorrections, queuedCorrections);
            Interlocked.Add(ref _coalescedCorrections, coalescedCorrections);
            UpdateMaxQueuedCorrectionsPerTick(queuedCorrections);
            UpdateMaxCoalescedCorrectionsPerTick(coalescedCorrections);
            UpdateMaxQueueDepth(queueDepth);
        }

        public ClientCorrectionStatsSnapshot Snapshot() => new ClientCorrectionStatsSnapshot(
            rollbackCount: Interlocked.Read(ref _rollbackCount),
            replayedInputCount: Interlocked.Read(ref _replayedInputCount),
            maxRollbackInputs: Volatile.Read(ref _maxRollbackInputs),
            totalCorrectionMagnitude: Volatile.Read(ref _totalCorrectionMagnitude),
            correctionSamples: Interlocked.Read(ref _correctionSamples),
            applyLatencyTicksSum: Interlocked.Read(ref _applyLatencyTicksSum),
            maxApplyLatencyTicks: Interlocked.Read(ref _maxApplyLatencyTicks),
            applyEvents: Interlocked.Read(ref _applyEvents),
            queuedCorrections: Interlocked.Read(ref _queuedCorrections),
            coalescedCorrections: Interlocked.Read(ref _coalescedCorrections),
            maxQueuedCorrectionsPerTick: Volatile.Read(ref _maxQueuedCorrectionsPerTick),
            maxCoalescedCorrectionsPerTick: Volatile.Read(ref _maxCoalescedCorrectionsPerTick),
            maxQueueDepth: Volatile.Read(ref _maxQueueDepth));

        public void Reset()
        {
            Interlocked.Exchange(ref _rollbackCount, 0);
            Interlocked.Exchange(ref _replayedInputCount, 0);
            Interlocked.Exchange(ref _maxRollbackInputs, 0);
            Interlocked.Exchange(ref _correctionSamples, 0);
            Interlocked.Exchange(ref _applyLatencyTicksSum, 0);
            Interlocked.Exchange(ref _maxApplyLatencyTicks, 0);
            Interlocked.Exchange(ref _applyEvents, 0);
            Interlocked.Exchange(ref _queuedCorrections, 0);
            Interlocked.Exchange(ref _coalescedCorrections, 0);
            Interlocked.Exchange(ref _maxQueuedCorrectionsPerTick, 0);
            Interlocked.Exchange(ref _maxCoalescedCorrectionsPerTick, 0);
            Interlocked.Exchange(ref _maxQueueDepth, 0);
            Interlocked.Exchange(ref _totalCorrectionMagnitude, 0d);
        }

        private static void AddDouble(ref double location, double value)
        {
            while (true)
            {
                double current = Volatile.Read(ref location);
                double next = current + value;
                double previous = Interlocked.CompareExchange(ref location, next, current);
                if (previous == current)
                    break;
            }
        }

        private void UpdateMaxRollbackInputs(int value)
        {
            while (true)
            {
                int current = Volatile.Read(ref _maxRollbackInputs);
                if (value <= current)
                    return;

                int previous = Interlocked.CompareExchange(ref _maxRollbackInputs, value, current);
                if (previous == current)
                    return;
            }
        }

        private void UpdateMaxLatency(long value)
        {
            while (true)
            {
                long current = Volatile.Read(ref _maxApplyLatencyTicks);
                if (value <= current)
                    return;

                long previous = Interlocked.CompareExchange(ref _maxApplyLatencyTicks, value, current);
                if (previous == current)
                    return;
            }
        }

        private void UpdateMaxQueuedCorrectionsPerTick(int value)
        {
            while (true)
            {
                int current = Volatile.Read(ref _maxQueuedCorrectionsPerTick);
                if (value <= current)
                    return;

                int previous = Interlocked.CompareExchange(ref _maxQueuedCorrectionsPerTick, value, current);
                if (previous == current)
                    return;
            }
        }

        private void UpdateMaxCoalescedCorrectionsPerTick(int value)
        {
            while (true)
            {
                int current = Volatile.Read(ref _maxCoalescedCorrectionsPerTick);
                if (value <= current)
                    return;

                int previous = Interlocked.CompareExchange(ref _maxCoalescedCorrectionsPerTick, value, current);
                if (previous == current)
                    return;
            }
        }

        private void UpdateMaxQueueDepth(int value)
        {
            while (true)
            {
                int current = Volatile.Read(ref _maxQueueDepth);
                if (value <= current)
                    return;

                int previous = Interlocked.CompareExchange(ref _maxQueueDepth, value, current);
                if (previous == current)
                    return;
            }
        }
    }
}
