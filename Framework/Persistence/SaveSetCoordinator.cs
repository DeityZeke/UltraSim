using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace UltraSim.Framework.Persistence;

/// <summary>Coordinates bounded participant capture, staged restore, and crash-durable save-set publication.</summary>
/// <remarks>
/// One exclusive writer lease serializes publication. Capture and prepare are
/// private-state phases; callers publish a validated <see cref="PreparedLoadGeneration"/>
/// exactly once through their authoritative root.
/// </remarks>
public sealed class SaveSetCoordinator
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private const string ManifestName = "manifest.json";
    private const string CurrentName = "CURRENT";
    private const string WriterLockName = "SAVE.LOCK";
    private readonly string _root;
    private readonly string _generations;
    private readonly IDurableFileSystem _files;
    private readonly ISaveSetFaultInjector? _faults;

    public SaveSetCoordinator(string root, IDurableFileSystem? files = null, ISaveSetFaultInjector? faults = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _generations = Path.Combine(_root, "generations");
        _files = files ?? new DurableFileSystem();
        _faults = faults;
        _files.CreateDirectory(_generations);
    }

    /// <summary>
    /// Reads the next forward publication sequence for a newly opened owner. This is not a
    /// reservation: Publish still checks the durable high-water under its exclusive writer lease.
    /// Complete generation names, including abandoned complete publications, fence reuse;
    /// pending staging names do not. False means the sequence space is exhausted, never wrapped.
    /// </summary>
    public bool TryGetNextSequence(out ulong sequence)
    {
        ulong greatest = ReadGenerationHighWater();
        sequence = greatest == ulong.MaxValue ? 0 : greatest + 1;
        return sequence != 0;
    }

    /// <summary>Captures all participants inside one caller-owned frozen logical epoch.</summary>
    public SaveSetManifest Save(IReadOnlyList<ISaveSetParticipant> participants, in SaveSetContext context,
        Action<Action> captureBoundary)
    {
        using CapturedSaveSet captured = Capture(participants, context, captureBoundary);
        return Publish(captured);
    }

    /// <summary>Captures immutable participant-owned state and releases the authority boundary.</summary>
    public CapturedSaveSet Capture(
        IReadOnlyList<ISaveSetParticipant> participants,
        in SaveSetContext context,
        Action<Action> captureBoundary)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(captureBoundary);
        if (context.Sequence == 0) throw new ArgumentOutOfRangeException(nameof(context));
        ValidateParticipants(participants);
        var captured = new CapturedSaveSetParticipant[participants.Count];
        int count = 0;
        bool capturedEpoch = false;
        SaveSetContext captureContext = context;
        try
        {
            captureBoundary(() =>
            {
                if (capturedEpoch)
                    throw new InvalidOperationException("The save-set capture boundary invoked its epoch action more than once.");
                capturedEpoch = true;
                for (int index = 0; index < participants.Count; index++)
                {
                    ISaveSetParticipant participant = participants[index];
                    ICapturedSaveSetParticipant capture = participant.Capture(captureContext) ??
                        throw new IOException($"Participant '{participant.Key}' returned no captured state.");
                    if (!StringComparer.Ordinal.Equals(capture.Key, participant.Key))
                    {
                        capture.Dispose();
                        throw new IOException("Captured participant identity differs from its registered key.");
                    }
                    captured[count++] = new CapturedSaveSetParticipant(
                        participant.Key, participant.MaximumArtifactBytes, capture);
                }
            });
            if (!capturedEpoch)
                throw new InvalidOperationException("The save-set capture boundary did not invoke its epoch action.");
            return new CapturedSaveSet(context, captured);
        }
        catch
        {
            for (int index = 0; index < count; index++)
                captured[index].Capture.Dispose();
            throw;
        }
    }

    /// <summary>Encodes and durably publishes an already captured save set.</summary>
    public SaveSetManifest Publish(CapturedSaveSet captured)
    {
        ArgumentNullException.ThrowIfNull(captured);
        SaveSetContext context = captured.Context;
        CapturedSaveSetParticipant[] participants = captured.TakeParticipants();
        string pending = Path.Combine(_generations, context.Sequence.ToString("D20") + ".pending");
        string complete = Path.Combine(_generations, context.Sequence.ToString("D20"));
        try
        {
            using IDisposable writerLease = AcquireWriterLease();
            CleanupAbandonedPending();
            EnsureForwardSequence(context.Sequence);
            if (_files.DirectoryExists(pending) || _files.DirectoryExists(complete))
                throw new IOException("The requested save-set generation already exists.");
            _files.CreateDirectory(pending);
            Reach(SaveSetFaultPoint.AfterPendingDirectory, pending);
            var artifacts = new SaveSetArtifact[participants.Length];
            for (int index = 0; index < participants.Length; index++)
            {
                CapturedSaveSetParticipant participant = participants[index];
                string name = participant.Key + ".bin";
                string path = Path.Combine(pending, name);
                long length;
                byte[] digest;
                using (Stream stream = _files.CreateDurableFile(path))
                {
                    using var algorithm = SHA256.Create();
                    using var hashing = new CryptoStream(
                        stream, algorithm, CryptoStreamMode.Write, leaveOpen: true);
                    using (var bounded = new BoundedWriteStream(
                        hashing, participant.MaximumArtifactBytes, leaveOpen: true))
                    {
                        participant.Capture.WriteArtifact(bounded);
                    }
                    hashing.FlushFinalBlock();
                    _files.FlushFile(stream);
                    length = stream.Length;
                    if (length < 0 || length > participant.MaximumArtifactBytes || length > Array.MaxLength)
                        throw new IOException($"Participant '{participant.Key}' exceeded its configured artifact bound.");
                    digest = algorithm.Hash ??
                        throw new IOException($"Participant '{participant.Key}' produced no artifact digest.");
                }
                artifacts[index] = new SaveSetArtifact(participant.Key, name, length, Convert.ToHexString(digest));
                Reach(SaveSetFaultPoint.AfterParticipantFile, path);
            }
            Array.Sort(artifacts, static (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
            var manifest = new SaveSetManifest(1, context.Sequence, context.UnixMilliseconds, artifacts);
            string manifestPath = Path.Combine(pending, ManifestName);
            using (Stream stream = _files.CreateDurableFile(manifestPath))
            {
                JsonSerializer.Serialize(stream, manifest, SaveSetJsonContext.Default.SaveSetManifest);
                _files.FlushFile(stream);
            }
            Reach(SaveSetFaultPoint.AfterManifestFile, manifestPath);
            _files.FlushDirectory(pending);
            Reach(SaveSetFaultPoint.AfterPendingDirectoryFlush, pending);
            _files.MoveDirectory(pending, complete);
            Reach(SaveSetFaultPoint.AfterGenerationRename, complete);
            _files.FlushDirectory(_generations);
            Reach(SaveSetFaultPoint.AfterGenerationDirectoryFlush, _generations);

            string pointerTemp = Path.Combine(_root, CurrentName + ".tmp." + context.Sequence.ToString("D20"));
            using (Stream stream = _files.CreateDurableFile(pointerTemp))
            {
                byte[] pointer = Encoding.ASCII.GetBytes(context.Sequence.ToString("D20") + "\n");
                stream.Write(pointer);
                _files.FlushFile(stream);
            }
            Reach(SaveSetFaultPoint.AfterCurrentPointerFile, pointerTemp);
            _files.ReplaceFile(pointerTemp, Path.Combine(_root, CurrentName));
            Reach(SaveSetFaultPoint.AfterCurrentPointerRename, Path.Combine(_root, CurrentName));
            _files.FlushDirectory(_root);
            return manifest;
        }
        catch
        {
            if (_files.DirectoryExists(pending)) _files.DeleteDirectory(pending, recursive: true);
            throw;
        }
        finally
        {
            foreach (CapturedSaveSetParticipant participant in participants)
                participant.Capture.Dispose();
        }
    }

    public PreparedLoadGeneration PrepareLoad(IReadOnlyList<ISaveSetParticipant> participants,
        IExternalPersistentReferenceResolver resolver, SaveSetRecoveryMode recoveryMode = SaveSetRecoveryMode.StrictCurrent,
        Func<Exception, bool>? additionalRecoverableFailure = null)
    {
        ArgumentNullException.ThrowIfNull(participants); ArgumentNullException.ThrowIfNull(resolver);
        ValidateParticipants(participants);
        Exception? currentFailure = null;
        foreach (string generation in EnumerateCandidates(recoveryMode))
        {
            try { return PrepareGeneration(generation, participants, resolver); }
            catch (Exception exception) when (recoveryMode == SaveSetRecoveryMode.CurrentThenNewestComplete &&
                (IsRecoverableCandidateFailure(exception) || additionalRecoverableFailure?.Invoke(exception) == true))
            { currentFailure ??= exception; }
        }
        throw new IOException("No complete compatible save-set generation could be prepared.", currentFailure);
    }

    private PreparedLoadGeneration PrepareGeneration(string directory, IReadOnlyList<ISaveSetParticipant> participants,
        IExternalPersistentReferenceResolver resolver)
    {
        string manifestPath = Path.Combine(directory, ManifestName);
        long manifestLength = _files.GetFileLength(manifestPath);
        if (manifestLength <= 0 || manifestLength > MaximumManifestBytes)
            throw new InvalidDataException("Save-set manifest size is outside its configured bound.");
        SaveSetManifest manifest;
        using (Stream manifestStream = _files.OpenRead(manifestPath))
            manifest = JsonSerializer.Deserialize(manifestStream,
                SaveSetJsonContext.Default.SaveSetManifest) ?? throw new InvalidDataException("Save-set manifest is empty.");
        if (manifest.FormatVersion != 1 || manifest.Sequence == 0) throw new InvalidDataException("Unsupported save-set manifest.");
        string directoryName = Path.GetFileName(directory);
        if (directoryName.Length != 20 || !ulong.TryParse(directoryName, out ulong directorySequence) ||
            directorySequence != manifest.Sequence)
            throw new InvalidDataException("Save-set manifest sequence disagrees with its generation directory.");
        if (manifest.Artifacts is null || manifest.Artifacts.Length != participants.Count)
            throw new InvalidDataException("Save-set manifest participant count is inconsistent.");
        var artifactKeys = new HashSet<string>(StringComparer.Ordinal);
        var artifactFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (SaveSetArtifact? artifact in manifest.Artifacts)
        {
            if (artifact is null || !IsKey(artifact.Key) || artifact.FileName != artifact.Key + ".bin" ||
                artifact.ByteCount < 0 || string.IsNullOrEmpty(artifact.Sha256) || artifact.Sha256.Length != 64 ||
                !artifactKeys.Add(artifact.Key) || !artifactFiles.Add(artifact.FileName))
                throw new InvalidDataException("Save-set manifest artifact directory is invalid or duplicated.");
        }
        var artifactByKey = manifest.Artifacts.ToDictionary(static artifact => artifact.Key, StringComparer.Ordinal);
        var prepared = new IPreparedLoadParticipant[participants.Count];
        int count = 0;
        try
        {
            var context = new SaveSetContext(manifest.Sequence, manifest.UnixMilliseconds);
            foreach (ISaveSetParticipant participant in participants)
            {
                if (!artifactByKey.TryGetValue(participant.Key, out SaveSetArtifact? artifact))
                    throw new InvalidDataException($"Manifest has no artifact for participant '{participant.Key}'.");
                string path = ResolveContained(directory, artifact.FileName);
                long physicalLength = _files.GetFileLength(path);
                if (physicalLength != artifact.ByteCount || physicalLength > participant.MaximumArtifactBytes ||
                    physicalLength > Array.MaxLength)
                    throw new InvalidDataException($"Artifact size is invalid for participant '{participant.Key}'.");
                byte[] bytes = GC.AllocateUninitializedArray<byte>(checked((int)physicalLength));
                using (Stream read = _files.OpenRead(path)) read.ReadExactly(bytes);
                if (!StringComparer.Ordinal.Equals(Convert.ToHexString(SHA256.HashData(bytes)), artifact.Sha256))
                    throw new InvalidDataException($"Artifact integrity failed for participant '{participant.Key}'.");
                IPreparedLoadParticipant state = participant.Prepare(bytes, context) ??
                    throw new InvalidDataException($"Participant '{participant.Key}' returned no prepared state.");
                if (!StringComparer.Ordinal.Equals(state.Key, participant.Key))
                {
                    state.Dispose();
                    throw new InvalidDataException("Prepared participant identity differs from its registered key.");
                }
                prepared[count++] = state;
            }
            IPersistentReferenceResolver stagedResolver = new PreparedParticipantResolver(resolver, prepared, count);
            for (int i = 0; i < count; i++) prepared[i].Resolve(stagedResolver);
            for (int i = 0; i < count; i++) prepared[i].Validate();
            return new PreparedLoadGeneration(manifest.Sequence, prepared);
        }
        catch
        {
            for (int i = 0; i < count; i++) prepared[i]?.Dispose();
            throw;
        }
    }

    private IEnumerable<string> EnumerateCandidates(SaveSetRecoveryMode mode)
    {
        string pointer = Path.Combine(_root, CurrentName);
        string? currentPath = null;
        if (_files.FileExists(pointer))
        {
            try
            {
                long pointerLength = _files.GetFileLength(pointer);
                if (pointerLength is <= 0 or > 64) throw new InvalidDataException("CURRENT pointer size is invalid.");
                string current = _files.ReadAllText(pointer).Trim();
                if (current.Length != 20 || current.Any(static character => character is < '0' or > '9') || current == new string('0', 20))
                    throw new InvalidDataException("CURRENT pointer is malformed.");
                currentPath = ResolveContained(_generations, current);
                if (!_files.DirectoryExists(currentPath))
                {
                    currentPath = null;
                    if (mode == SaveSetRecoveryMode.StrictCurrent) throw new InvalidDataException("CURRENT generation is absent.");
                }
            }
            catch (InvalidDataException) when (mode == SaveSetRecoveryMode.CurrentThenNewestComplete)
            {
                currentPath = null;
            }
            if (currentPath is not null) yield return currentPath;
        }
        else if (mode == SaveSetRecoveryMode.StrictCurrent)
        {
            yield break;
        }
        if (mode == SaveSetRecoveryMode.CurrentThenNewestComplete)
        {
            foreach (string directory in _files.EnumerateDirectories(_generations, "*")
                .Where(static path => !path.EndsWith(".pending", StringComparison.Ordinal))
                .OrderByDescending(static path => path, StringComparer.Ordinal))
                if (currentPath is null || !StringComparer.Ordinal.Equals(directory, currentPath)) yield return directory;
        }
    }

    private static void ValidateParticipants(IReadOnlyList<ISaveSetParticipant> participants)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (ISaveSetParticipant participant in participants)
        {
            if (participant is null || participant.MaximumArtifactBytes <= 0 || participant.MaximumArtifactBytes > Array.MaxLength ||
                !IsKey(participant.Key) || !keys.Add(participant.Key))
                throw new ArgumentException("Participant keys must be unique lowercase safe file-name tokens.", nameof(participants));
        }
    }
    private static bool IsKey(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 64 &&
        value.All(static c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');
    private static string ResolveContained(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(Path.DirectorySeparatorChar) || relative.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidDataException("Manifest/pointer contains an unsafe relative path.");
        string full = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Path escapes the save-set root.");
        return full;
    }
    private static bool IsRecoverableCandidateFailure(Exception exception) => exception is
        InvalidDataException or FileNotFoundException or DirectoryNotFoundException or
        JsonException;

    private sealed class PreparedParticipantResolver : IPersistentReferenceResolver
    {
        private readonly IExternalPersistentReferenceResolver _fallback;
        private readonly IPreparedLoadParticipant[] _participants;
        private readonly int _count;

        internal PreparedParticipantResolver(
            IExternalPersistentReferenceResolver fallback,
            IPreparedLoadParticipant[] participants,
            int count)
        {
            _fallback = fallback;
            _participants = participants;
            _count = count;
        }

        public bool TryResolve(PersistentReferenceKey key, out object? value)
        {
            // A staged owner fences its entire namespace, even if an earlier
            // non-owner provider claims the same key or the old generation has it.
            IPreparedLoadParticipant? owner = null;
            for (int index = 0; index < _count; index++)
            {
                if (_participants[index] is IPreparedPersistentReferenceNamespace candidate && candidate.Owns(key))
                {
                    if (owner is null) owner = _participants[index];
                    else throw new InvalidDataException("Multiple staged participants own one durable reference namespace.");
                }
            }
            if (owner is not null)
            {
                if (owner is IPreparedPersistentReferenceResolver staged)
                    return staged.TryResolve(key, out value);
                value = null; return false;
            }
            for (int index = 0; index < _count; index++)
            {
                if (_participants[index] is IPreparedPersistentReferenceResolver provider &&
                    provider.TryResolve(key, out value))
                {
                    return true;
                }
            }

            return _fallback.TryResolve(key, out value);
        }
    }

    private IDisposable AcquireWriterLease()
    {
        string path = Path.Combine(_root, WriterLockName);
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                return _files.AcquireExclusiveFile(path);
            }
            catch (IOException) when (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(30))
            {
                Thread.Sleep(10);
            }
        }
    }

    private void CleanupAbandonedPending()
    {
        foreach (string abandoned in _files.EnumerateDirectories(_generations, "*.pending"))
            _files.DeleteDirectory(abandoned, recursive: true);
        foreach (string pointer in _files.EnumerateFiles(_root, CurrentName + ".tmp.*"))
            _files.DeleteFile(pointer);
    }

    private void EnsureForwardSequence(ulong sequence)
    {
        if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        ulong greatest = ReadGenerationHighWater();
        if (sequence <= greatest)
            throw new InvalidOperationException($"Save-set sequence {sequence} does not advance the published generation high-water {greatest}.");
    }

    private ulong ReadGenerationHighWater()
    {
        ulong greatest = 0;
        foreach (string directory in _files.EnumerateDirectories(_generations, "*"))
        {
            string name = Path.GetFileName(directory);
            if (name.Length == 20 && ulong.TryParse(name, out ulong candidate) && candidate > greatest)
                greatest = candidate;
        }
        return greatest;
    }
    private void Reach(SaveSetFaultPoint point, string path) => _faults?.Reach(point, path);
}

