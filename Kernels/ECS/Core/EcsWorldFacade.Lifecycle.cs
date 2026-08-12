namespace UltraSim.ECS;

public sealed partial class EcsWorld
{
    /// <summary>Captures an independent cursor at the oldest lifecycle record still retained.</summary>
    /// <remarks>
    /// Lifecycle reports successful entity existence and component-membership
    /// commits, not component values or enable-state changes. Copying the cursor
    /// creates an independent consumer at the same position.
    /// </remarks>
    public LifecycleCursor CaptureLifecycleCursorFromBeginning()
    {
        EnsureOwnerThread();
        return _core.CaptureLifecycleCursorFromBeginning();
    }

    /// <summary>Captures an independent cursor after every lifecycle fact currently published.</summary>
    /// <remarks>Use this when only future successful topology commits are relevant.</remarks>
    public LifecycleCursor CaptureLifecycleCursorFromNow()
    {
        EnsureOwnerThread();
        return _core.CaptureLifecycleCursorFromNow();
    }

    /// <summary>Copies a retained lifecycle prefix into caller-owned storage.</summary>
    /// <remarks>
    /// The cursor advances only through records actually copied. A slow cursor
    /// whose history was overwritten receives <see cref="LifecycleReadStatus.ResyncRequired"/>
    /// and remains unchanged; recapturing a cursor is an explicit consumer choice.
    /// This operation is owner-thread-only and allocation-free.
    /// </remarks>
    public LifecycleReadResult ReadLifecycle(
        ref LifecycleCursor cursor,
        Span<LifecycleRecord> destination)
    {
        EnsureOwnerThread();
        return _core.ReadLifecycle(ref cursor, destination);
    }

    /// <summary>Returns cold capacity, retention, overwrite, and sequence diagnostics.</summary>
    public LifecycleHistorySnapshot GetLifecycleHistorySnapshot()
    {
        EnsureOwnerThread();
        return _core.GetLifecycleHistorySnapshot();
    }

    internal void SetLifecycleNextSequenceForTest(ulong sequence)
    {
        EnsureOwnerThread();
        _core.SetLifecycleNextSequenceForTest(sequence);
    }
}
