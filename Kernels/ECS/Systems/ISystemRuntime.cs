namespace UltraSim.ECS;

/// <summary>
/// Persistent cold-created state around one ephemeral unmanaged or managed-reference
/// containing job value. Boundary calls occur once per invocation, never per row.
/// </summary>
public interface ISystemRuntime<TJob>
    where TJob : struct
{
    void Initialize(in SystemInitializationContext context) { }

    TJob Prepare(in SystemPrepareContext context);

    /// <summary>
    /// Receives participant-local results in stable participant order. The span
    /// is scheduler-owned and valid only for this synchronous call.
    /// </summary>
    void Complete(in SystemCompleteContext context, ReadOnlySpan<TJob> participantResults) { }

    void Shutdown(in SystemShutdownContext context) { }
}

/// <summary>Bounded non-ECS range work executed by the scheduler worker runtime.</summary>
public interface IRangeJob
{
    void Execute(in SystemJobContext context, int start, int count);
}

public interface IRangeSystemRuntime<TJob> : ISystemRuntime<TJob>
    where TJob : struct, IRangeJob
{
    int GetRangeCount(in SystemPrepareContext context);
}

/// <summary>
/// Persistent owner of one independent exact-change cursor. Scheduled changed-row
/// consumption is deliberately serial so acknowledgement remains success-only.
/// </summary>
public interface IChangedRowsSystemRuntime<TJob> : ISystemRuntime<TJob>
    where TJob : struct
{
    ChangeCursor Cursor { get; set; }
}

public interface IScheduledChangedRowsChunkJob<TRead>
    where TRead : unmanaged, IComponent<TRead>
{
    void Execute(in SystemJobContext context, in ReadOnlyComponentChunk<TRead> chunk);
}
