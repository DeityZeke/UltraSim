using UltraSim.ECS;

namespace UltraSim.Server.Persistence;

public readonly record struct SaveSetContext(ulong Sequence, long UnixMilliseconds);

public interface ISaveSetParticipant
{
    string Key { get; }
    long MaximumArtifactBytes => Array.MaxLength;
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

/// <summary>
/// Explicit opt-in resolver for immutable authority outside the save set.
/// </summary>
/// <remarks>
/// Implementations must never consult the active generation being replaced.
/// Authoritative links owned by a staged participant resolve only from that
/// participant; its namespace cannot fall through to this resolver.
/// </remarks>
public interface IExternalPersistentEntityResolver : IPersistentEntityResolver { }

/// <summary>
/// A prepared participant can expose durable identities to later prepared
/// participants during one staged load. Implementations must resolve only
/// candidate state, never an independently active generation.
/// </summary>
public interface IPreparedPersistentEntityResolver
{
    bool TryResolve(PersistentEntityKey key, out object? value);
}

/// <summary>Declares that a staged participant owns a durable identity namespace.</summary>
/// <remarks>
/// When a staged owner recognizes a world namespace but cannot resolve an
/// entity, resolution must fail. It must never fall through to active or
/// external authority for the same namespace.
/// </remarks>
public interface IPreparedPersistentEntityNamespace
{
    bool Owns(PersistentEntityKey key);
}

/// <summary>Owned participant captures from one common logical epoch.</summary>
public sealed class CapturedSaveSet : IDisposable
{
    private readonly CapturedSaveSetParticipant[] _participants;
    private bool _consumed;

    internal CapturedSaveSet(SaveSetContext context, CapturedSaveSetParticipant[] participants)
    {
        Context = context;
        _participants = participants;
    }

    public SaveSetContext Context { get; }
    public int ParticipantCount => _participants.Length;

    internal CapturedSaveSetParticipant[] TakeParticipants()
    {
        if (_consumed)
            throw new InvalidOperationException("The captured save set was already consumed.");
        _consumed = true;
        return _participants;
    }

    public void Dispose()
    {
        if (_consumed)
            return;
        _consumed = true;
        foreach (CapturedSaveSetParticipant participant in _participants)
            participant.Capture.Dispose();
    }
}

internal readonly record struct CapturedSaveSetParticipant(
    string Key,
    long MaximumArtifactBytes,
    ICapturedSaveSetParticipant Capture);

/// <summary>A completely prepared generation; publication is one host-owned atomic swap.</summary>
public sealed class PreparedLoadGeneration : IDisposable
{
    private readonly IPreparedLoadParticipant[] _participants;
    private readonly System.Collections.ObjectModel.ReadOnlyCollection<IPreparedLoadParticipant> _view;
    private bool _publicationAttempted;
    private bool _disposed;
    internal PreparedLoadGeneration(ulong sequence, IPreparedLoadParticipant[] participants)
    {
        Sequence = sequence;
        _participants = participants;
        _view = Array.AsReadOnly(participants);
    }
    public ulong Sequence { get; }
    public IReadOnlyList<IPreparedLoadParticipant> Participants => _view;
    public void Commit(Action<IReadOnlyList<IPreparedLoadParticipant>> publishGeneration)
    {
        ArgumentNullException.ThrowIfNull(publishGeneration);
        if (_disposed || _publicationAttempted) throw new InvalidOperationException("Prepared generation is no longer publishable.");
        // Publication is an exactly-once ownership transfer. Mark the attempt
        // before invoking host code: once arbitrary publication code has run,
        // rollback cannot be proven and retry could duplicate partially
        // published participants.
        _publicationAttempted = true;
        publishGeneration(_view);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_publicationAttempted)
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
