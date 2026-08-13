using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UltraSim.ECS;

namespace UltraSim.Server.Persistence;

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
        Directory.CreateDirectory(_generations);
        _files = files ?? new DurableFileSystem();
        _faults = faults;
    }

    /// <summary>Captures all participants inside one caller-owned frozen logical epoch.</summary>
    public SaveSetManifest Save(IReadOnlyList<ISaveSetParticipant> participants, in SaveSetContext context,
        Action<Action> captureBoundary)
    {
        using IDisposable writerLease = AcquireWriterLease();
        CleanupAbandonedPending();
        EnsureForwardSequence(context.Sequence);
        string pending = Path.Combine(_generations, context.Sequence.ToString("D20") + ".pending");
        try
        {
            return SaveUnderLease(participants, context, captureBoundary);
        }
        catch
        {
            if (Directory.Exists(pending)) Directory.Delete(pending, recursive: true);
            throw;
        }
    }

    private SaveSetManifest SaveUnderLease(IReadOnlyList<ISaveSetParticipant> participants, in SaveSetContext context,
        Action<Action> captureBoundary)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(captureBoundary);
        if (context.Sequence == 0) throw new ArgumentOutOfRangeException(nameof(context));
        ValidateParticipants(participants);
        string pending = Path.Combine(_generations, context.Sequence.ToString("D20") + ".pending");
        string complete = Path.Combine(_generations, context.Sequence.ToString("D20"));
        if (Directory.Exists(pending) || Directory.Exists(complete))
            throw new IOException("The requested save-set generation already exists.");
        Directory.CreateDirectory(pending);
        Reach(SaveSetFaultPoint.AfterPendingDirectory, pending);
        var artifacts = new SaveSetArtifact[participants.Count];
        var captured = new ICapturedSaveSetParticipant?[participants.Count];
        SaveSetContext captureContext = context;
        try
        {
            bool capturedEpoch = false;
            captureBoundary(() =>
            {
                if (capturedEpoch) throw new InvalidOperationException("The save-set capture boundary invoked its epoch action more than once.");
                capturedEpoch = true;
                for (int index = 0; index < participants.Count; index++)
                {
                    ICapturedSaveSetParticipant capture = participants[index].Capture(captureContext) ??
                        throw new IOException($"Participant '{participants[index].Key}' returned no captured state.");
                    if (!StringComparer.Ordinal.Equals(capture.Key, participants[index].Key))
                    {
                        capture.Dispose();
                        throw new IOException("Captured participant identity differs from its registered key.");
                    }
                    captured[index] = capture;
                }
            });
            if (!capturedEpoch)
                throw new InvalidOperationException("The save-set capture boundary did not invoke its epoch action.");
            for (int index = 0; index < participants.Count; index++)
            {
                ISaveSetParticipant participant = participants[index];
                string name = participant.Key + ".bin";
                string path = Path.Combine(pending, name);
                long length;
                using (Stream stream = _files.CreateDurableFile(path))
                {
                    using (var bounded = new BoundedWriteStream(stream, participant.MaximumArtifactBytes, leaveOpen: true))
                        captured[index]!.WriteArtifact(bounded);
                    _files.FlushFile(stream);
                    length = stream.Length;
                    if (length < 0 || length > participant.MaximumArtifactBytes || length > Array.MaxLength)
                        throw new IOException($"Participant '{participant.Key}' exceeded its configured artifact bound.");
                }
                byte[] digest;
                using (Stream read = _files.OpenRead(path)) digest = SHA256.HashData(read);
                artifacts[index] = new SaveSetArtifact(participant.Key, name, length, Convert.ToHexString(digest));
                Reach(SaveSetFaultPoint.AfterParticipantFile, path);
            }
        }
        finally
        {
            foreach (ICapturedSaveSetParticipant? capture in captured) capture?.Dispose();
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

    public PreparedLoadGeneration PrepareLoad(IReadOnlyList<ISaveSetParticipant> participants,
        IPersistentEntityResolver resolver, SaveSetRecoveryMode recoveryMode = SaveSetRecoveryMode.StrictCurrent)
    {
        ArgumentNullException.ThrowIfNull(participants); ArgumentNullException.ThrowIfNull(resolver);
        ValidateParticipants(participants);
        Exception? currentFailure = null;
        foreach (string generation in EnumerateCandidates(recoveryMode))
        {
            try { return PrepareGeneration(generation, participants, resolver); }
            catch (Exception exception) when (recoveryMode == SaveSetRecoveryMode.CurrentThenNewestComplete &&
                IsRecoverableCandidateFailure(exception))
            { currentFailure ??= exception; }
        }
        throw new IOException("No complete compatible save-set generation could be prepared.", currentFailure);
    }

    private PreparedLoadGeneration PrepareGeneration(string directory, IReadOnlyList<ISaveSetParticipant> participants,
        IPersistentEntityResolver resolver)
    {
        string manifestPath = Path.Combine(directory, ManifestName);
        long manifestLength = new FileInfo(manifestPath).Length;
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
        foreach (SaveSetArtifact artifact in manifest.Artifacts)
        {
            if (!IsKey(artifact.Key) || artifact.FileName != artifact.Key + ".bin" ||
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
                long physicalLength = new FileInfo(path).Length;
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
            for (int i = 0; i < count; i++) prepared[i].Resolve(resolver);
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
        if (File.Exists(pointer))
        {
            try
            {
                var info = new FileInfo(pointer);
                if (info.Length is <= 0 or > 64) throw new InvalidDataException("CURRENT pointer size is invalid.");
                string current = File.ReadAllText(pointer).Trim();
                if (current.Length != 20 || current.Any(static character => character is < '0' or > '9') || current == new string('0', 20))
                    throw new InvalidDataException("CURRENT pointer is malformed.");
                currentPath = ResolveContained(_generations, current);
                if (!Directory.Exists(currentPath))
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
            foreach (string directory in Directory.EnumerateDirectories(_generations)
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
        JsonException or PersistenceValidationException or PersistenceCompatibilityException;

    private IDisposable AcquireWriterLease()
    {
        string path = Path.Combine(_root, WriterLockName);
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    1, FileOptions.WriteThrough);
            }
            catch (IOException) when (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(30))
            {
                Thread.Sleep(10);
            }
        }
    }

    private void CleanupAbandonedPending()
    {
        foreach (string abandoned in Directory.EnumerateDirectories(_generations, "*.pending"))
            Directory.Delete(abandoned, recursive: true);
        foreach (string pointer in Directory.EnumerateFiles(_root, CurrentName + ".tmp.*"))
            File.Delete(pointer);
    }

    private void EnsureForwardSequence(ulong sequence)
    {
        if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        ulong greatest = 0;
        foreach (string directory in Directory.EnumerateDirectories(_generations))
        {
            string name = Path.GetFileName(directory);
            if (name.Length == 20 && ulong.TryParse(name, out ulong candidate) && candidate > greatest)
                greatest = candidate;
        }
        if (sequence <= greatest)
            throw new InvalidOperationException($"Save-set sequence {sequence} does not advance the published generation high-water {greatest}.");
    }
    private void Reach(SaveSetFaultPoint point, string path) => _faults?.Reach(point, path);
}

/// <summary>Prevents a participant from materializing an artifact beyond its declared cold-path bound.</summary>
internal sealed class BoundedWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maximumLength;
    private readonly bool _leaveOpen;

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
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => true;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set
        {
            EnsureLength(value);
            _inner.Position = value;
        }
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin)
    {
        long destination = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_inner.Position + offset),
            SeekOrigin.End => checked(_inner.Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        EnsureLength(destination);
        return _inner.Seek(offset, origin);
    }
    public override void SetLength(long value)
    {
        EnsureLength(value);
        _inner.SetLength(value);
    }
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

    private void EnsureWrite(int count) => EnsureLength(checked(_inner.Position + count));
    private void EnsureLength(long length)
    {
        if (length < 0 || length > _maximumLength)
            throw new IOException($"Participant artifact exceeds its configured {_maximumLength}-byte bound.");
    }
}

public sealed record SaveSetArtifact(string Key, string FileName, long ByteCount, string Sha256);
public sealed record SaveSetManifest(int FormatVersion, ulong Sequence, long UnixMilliseconds, SaveSetArtifact[] Artifacts);

[JsonSerializable(typeof(SaveSetManifest))]
internal partial class SaveSetJsonContext : JsonSerializerContext { }
