namespace UltraSim.ECS;

/// <summary>
/// Persistent cold-created state around one ephemeral unmanaged or managed-reference
/// containing job value. Boundary calls occur once per invocation, never per row.
/// </summary>
/// <remarks>
/// Keep persistent system state in the runtime. <see cref="Prepare"/> returns the
/// ephemeral value copied per participant; <see cref="Complete"/> receives those
/// participant-local results in deterministic participant order. Job contexts and
/// chunk views must never be retained by the runtime.
/// </remarks>
public interface ISystemRuntime<TJob>
    where TJob : struct
{
    /// <summary>Initializes persistent state once after the world is sealed.</summary>
    void Initialize(in SystemInitializationContext context) { }

    /// <summary>Builds the ephemeral job value for one due invocation.</summary>
    TJob Prepare(in SystemPrepareContext context);

    /// <summary>
    /// Receives participant-local results in stable participant order. The span
    /// is scheduler-owned and valid only for this synchronous call.
    /// </summary>
    void Complete(in SystemCompleteContext context, ReadOnlySpan<TJob> participantResults) { }

    /// <summary>Releases persistent runtime state during scheduler shutdown.</summary>
    void Shutdown(in SystemShutdownContext context) { }
}

/// <summary>Opt-in canonical persistent state owned by a registered system runtime.</summary>
/// <remarks>
/// Implementations write explicit little-endian/versioned bytes. Scheduler topology,
/// worker assignment, profiling counters, prepared handles, and change cursors are
/// not persistence state.
/// </remarks>
public interface IPersistentSystemState
{
    int PersistenceStateByteCount { get; }
    bool PersistenceStateRequired => true;
    void WritePersistenceState(Span<byte> destination);
    void ReadPersistenceState(ushort savedVersion, ReadOnlySpan<byte> source);
}

/// <summary>Bounded non-query index work executed by the scheduler worker runtime.</summary>
/// <remarks>
/// Use for external arrays, preselected entity operations, or other already-
/// bounded work. Do not scan the entity population with prepared lookups when a
/// generated chunk job can select the required component topology directly.
/// </remarks>
/// <example>Process a preselected array of 256 combat target operations.</example>
public interface IRangeJob
{
    void Execute(in SystemJobContext context, int start, int count);
}

/// <summary>Persistent runtime that supplies the bounded count for an <see cref="IRangeJob"/>.</summary>
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

/// <summary>Serial scheduler callback over contiguous runs of currently present changed rows.</summary>
/// <remarks>It observes changed values, not structural lifecycle events or tombstones.</remarks>
public interface IScheduledChangedRowsChunkJob<TRead>
    where TRead : unmanaged, IComponent<TRead>
{
    void Execute(in SystemJobContext context, in ReadOnlyComponentChunk<TRead> chunk);
}
