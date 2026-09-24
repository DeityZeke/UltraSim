using System.Buffers.Binary;
using System.Text;
using UltraSim.ECS;
using UltraSim.Game.World;
using UltraSim.Server.Persistence;
using UltraSim.Spatial;

namespace UltraSim.Game.Persistence;

/// <summary>Hard bounds for the durable prepared-world artifact.</summary>
public sealed class WorldPersistenceOptions
{
    public WorldPersistenceOptions(
        long maximumArtifactBytes = 256L * 1024 * 1024,
        WorldMutableOverlayLimits? overlayLimits = null)
    {
        if (maximumArtifactBytes <= 0 || maximumArtifactBytes > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(maximumArtifactBytes));
        MaximumArtifactBytes = maximumArtifactBytes;
        OverlayLimits = overlayLimits ?? WorldMutableOverlayLimits.Default;
        OverlayLimits.Validate();
    }

    public long MaximumArtifactBytes { get; }
    public WorldMutableOverlayLimits OverlayLimits { get; }
}

/// <summary>
/// Durable current-world participant. Immutable WorldPack bytes are referenced by exact identity;
/// only bounded complete mutable replacements are carried in the save set.
/// </summary>
public sealed class WorldSaveSetParticipant : ISaveSetParticipant
{
    private const uint Magic = 0x56575355; // "USWV"
    private const ushort FormatVersion = 1;
    private const ushort CompositionFormatVersion = 2;
    private const byte HasBaseWorld = 1 << 0;
    private const byte ReplacesMultiInstances = 1 << 1;
    private const int StaticRecordBytes = 82;
    private const int MultiFragmentRecordBytes = 86;
    private const int MultiInstanceRecordBytes = 30;

    private readonly GameWorldState? _source;
    private readonly PersistentWorldId _sourceWorldId;
    private readonly Func<WorldPackFingerprint, BoundWorldPack?>? _packResolver;
    private readonly Func<WorldStructuralManifestFingerprint, WorldStructuralManifest?>? _manifestResolver;
    private readonly WorldPersistenceOptions _options;

