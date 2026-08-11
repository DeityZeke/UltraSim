using System.Runtime.CompilerServices;
using System.Threading;

namespace UltraSim.ECS;

/// <summary>One persistent CAS gate per system invocation; never one version per worker.</summary>
internal sealed class SchedulerChangePublication
{
    private long _publishedValue;
    private ChangeVersion _candidate;

    internal ChangeVersion Candidate => _candidate;

    internal bool WasPublished => Volatile.Read(ref _publishedValue) != 0;

    internal void Reset(ChangeVersion candidate)
    {
        if (!candidate.IsValid)
            throw new ArgumentException("A scheduler publication candidate must be valid.", nameof(candidate));
        _candidate = candidate;
        Volatile.Write(ref _publishedValue, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ChangeVersion Acquire()
    {
        long candidate = unchecked((long)_candidate.Value);
        if (Volatile.Read(ref _publishedValue) == 0)
            Interlocked.CompareExchange(ref _publishedValue, candidate, 0);
        return _candidate;
    }
}
