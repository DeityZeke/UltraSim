using System.Diagnostics;
using System.Threading;

namespace UltraSim.ECS;

/// <summary>
/// Shared owner-state guard for a retainable canonical structural arena.
/// Scheduler producer lanes deliberately do not use this gate.
/// </summary>
internal sealed class StructuralRecordingGate
{
    private int _systemGraphDepth;

    internal bool IsSystemGraphActive => Volatile.Read(ref _systemGraphDepth) != 0;

    internal void EnterSystemGraph()
    {
        int depth = Interlocked.Increment(ref _systemGraphDepth);
        Debug.Assert(depth == 1, "System-graph structural recording gates are not reentrant.");
    }

    internal void ExitSystemGraph()
    {
        int depth = Interlocked.Decrement(ref _systemGraphDepth);
        Debug.Assert(depth == 0, "System-graph structural recording gate depth became invalid.");
    }
}