    public WorldSaveSetParticipant(
        GameWorldState source,
        PersistentWorldId durableWorldId,
        WorldPersistenceOptions? options = null,
        string key = "worldview")
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (!durableWorldId.IsValid)
            throw new ArgumentException("A valid durable world identity is required.", nameof(durableWorldId));
        _sourceWorldId = durableWorldId;
        _options = options ?? new WorldPersistenceOptions();
        Key = ValidateKey(key);
    }

    /// <summary>Creates a restore-only participant using the configured exact-pack catalog.</summary>
    public WorldSaveSetParticipant(
        Func<WorldPackFingerprint, BoundWorldPack?> packResolver,
        WorldPersistenceOptions? options = null,
        string key = "worldview")
    {
        _packResolver = packResolver ?? throw new ArgumentNullException(nameof(packResolver));
        _options = options ?? new WorldPersistenceOptions();
        Key = ValidateKey(key);
    }

    /// <summary>Restore-only exact immutable manifest and Pack resolvers for canonical worldview/v2.</summary>
    public WorldSaveSetParticipant(
        Func<WorldStructuralManifestFingerprint, WorldStructuralManifest?> manifestResolver,
        Func<WorldPackFingerprint, BoundWorldPack?> packResolver,
        WorldPersistenceOptions? options = null,
        string key = "worldview")
        : this(packResolver, options, key)
    {
        _manifestResolver = manifestResolver ?? throw new ArgumentNullException(nameof(manifestResolver));
    }

    public string Key { get; }
    public long MaximumArtifactBytes => _options.MaximumArtifactBytes;

    public ICapturedSaveSetParticipant Capture(in SaveSetContext context)
    {
        if (context.Sequence == 0)
            throw new ArgumentOutOfRangeException(nameof(context));
        GameWorldState source = _source ??
            throw new InvalidOperationException("A restore-only current-world participant cannot capture a save.");
        if (source.PreparedComposition is PreparedWorldComposition composition)
        {
            // The artifact describes durable overlay state for the whole manifest, not the
            // currently prepared subset. Until a separate durable overlay catalog exists,
            // refusing a partial runtime is safer than silently omitting an unprepared Pack.
            if (!composition.TryValidateComplete(out PreparedWorldCompositionError validationError))
                throw new PersistenceValidationException(
                    $"The canonical current-world composition cannot be captured: {validationError}.");
            for (int index = 0; index < composition.PreparedPackCount; index++)
            {
                composition.TryGetPreparedPackAt(index, out PreparedWorldPack pack);
                ValidateOverlayBounds(pack.View.Overlay, _options);
            }
            return new CapturedComposition(Key, context.Sequence, _sourceWorldId, composition);
        }
        if (source.PreparedWorld is null)
            return new Captured(Key, context.Sequence, _sourceWorldId, null, null);
        WorldMutableOverlay overlay = source.Overlay ??
            throw new InvalidOperationException("A prepared world lost its durable overlay.");
        ValidateOverlayBounds(overlay, _options);
        return new Captured(Key, context.Sequence, _sourceWorldId, source.PreparedWorld.BaseWorld, overlay);
    }

    public IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context)
    {
        if (context.Sequence == 0)
            throw new ArgumentOutOfRangeException(nameof(context));
        if (artifact.Length == 0 || artifact.Length > _options.MaximumArtifactBytes)
            throw new PersistenceValidationException("The current-world artifact size is outside its configured bound.");
        Parsed? parsed = null;
        ParsedComposition? parsedComposition = null;
        try
        {
            if (artifact.Length < 6)
                throw new PersistenceValidationException("The current-world artifact is truncated.");
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(artifact.Span[4..]);
            if (version == FormatVersion)
                parsed = Parse(artifact.Span, context.Sequence, _options);
            else if (version == CompositionFormatVersion)
                parsedComposition = ParseComposition(artifact.Span, context.Sequence, _options);
            else
                throw new PersistenceCompatibilityException("The current-world artifact format is unsupported.");
        }
        catch (PersistenceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw new PersistenceValidationException(
                $"The current-world artifact contains invalid state: {exception.Message}");
        }
        return new PreparedWorldParticipant(Key, parsed, parsedComposition, _manifestResolver, _packResolver);
    }

    private static ParsedComposition ParseComposition(ReadOnlySpan<byte> bytes, ulong expectedSequence,
        WorldPersistenceOptions options)
    {
        var reader = new WorldArtifactReader(bytes);
        if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != CompositionFormatVersion ||
            reader.ReadUInt16() != 0 || reader.ReadUInt64() != expectedSequence)
            throw new PersistenceValidationException("The canonical current-world header is invalid.");
        var worldId = new PersistentWorldId(reader.ReadUInt64());
        var space = new SpatialSpaceId(reader.ReadUInt64());
        var revision = new WorldCompositionRevision(reader.ReadUInt64());
        if (!space.IsValid || !revision.IsValid ||
            !WorldStructuralManifestFingerprint.TryRead(reader.ReadBytes(32), out var manifestFingerprint))
            throw new PersistenceValidationException("The canonical current-world identity is invalid.");
        int count = reader.ReadCount("Pack overlay");
        if (count > WorldStructuralManifestFormat.MaximumPacks)
            throw new PersistenceValidationException("The canonical current-world Pack row count is excessive.");
        var rows = new ParsedCompositionRow[count];
        WorldPackCoord previous = default;
        for (int index = 0; index < count; index++)
        {
            var coord = new WorldPackCoord(reader.ReadInt32(), reader.ReadInt32());
            int length = reader.ReadCount("Pack overlay bytes");
            if (length == 0 || index != 0 && previous.CompareTo(coord) >= 0)
                throw new PersistenceValidationException("Canonical Pack overlay rows are invalid or unsorted.");
            Parsed row = Parse(reader.ReadBytes(length), expectedSequence, options);
            if (!row.HasBaseWorld || row.DurableWorldId != worldId || row.Space != space ||
                !row.OverlayRevision.IsValid && row.Chunks.Length == 0 && row.MultiInstances is null)
                throw new PersistenceValidationException("A canonical Pack overlay row has inconsistent identity.");
            rows[index] = new ParsedCompositionRow(coord, row);
            previous = coord;
        }
        reader.EnsureEnd();
        return new ParsedComposition(worldId, space, revision, manifestFingerprint, rows);
    }

    private static Parsed Parse(
        ReadOnlySpan<byte> bytes,
        ulong expectedSequence,
        WorldPersistenceOptions options)
    {
        var reader = new WorldArtifactReader(bytes);
        if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != FormatVersion)
            throw new PersistenceCompatibilityException("The current-world artifact format is unsupported.");
        byte flags = reader.ReadByte();
        if ((flags & ~(HasBaseWorld | ReplacesMultiInstances)) != 0)
            throw new PersistenceValidationException("The current-world artifact contains unknown flags.");
        if (reader.ReadByte() != 0)
            throw new PersistenceValidationException("The current-world artifact reserved byte is nonzero.");
        if (reader.ReadUInt64() != expectedSequence)
            throw new PersistenceValidationException("Current-world artifact sequence disagrees with the save set.");

        var durableWorldId = new PersistentWorldId(reader.ReadUInt64());
        bool hasBase = (flags & HasBaseWorld) != 0;
        bool replacesRoots = (flags & ReplacesMultiInstances) != 0;
        ulong spaceValue = reader.ReadUInt64();
        ulong contentLow = reader.ReadUInt64();
        ulong contentHigh = reader.ReadUInt64();
        var fingerprint = new WorldPackFingerprint(
            reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64());
        var overlayRevision = new WorldOverlayRevision(reader.ReadUInt64());
        int chunkCount = reader.ReadCount("changed chunk");
        int rootCount = reader.ReadCount("Multi root");

        if (chunkCount > options.OverlayLimits.MaximumChangedChunks ||
            rootCount > options.OverlayLimits.MaximumMultiInstances)
            throw new PersistenceValidationException("The current-world overlay exceeds configured record bounds.");
        if (!hasBase)
        {
            if (spaceValue != 0 || contentLow != 0 || contentHigh != 0 || fingerprint.IsValid ||
                overlayRevision.IsValid || chunkCount != 0 || rootCount != 0 || replacesRoots)
                throw new PersistenceValidationException("A base-less current world contains overlay state.");
            reader.EnsureEnd();
            return new Parsed(durableWorldId, false, default, default, default, default, [], null,
                options.OverlayLimits);
        }
        if (spaceValue == 0 || contentLow == 0 && contentHigh == 0 || !fingerprint.IsValid)
            throw new PersistenceValidationException("The current-world base-pack identity is invalid.");
        if (!replacesRoots && rootCount != 0)
            throw new PersistenceValidationException("The current-world Multi-root replacement flag is inconsistent.");
        bool hasOverlay = chunkCount != 0 || replacesRoots;
        if (hasOverlay && !overlayRevision.IsValid)
            throw new PersistenceValidationException("The current-world overlay revision is inconsistent.");

        var chunks = new WorldMutableOverlay.ChunkReplacement[chunkCount];
        SpatialChunkKey previous = default;
        for (int index = 0; index < chunks.Length; index++)
        {
            var key = new SpatialChunkKey(new SpatialSpaceId(reader.ReadUInt64()), reader.ReadInt32(), reader.ReadInt32());
            var revision = new WorldChunkContentRevision(reader.ReadUInt64());
            var hash = new WorldChunkContentHash(
                reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64());
            WorldChunkReplacementKinds kinds = (WorldChunkReplacementKinds)reader.ReadByte();
            if (reader.ReadByte() != 0 || reader.ReadUInt16() != 0 ||
                key.Space.Value != spaceValue || !revision.IsValid || !hash.IsValid ||
                revision.Value > overlayRevision.Value ||
                kinds == WorldChunkReplacementKinds.None ||
                (kinds & ~(WorldChunkReplacementKinds.Terrain |
                    WorldChunkReplacementKinds.FrozenStatics |
                    WorldChunkReplacementKinds.MultiFragments)) != 0 ||
                index != 0 && previous.CompareTo(key) >= 0)
                throw new PersistenceValidationException("A current-world changed-chunk row is invalid or noncanonical.");

            byte[]? terrainMaterials = null;
            byte[]? terrainHeights = null;
            if ((kinds & WorldChunkReplacementKinds.Terrain) != 0)
            {
                terrainMaterials = reader.ReadBytes(TerrainChunkFormat.MaterialPlaneBytes);
                terrainHeights = reader.ReadBytes(TerrainChunkFormat.HeightPlaneBytes);
            }
            WorldFrozenStaticRecord[]? statics = null;
            if ((kinds & WorldChunkReplacementKinds.FrozenStatics) != 0)
            {
                int count = reader.ReadCount("frozen static");
                if (count > options.OverlayLimits.MaximumStaticsPerChunk)
                    throw new PersistenceValidationException("A current-world static replacement exceeds its configured bound.");
                reader.EnsureRemaining(checked((long)count * StaticRecordBytes));
                statics = new WorldFrozenStaticRecord[count];
                for (int row = 0; row < count; row++) statics[row] = reader.ReadStatic();
            }
            WorldMultiFragmentRecord[]? fragments = null;
            if ((kinds & WorldChunkReplacementKinds.MultiFragments) != 0)
            {
                int count = reader.ReadCount("Multi fragment");
                if (count > options.OverlayLimits.MaximumMultiFragmentsPerChunk)
                    throw new PersistenceValidationException("A current-world Multi replacement exceeds its configured bound.");
                reader.EnsureRemaining(checked((long)count * MultiFragmentRecordBytes));
                fragments = new WorldMultiFragmentRecord[count];
                for (int row = 0; row < count; row++) fragments[row] = reader.ReadFragment();
            }
            chunks[index] = new WorldMutableOverlay.ChunkReplacement(
                key, revision, hash, terrainMaterials, terrainHeights, statics, fragments);
            previous = key;
        }

        WorldMultiInstanceRecord[]? roots = null;
        if (replacesRoots)
        {
            reader.EnsureRemaining(checked((long)rootCount * MultiInstanceRecordBytes));
            roots = new WorldMultiInstanceRecord[rootCount];
            for (int index = 0; index < roots.Length; index++)
            {
                roots[index] = reader.ReadMultiInstance();
                if (!roots[index].Id.IsValid || roots[index].PieceCount <= 0 ||
                    index != 0 && roots[index - 1].Id.CompareTo(roots[index].Id) >= 0)
                    throw new PersistenceValidationException(
                        "The current-world Multi roots are invalid or noncanonical.");
            }
        }
        reader.EnsureEnd();
        return new Parsed(
            durableWorldId, true, new SpatialSpaceId(spaceValue),
            new WorldContentFingerprint(contentLow, contentHigh), fingerprint,
            overlayRevision, chunks, roots, options.OverlayLimits);
    }

    private static void ValidateOverlayBounds(WorldMutableOverlay overlay, WorldPersistenceOptions options)
    {
        if (overlay.ChangedChunkCount > options.OverlayLimits.MaximumChangedChunks ||
            overlay.ReplacementMultiInstanceCount > options.OverlayLimits.MaximumMultiInstances ||
            overlay.ReservedBytes > options.OverlayLimits.MaximumRetainedBytes)
            throw new PersistenceValidationException("The current-world overlay exceeds configured persistence bounds.");
        foreach (WorldMutableOverlay.ChunkReplacement chunk in overlay.Chunks)
        {
            if ((chunk.FrozenStatics?.Length ?? 0) > options.OverlayLimits.MaximumStaticsPerChunk ||
                (chunk.MultiFragments?.Length ?? 0) > options.OverlayLimits.MaximumMultiFragmentsPerChunk)
                throw new PersistenceValidationException("A changed chunk exceeds configured persistence bounds.");
        }
    }

    private static string ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("A save-set participant key is required.", nameof(key));
        return key;
    }

    private sealed class Captured : ICapturedSaveSetParticipant
    {
        private readonly ulong _sequence;
        private readonly PersistentWorldId _worldId;
        private BoundWorldPack? _baseWorld;
        private WorldMutableOverlay? _overlay;
        private bool _written;

        internal Captured(
            string key,
            ulong sequence,
            PersistentWorldId worldId,
            BoundWorldPack? baseWorld,
            WorldMutableOverlay? overlay)
        {
            Key = key;
            _sequence = sequence;
            _worldId = worldId;
            _baseWorld = baseWorld;
            _overlay = overlay;
        }

        public string Key { get; }

        public void WriteArtifact(Stream destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (_written)
                throw new InvalidOperationException("The current-world capture was already consumed.");
            _written = true;
            BoundWorldPack? baseWorld = _baseWorld;
            WorldMutableOverlay? overlay = _overlay;
            using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            byte flags = 0;
            if (baseWorld is not null) flags |= HasBaseWorld;
            if (overlay?.ReplacesMultiInstances == true) flags |= ReplacesMultiInstances;
            writer.Write(flags);
            writer.Write((byte)0);
            writer.Write(_sequence);
            writer.Write(_worldId.Value);
            writer.Write(baseWorld?.Space.Value ?? 0UL);
            writer.Write(baseWorld?.Content.Fingerprint.Low ?? 0UL);
            writer.Write(baseWorld?.Content.Fingerprint.High ?? 0UL);
            Write(writer, baseWorld?.Fingerprint ?? default);
            writer.Write(overlay?.Revision.Value ?? 0UL);
            writer.Write(overlay?.ChangedChunkCount ?? 0);
            writer.Write(overlay?.ReplacesMultiInstances == true ? overlay.ReplacementMultiInstanceCount : 0);
            if (overlay is not null)
            {
                foreach (WorldMutableOverlay.ChunkReplacement chunk in overlay.Chunks)
                {
                    writer.Write(chunk.Key.Space.Value);
                    writer.Write(chunk.Key.Coord.X);
                    writer.Write(chunk.Key.Coord.Z);
                    writer.Write(chunk.Revision.Value);
                    Write(writer, chunk.Hash);
                    writer.Write((byte)chunk.Kinds);
                    writer.Write((byte)0);
                    writer.Write((ushort)0);
                    if (chunk.TerrainMaterials is not null)
                    {
                        writer.Write(chunk.TerrainMaterials);
                        writer.Write(chunk.TerrainHeights!);
                    }
                    if (chunk.FrozenStatics is not null)
                    {
                        writer.Write(chunk.FrozenStatics.Length);
                        foreach (WorldFrozenStaticRecord record in chunk.FrozenStatics)
                            WorldOverlayBinary.Write(writer, record);
                    }
                    if (chunk.MultiFragments is not null)
                    {
                        writer.Write(chunk.MultiFragments.Length);
                        foreach (WorldMultiFragmentRecord record in chunk.MultiFragments)
                            WorldOverlayBinary.Write(writer, record);
                    }
                }
                if (overlay.ReplacesMultiInstances)
                    foreach (WorldMultiInstanceRecord root in overlay.MultiInstances)
                        WorldOverlayBinary.Write(writer, root);
            }
            writer.Flush();
            _baseWorld = null;
            _overlay = null;
        }

        public void Dispose()
        {
            _written = true;
            _baseWorld = null;
            _overlay = null;
        }

        private static void Write(BinaryWriter writer, WorldPackFingerprint fingerprint)
        {
            writer.Write(fingerprint.Part0); writer.Write(fingerprint.Part1);
            writer.Write(fingerprint.Part2); writer.Write(fingerprint.Part3);
        }

        private static void Write(BinaryWriter writer, WorldChunkContentHash hash)
        {
            writer.Write(hash.Part0); writer.Write(hash.Part1);
            writer.Write(hash.Part2); writer.Write(hash.Part3);
        }
    }

    private sealed class CapturedComposition : ICapturedSaveSetParticipant
    {
        private readonly ulong _sequence;
        private readonly PersistentWorldId _worldId;
        private PreparedWorldComposition? _composition;

        internal CapturedComposition(string key, ulong sequence, PersistentWorldId worldId,
            PreparedWorldComposition composition)
        {
            Key = key;
            _sequence = sequence;
            _worldId = worldId;
            _composition = composition;
        }

        public string Key { get; }

        public void WriteArtifact(Stream destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            PreparedWorldComposition composition = _composition ??
                throw new InvalidOperationException("The canonical World capture was already consumed.");
            _composition = null;
            using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(CompositionFormatVersion);
            writer.Write((ushort)0);
            writer.Write(_sequence);
            writer.Write(_worldId.Value);
            writer.Write(composition.Space.Value);
            writer.Write(composition.Revision.Value);
            Span<byte> fingerprint = stackalloc byte[32];
            composition.ManifestFingerprint.CopyTo(fingerprint);
            writer.Write(fingerprint);
            int count = 0;
            for (int index = 0; index < composition.PreparedPackCount; index++)
            {
                composition.TryGetPreparedPackAt(index, out PreparedWorldPack pack);
                if (IsDurable(pack.View.Overlay)) count++;
            }
            writer.Write(count);
            for (int index = 0; index < composition.PreparedPackCount; index++)
            {
                composition.TryGetPreparedPackAt(index, out PreparedWorldPack pack);
                if (!IsDurable(pack.View.Overlay)) continue;
                using var rowStream = new MemoryStream();
                using (var row = new Captured(Key, _sequence, _worldId,
                           pack.View.BaseWorld, pack.View.Overlay))
                    row.WriteArtifact(rowStream);
                writer.Write(pack.Coord.X);
                writer.Write(pack.Coord.Z);
                writer.Write(checked((int)rowStream.Length));
                writer.Write(rowStream.GetBuffer().AsSpan(0, checked((int)rowStream.Length)));
            }
            writer.Flush();
        }

        private static bool IsDurable(WorldMutableOverlay overlay) =>
            overlay.Revision.IsValid || overlay.ChangedChunkCount != 0 || overlay.ReplacesMultiInstances;

        public void Dispose() => _composition = null;
    }

    internal sealed record Parsed(
        PersistentWorldId DurableWorldId,
        bool HasBaseWorld,
        SpatialSpaceId Space,
        WorldContentFingerprint ContentFingerprint,
        WorldPackFingerprint PackFingerprint,
        WorldOverlayRevision OverlayRevision,
        WorldMutableOverlay.ChunkReplacement[] Chunks,
        WorldMultiInstanceRecord[]? MultiInstances,
        WorldMutableOverlayLimits Limits);

    internal sealed record ParsedCompositionRow(WorldPackCoord Coord, Parsed Overlay);
    internal sealed record ParsedComposition(
        PersistentWorldId DurableWorldId,
        SpatialSpaceId Space,
        WorldCompositionRevision Revision,
        WorldStructuralManifestFingerprint ManifestFingerprint,
        ParsedCompositionRow[] Rows);

    private ref struct WorldArtifactReader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _offset;

        internal WorldArtifactReader(ReadOnlySpan<byte> bytes) { _bytes = bytes; _offset = 0; }
        internal byte ReadByte() { Ensure(1); return _bytes[_offset++]; }
        internal ushort ReadUInt16() { Ensure(2); ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_bytes[_offset..]); _offset += 2; return value; }
        internal uint ReadUInt32() { Ensure(4); uint value = BinaryPrimitives.ReadUInt32LittleEndian(_bytes[_offset..]); _offset += 4; return value; }
        internal ulong ReadUInt64() { Ensure(8); ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_bytes[_offset..]); _offset += 8; return value; }
        internal int ReadInt32() { Ensure(4); int value = BinaryPrimitives.ReadInt32LittleEndian(_bytes[_offset..]); _offset += 4; return value; }
        internal long ReadInt64() { Ensure(8); long value = BinaryPrimitives.ReadInt64LittleEndian(_bytes[_offset..]); _offset += 8; return value; }
        internal int ReadCount(string kind) { int value = ReadInt32(); if (value < 0) throw new PersistenceValidationException($"The current-world {kind} count is negative."); return value; }
        internal byte[] ReadBytes(int count) { Ensure(count); byte[] value = _bytes.Slice(_offset, count).ToArray(); _offset += count; return value; }
        internal void EnsureRemaining(long count) { if (count < 0 || count > _bytes.Length - _offset) throw new PersistenceValidationException("Current-world counts exceed remaining artifact bytes."); }
        internal void EnsureEnd() { if (_offset != _bytes.Length) throw new PersistenceValidationException("The current-world artifact contains trailing bytes."); }

        internal WorldFrozenStaticRecord ReadStatic() => new(
            new WorldStaticId(ReadUInt64()), new WorldStaticDefinitionId(ReadUInt32()),
            ReadPosition(), new WorldYaw(ReadUInt16()), ReadUInt16(), (WorldStaticFlags)ReadUInt16(), ReadBounds());

        internal WorldMultiFragmentRecord ReadFragment() => new(
            new MultiInstanceId(ReadUInt64()), new MultiPieceId(ReadUInt32()),
            new WorldStaticDefinitionId(ReadUInt32()), ReadPosition(), new WorldYaw(ReadUInt16()),
            ReadUInt16(), (WorldStaticFlags)ReadUInt16(), ReadBounds());

        internal WorldMultiInstanceRecord ReadMultiInstance() => new(
            new MultiInstanceId(ReadUInt64()), ReadPosition(), new WorldYaw(ReadUInt16()), ReadInt32());

        private WorldPosition ReadPosition() => new(
            ReadInt32(), ReadInt32(), ReadUInt16(), ReadUInt16(), ReadInt32());

        private WorldStaticBounds ReadBounds() => new(
            ReadInt64(), ReadInt64(), ReadInt64(), ReadInt64(), ReadInt64(), ReadInt64());

        private void Ensure(int count)
        {
            if (count < 0 || count > _bytes.Length - _offset)
                throw new PersistenceValidationException("The current-world artifact is truncated.");
        }
    }
}

