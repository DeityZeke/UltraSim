using System.Text;
using UltraSim.Framework.Persistence;
using UltraSim.Framework.RuntimeHost;

string root = Path.Combine(Path.GetTempPath(), "ultrasim-framework-save-" + Guid.NewGuid().ToString("N"));
try
{
    var live = new RuntimeSidecars();
    var first = new SidecarKey(new("owner.one"), new("notes.quest"), "left");
    var second = new SidecarKey(new("owner.one"), new("notes.quest"), "right");
    live.Add(first, new Note(17)); live.Add(second, new Note(29));
    var participant = new NoteParticipant(live, first, second);
    var coordinator = new SaveSetCoordinator(root);
    coordinator.Save([participant], new(1, 100), action => action());
    live.Remove(first); live.Remove(second);
    live.Add(first, new Note(88)); live.Add(second, new Note(99));
    using (PreparedLoadGeneration prepared = coordinator.PrepareLoad([participant], EmptyResolver.Instance))
    {
        var staged = (PreparedNotes)prepared.Participants.Single();
        Check(staged.First == 17 && staged.Second == 29);
        prepared.Commit(_ =>
        {
            live.Remove(first); live.Remove(second);
            live.Add(first, new Note(staged.First)); live.Add(second, new Note(staged.Second));
        });
    }
    Check(live.TryGet<Note>(first, out var one) && one!.Value == 17);
    Check(live.TryGet<Note>(second, out var two) && two!.Value == 29);
    participant.WriteUnsupportedVersion = true;
    coordinator.Save([participant], new(2, 200), action => action());
    try
    {
        using var rejected = coordinator.PrepareLoad([participant], EmptyResolver.Instance);
        throw new Exception("Unsupported sidecar version reached publication.");
    }
    catch (InvalidDataException) { }
    Check(live.TryGet<Note>(first, out one) && one!.Value == 17);
    Check(live.TryGet<Note>(second, out two) && two!.Value == 29);
    using (PreparedLoadGeneration recovered = coordinator.PrepareLoad(
        [participant], EmptyResolver.Instance, SaveSetRecoveryMode.CurrentThenNewestComplete))
        Check(recovered.Sequence == 1);
    Console.WriteLine("PASS P2 Framework-only multiple same-type sidecars, durable roundtrip, staging rejection and recovery");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

static void Check(bool condition)
{
    if (!condition) throw new InvalidOperationException("Framework persistence assertion failed.");
}

sealed record Note(int Value);
sealed class EmptyResolver : IExternalPersistentReferenceResolver
{
    internal static readonly EmptyResolver Instance = new();
    public bool TryResolve(PersistentReferenceKey key, out object? value) { value = null; return false; }
}
sealed class NoteParticipant : ISaveSetParticipant
{
    private readonly RuntimeSidecars _live;
    private readonly SidecarKey _first, _second;
    internal NoteParticipant(RuntimeSidecars live, SidecarKey first, SidecarKey second)
    { _live = live; _first = first; _second = second; }
    public string Key => "notes";
    public long MaximumArtifactBytes => 128;
    internal bool WriteUnsupportedVersion { get; set; }
    public ICapturedSaveSetParticipant Capture(in SaveSetContext context)
    {
        if (!_live.TryGet<Note>(_first, out var first) || !_live.TryGet<Note>(_second, out var second))
            throw new InvalidOperationException("Missing sidecar.");
        return new CapturedNotes(Key, WriteUnsupportedVersion ? (byte)99 : (byte)1,
            _first, first!.Value, _second, second!.Value);
    }
    public IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context)
    {
        using var stream = new MemoryStream(artifact.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadByte() != 1 || reader.ReadByte() != 2)
            throw new InvalidDataException("Unsupported or malformed sidecar artifact.");
        int first = Read(reader, _first);
        int second = Read(reader, _second);
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing sidecar bytes.");
        return new PreparedNotes(Key, first, second);
    }
    private static int Read(BinaryReader reader, SidecarKey expected)
    {
        if (reader.ReadString() != expected.Owner.Value || reader.ReadString() != expected.Type.Value ||
            reader.ReadString() != expected.Instance)
            throw new InvalidDataException("Wrong sidecar owner/family/instance identity.");
        return reader.ReadInt32();
    }
}
sealed class CapturedNotes(string key, byte version, SidecarKey firstKey, int first,
    SidecarKey secondKey, int second) : ICapturedSaveSetParticipant
{
    public string Key => key;
    public void WriteArtifact(Stream destination)
    {
        using var writer = new BinaryWriter(destination, Encoding.ASCII, leaveOpen: true);
        writer.Write(version); writer.Write((byte)2);
        Write(writer, firstKey, first);
        Write(writer, secondKey, second);
    }
    private static void Write(BinaryWriter writer, SidecarKey identity, int value)
    {
        writer.Write(identity.Owner.Value);
        writer.Write(identity.Type.Value);
        writer.Write(identity.Instance);
        writer.Write(value);
    }
    public void Dispose() { }
}
sealed class PreparedNotes(string key, int first, int second) : IPreparedLoadParticipant
{
    public string Key => key;
    public int First => first;
    public int Second => second;
    public void Resolve(IPersistentReferenceResolver resolver) { }
    public void Validate() { if (first < 0 || second < 0) throw new InvalidDataException("Negative note."); }
    public void Dispose() { }
}