/// <summary>Prevents a participant from materializing an artifact beyond its declared cold-path bound.</summary>
internal sealed class BoundedWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maximumLength;
    private readonly bool _leaveOpen;
    private long _position;

    internal BoundedWriteStream(Stream inner, long maximumLength, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (!inner.CanWrite) throw new ArgumentException("The bounded stream requires a writable destination.", nameof(inner));
        if (maximumLength <= 0) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        _inner = inner;
        _maximumLength = maximumLength;
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _position;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count) throw new ArgumentException("The write range exceeds the source buffer.");
        EnsureWrite(count);
        _inner.Write(buffer, offset, count);
    }
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureWrite(buffer.Length);
        _inner.Write(buffer);
    }
    public override void WriteByte(byte value)
    {
        EnsureWrite(1);
        _inner.WriteByte(value);
    }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count) throw new ArgumentException("The write range exceeds the source buffer.");
        EnsureWrite(count);
        return _inner.WriteAsync(buffer, offset, count, cancellationToken);
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureWrite(buffer.Length);
        return _inner.WriteAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen) _inner.Dispose();
        base.Dispose(disposing);
    }

    private void EnsureWrite(int count)
    {
        EnsureLength(checked(_position + count));
        _position += count;
    }
    private void EnsureLength(long length)
    {
        if (length < 0 || length > _maximumLength)
            throw new IOException($"Participant artifact exceeds its configured {_maximumLength}-byte bound.");
    }
}

/// <summary>Canonical manifest entry for one bounded participant artifact.</summary>
public sealed record SaveSetArtifact(string Key, string FileName, long ByteCount, string Sha256);

/// <summary>Complete save-set commit metadata published only after every artifact is durable.</summary>
public sealed record SaveSetManifest(int FormatVersion, ulong Sequence, long UnixMilliseconds, SaveSetArtifact[] Artifacts);

[JsonSerializable(typeof(SaveSetManifest))]
internal partial class SaveSetJsonContext : JsonSerializerContext { }