/// <summary>Resolved private current-world state awaiting whole-generation publication.</summary>
public sealed class PreparedWorldParticipant : IPreparedLoadParticipant
{
    private readonly WorldSaveSetParticipant.Parsed? _parsed;
    private readonly WorldSaveSetParticipant.ParsedComposition? _composition;
    private readonly Func<WorldStructuralManifestFingerprint, WorldStructuralManifest?>? _manifestResolver;
    private readonly Func<WorldPackFingerprint, BoundWorldPack?>? _packResolver;
    private bool _resolved;

    internal PreparedWorldParticipant(
        string key,
        WorldSaveSetParticipant.Parsed? parsed,
        WorldSaveSetParticipant.ParsedComposition? composition,
        Func<WorldStructuralManifestFingerprint, WorldStructuralManifest?>? manifestResolver,
        Func<WorldPackFingerprint, BoundWorldPack?>? packResolver)
    {
        Key = key;
        _parsed = parsed;
        _composition = composition;
        _manifestResolver = manifestResolver;
        _packResolver = packResolver;
    }

    public string Key { get; }
    public PersistentWorldId DurableWorldId => _composition?.DurableWorldId ?? _parsed!.DurableWorldId;
    public BoundWorldPack? BaseWorld { get; private set; }
    public WorldMutableOverlay? Overlay { get; private set; }
    public PreparedWorldComposition? PreparedComposition { get; private set; }

