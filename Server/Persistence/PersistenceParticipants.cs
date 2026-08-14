using UltraSim.ECS;

namespace UltraSim.Server.Persistence;

/// <summary>Common logical identity and timestamp shared by every participant in one save set.</summary>
public readonly record struct SaveSetContext(ulong Sequence, long UnixMilliseconds);

/// <summary>Captures and prepares one bounded named artifact in a coordinated save set.</summary>
/// <remarks>
/// Capture occurs inside the caller's frozen authority epoch. The returned
/// capture owns immutable state so encoding can continue after that epoch ends.
/// Prepare must not mutate active authority.
/// </remarks>
public interface ISaveSetParticipant
{
    /// <summary>Gets the stable unique manifest/artifact key.</summary>
    string Key { get; }
    /// <summary>Gets the hard encoded artifact bound enforced while writing and reading.</summary>
    long MaximumArtifactBytes => Array.MaxLength;
    /// <summary>Captures immutable participant state from the common epoch.</summary>
    ICapturedSaveSetParticipant Capture(in SaveSetContext context);
    /// <summary>Parses untrusted artifact bytes into private staged state.</summary>
    IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context);
}

/// <summary>Owned participant state from one common epoch; encoding occurs after that epoch is released.</summary>
public interface ICapturedSaveSetParticipant : IDisposable
{
    /// <summary>Gets the participant key that must match its registration.</summary>
    string Key { get; }
    /// <summary>Writes the owned immutable artifact into the coordinator's bounded stream.</summary>
    void WriteArtifact(Stream destination);
}

/// <summary>Private staged participant state in the resolve/validate load phases.</summary>
public interface IPreparedLoadParticipant : IDisposable
{
    /// <summary>Gets the participant key that must match its registration.</summary>
    string Key { get; }
    /// <summary>Resolves durable references only against admitted staged/external providers.</summary>
    void Resolve(IPersistentEntityResolver resolver);
    /// <summary>Performs all remaining failure-prone validation before publication.</summary>
    void Validate();
}

/// <summary>Resolves one complete persistent world/entity key to staged or admitted immutable authority.</summary>
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

/// <summary>Selects strict-current loading or classified fallback to the newest complete valid generation.</summary>
public enum SaveSetRecoveryMode : byte { StrictCurrent = 0, CurrentThenNewestComplete = 1 }

/// <summary>Deterministic crash-injection boundaries in durable save publication.</summary>
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

/// <summary>Test capability invoked at named durable-publication boundaries.</summary>
public interface ISaveSetFaultInjector
{
    void Reach(SaveSetFaultPoint point, string path);
}
