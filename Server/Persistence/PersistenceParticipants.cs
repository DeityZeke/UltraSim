using UltraSim.ECS;

namespace UltraSim.Server.Persistence;

public readonly record struct SaveSetContext(ulong Sequence, long UnixMilliseconds);

public interface ISaveSetParticipant
{
    string Key { get; }
    long MaximumArtifactBytes => int.MaxValue;
    ICapturedSaveSetParticipant Capture(in SaveSetContext context);
    IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context);
}

/// <summary>Owned participant state from one common epoch; encoding occurs after that epoch is released.</summary>
public interface ICapturedSaveSetParticipant : IDisposable
{
    string Key { get; }
    void WriteArtifact(Stream destination);
}

public interface IPreparedLoadParticipant : IDisposable
{
    string Key { get; }
    void Resolve(IPersistentEntityResolver resolver);
    void Validate();
}

public interface IPersistentEntityResolver
{
    bool TryResolve(PersistentEntityKey key, out object? value);
}

/// <summary>A completely prepared generation; publication is one host-owned atomic swap.</summary>
public sealed class PreparedLoadGeneration : IDisposable
{
    private readonly IPreparedLoadParticipant[] _participants;
    private bool _committed;
    private bool _disposed;
    internal PreparedLoadGeneration(ulong sequence, IPreparedLoadParticipant[] participants)
    { Sequence = sequence; _participants = participants; }
    public ulong Sequence { get; }
    public IReadOnlyList<IPreparedLoadParticipant> Participants => _participants;
    public void Commit(Action<IReadOnlyList<IPreparedLoadParticipant>> publishGeneration)
    {
        ArgumentNullException.ThrowIfNull(publishGeneration);
        if (_disposed || _committed) throw new InvalidOperationException("Prepared generation is no longer publishable.");
        publishGeneration(_participants);
        _committed = true;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_committed)
            foreach (IPreparedLoadParticipant participant in _participants) participant.Dispose();
    }
}

public enum SaveSetRecoveryMode : byte { StrictCurrent = 0, CurrentThenNewestComplete = 1 }

public enum SaveSetFaultPoint : byte
{
    None = 0,
    AfterPendingDirectory,
    AfterParticipantFile,
    AfterManifestFile,
    AfterPendingDirectoryFlush,
    AfterGenerationRename,
    AfterGenerationDirectoryFlush,
    AfterCurrentPointerFile,
    AfterCurrentPointerRename,
}

public interface ISaveSetFaultInjector
{
    void Reach(SaveSetFaultPoint point, string path);
}