    public void Resolve(IPersistentEntityResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (_resolved)
            throw new InvalidOperationException("The current-world participant was already resolved.");
        _resolved = true;
        if (_composition is not null)
        {
            ResolveComposition(_composition);
            return;
        }
        WorldSaveSetParticipant.Parsed parsed = _parsed!;
        if (!parsed.HasBaseWorld) return;
        BoundWorldPack baseWorld = _packResolver?.Invoke(parsed.PackFingerprint) ??
            throw new PersistenceCompatibilityException(
                $"The exact WorldPack '{parsed.PackFingerprint}' is unavailable.");
        if (baseWorld.Fingerprint != parsed.PackFingerprint || baseWorld.Space != parsed.Space ||
            baseWorld.Content.Fingerprint != parsed.ContentFingerprint)
            throw new PersistenceCompatibilityException("The resolved WorldPack identity or content binding disagrees with the save.");

        BaseWorld = baseWorld;
        Overlay = ResolveOverlay(parsed, baseWorld);
    }

    private void ResolveComposition(WorldSaveSetParticipant.ParsedComposition composition)
    {
        WorldStructuralManifest manifest = _manifestResolver?.Invoke(composition.ManifestFingerprint) ??
            throw new PersistenceCompatibilityException("The exact structural manifest is unavailable.");
        if (manifest.Fingerprint != composition.ManifestFingerprint)
            throw new PersistenceCompatibilityException("The resolved structural manifest identity disagrees with the save.");
        var prepared = new PreparedWorldPack[manifest.EntryCount];
        int rowIndex = 0;
        for (int index = 0; index < manifest.EntryCount; index++)
        {
            if (!manifest.TryGetEntry(index, out WorldStructuralManifestEntry entry))
                throw new PersistenceValidationException("The structural manifest lost an entry.");
            BoundWorldPack baseWorld = _packResolver?.Invoke(entry.Fingerprint) ??
                throw new PersistenceCompatibilityException($"The exact WorldPack '{entry.Fingerprint}' is unavailable.");
            if (baseWorld.Fingerprint != entry.Fingerprint || baseWorld.Space != composition.Space ||
                !baseWorld.Pack.TryValidateSpatialWindow(entry.Coord, out _, out _))
                throw new PersistenceCompatibilityException("A resolved Pack disagrees with its structural manifest.");
            WorldMutableOverlay? overlay = null;
            if (rowIndex < composition.Rows.Length && composition.Rows[rowIndex].Coord == entry.Coord)
            {
                WorldSaveSetParticipant.Parsed row = composition.Rows[rowIndex++].Overlay;
                if (row.PackFingerprint != entry.Fingerprint || row.Space != composition.Space ||
                    row.ContentFingerprint != baseWorld.Content.Fingerprint)
                    throw new PersistenceCompatibilityException("A Pack overlay row disagrees with its exact base identity.");
                overlay = ResolveOverlay(row, baseWorld);
            }
            prepared[index] = new PreparedWorldPack(entry.Coord, new WorldPreparedView(baseWorld, overlay));
        }
        if (rowIndex != composition.Rows.Length ||
            !PreparedWorldComposition.TryCreate(composition.Space, composition.Revision, manifest,
                prepared, out PreparedWorldComposition? candidate, out _) ||
            !candidate!.TryValidateComplete(out _))
            throw new PersistenceValidationException("The restored complete World composition is invalid.");
        PreparedComposition = candidate;
    }

    private static WorldMutableOverlay ResolveOverlay(WorldSaveSetParticipant.Parsed parsed, BoundWorldPack baseWorld)
    {

        var builder = new WorldMutableOverlayBuilder(baseWorld, parsed.Limits);
        foreach (WorldMutableOverlay.ChunkReplacement chunk in parsed.Chunks)
        {
            if (chunk.TerrainMaterials is not null)
            {
                ushort[] materials = new ushort[TerrainChunkFormat.MaterialCellCount];
                var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
                for (int index = 0; index < materials.Length; index++)
                    materials[index] = BinaryPrimitives.ReadUInt16LittleEndian(chunk.TerrainMaterials.AsSpan(index * 2));
                int baseMillimeters = BinaryPrimitives.ReadInt32LittleEndian(chunk.TerrainHeights!);
                if (!TerrainHeightPlaneBase.TryCreate(baseMillimeters, out TerrainHeightPlaneBase heightBase))
                    throw new PersistenceValidationException("A persisted terrain replacement has an invalid height base.");
                for (int index = 0; index < offsets.Length; index++)
                    offsets[index] = new TerrainHeightOffset(BinaryPrimitives.ReadUInt16LittleEndian(
                        chunk.TerrainHeights.AsSpan(TerrainChunkFormat.HeightPlaneBaseBytes + index * 2)));
                if (!builder.TryReplaceTerrain(chunk.Key, materials, heightBase, offsets))
                    throw new PersistenceValidationException("A persisted terrain replacement is invalid for its base world.");
            }
            if (chunk.FrozenStatics is not null &&
                !builder.TryReplaceFrozenStatics(chunk.Key, chunk.FrozenStatics))
                throw new PersistenceValidationException("A persisted static replacement is invalid for its base world.");
            if (chunk.MultiFragments is not null &&
                !builder.TryReplaceMultiFragments(chunk.Key, chunk.MultiFragments))
                throw new PersistenceValidationException("A persisted Multi replacement is invalid for its base world.");
        }
        if (parsed.MultiInstances is not null && !builder.TryReplaceMultiInstances(parsed.MultiInstances))
            throw new PersistenceValidationException("The persisted Multi-root replacement is invalid for its base world.");
        if (!builder.TryBuild(out WorldMutableOverlay? validated) || validated is null)
            throw new PersistenceValidationException("The persisted current-world overlay violates a world invariant.");
        for (int index = 0; index < parsed.Chunks.Length; index++)
        {
            if (!validated.TryGetChunkRevision(parsed.Chunks[index].Key, out _, out WorldChunkContentHash hash) ||
                hash != parsed.Chunks[index].Hash)
                throw new PersistenceValidationException("A persisted changed-chunk content hash is invalid.");
        }
        var overlay = new WorldMutableOverlay(
            baseWorld.Fingerprint, baseWorld.Space, parsed.OverlayRevision,
            validated.Limits, parsed.Chunks, parsed.MultiInstances);
        if (overlay.ReservedBytes > validated.Limits.MaximumRetainedBytes)
            throw new PersistenceValidationException("The persisted current-world overlay exceeds its retained-byte bound.");
        return overlay;
    }

    public void Validate()
    {
        if (!_resolved || (_composition is not null
                ? PreparedComposition is null
                : _parsed!.HasBaseWorld != (BaseWorld is not null) ||
                  _parsed.HasBaseWorld != (Overlay is not null)))
            throw new PersistenceValidationException("The prepared current-world state is incomplete.");
    }

    public void Dispose()
    {
        BaseWorld = null;
        Overlay = null;
        PreparedComposition = null;
    }
}
