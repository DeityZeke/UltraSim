using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Godot;
using UltraSim.Client.Content;

namespace UltraSim.Client.Godot;

public enum GodotDerivedAtlasFamily : byte
{
    Standalone = 0,
    WorldOpaque = 1,
}

/// <summary>Finite renderer policy participating in persistent derived identity.</summary>
public readonly record struct GodotDerivedResourcePolicy(
    uint PolicyVersion,
    string PlatformProfile,
    string ImportPolicy,
    GodotDerivedAtlasFamily AtlasFamily)
{
    public const uint CurrentFormatVersion = 1;
    public const string BackendIdentity = "godot-4.7";

    public static GodotDerivedResourcePolicy ProductionStandalone(string platformProfile) =>
        new(1, platformProfile, "rgba8-srgb-no-mip-v1", GodotDerivedAtlasFamily.Standalone);

    public bool IsValid => PolicyVersion != 0 && ValidToken(PlatformProfile, 64) &&
        ValidToken(ImportPolicy, 64) && Enum.IsDefined(AtlasFamily);

    private static bool ValidToken(string? value, int maximum)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximum) return false;
        foreach (char character in value)
            if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
                return false;
        return true;
    }
}

public readonly record struct GodotDerivedResourceKey(ContentHash Value)
{
    public bool IsValid => Value.IsValid;
    public override string ToString() => Value.ToString();
}

public readonly record struct GodotDerivedResourceStoreSnapshot(
    int LiveTextures,
    int LiveMaterials,
    ulong MemoryTextureHits,
    ulong DiskTextureHits,
    ulong TextureMisses,
    ulong TextureBuilds,
    ulong CorruptArtifacts,
    ulong MaterialBuilds,
    long DerivedBytesWritten,
    long DerivedBytesRead,
    long TextureBuildTicks,
    long TextureLoadTicks)
{
    public double TextureBuildMilliseconds => TextureBuildTicks * 1_000d / Stopwatch.Frequency;
    public double TextureLoadMilliseconds => TextureLoadTicks * 1_000d / Stopwatch.Frequency;
}

/// <summary>
/// Godot-only, disposable renderer-derived cache. Source CAS authorization and the process-local
/// presentation-generation fence remain owned by Client.Content; this store persists only strict
/// decoded RGBA artifacts under a backend/policy namespace.
/// </summary>
public sealed class GodotDerivedResourceStore : IDisposable
{
    private const ulong TextureMagic = 0x3154_5845_4453_5547UL; // GUSDEXT1
    private const int TextureHeaderBytes = 8 + 4 + 32 + 32 + 4 + 4 + 4 + 32;
    private const int MaximumArtifactBytes = checked((int)ContentRasterArtifact.MaximumDecodedBytes +
        TextureHeaderBytes);

    private readonly string _root;
    private readonly GodotDerivedResourcePolicy _policy;
    private readonly int _ownerThreadId = System.Environment.CurrentManagedThreadId;
    private readonly Dictionary<GodotDerivedResourceKey, NativeTextureEntry> _textures = [];
    private readonly Dictionary<GodotDerivedResourceKey, NativeMaterialEntry> _materials = [];
    private ulong _memoryTextureHits;
    private ulong _diskTextureHits;
    private ulong _textureMisses;
    private ulong _textureBuilds;
    private ulong _corruptArtifacts;
    private ulong _materialBuilds;
    private long _derivedBytesWritten;
    private long _derivedBytesRead;
    private long _textureBuildTicks;
    private long _textureLoadTicks;
    private bool _disposed;

    public GodotDerivedResourceStore(string writableRoot, GodotDerivedResourcePolicy policy)
    {
        if (string.IsNullOrWhiteSpace(writableRoot))
            throw new ArgumentException("A writable derived-resource root is required.", nameof(writableRoot));
        if (!policy.IsValid) throw new ArgumentException("The derived-resource policy is invalid.", nameof(policy));
        _policy = policy;
        string baseRoot = Path.GetFullPath(writableRoot);
        _root = Path.Combine(baseRoot, "Derived", "Godot", policy.PlatformProfile,
            $"format-{GodotDerivedResourcePolicy.CurrentFormatVersion}",
            $"policy-{policy.PolicyVersion}");
        EnsureSafeDirectory(baseRoot, _root);
    }

    public GodotDerivedResourcePolicy Policy => _policy;

    public GodotDerivedResourceStoreSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new GodotDerivedResourceStoreSnapshot(
            _textures.Count, _materials.Count, _memoryTextureHits, _diskTextureHits,
            _textureMisses, _textureBuilds, _corruptArtifacts, _materialBuilds,
            _derivedBytesWritten, _derivedBytesRead, _textureBuildTicks, _textureLoadTicks);
    }

    public GodotDerivedResourceKey GetTextureKey(ResolvedTexturePresentation source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ComputeKey("texture-rgba8", source.StableResourceFingerprint, _policy);
    }

    public GodotDerivedResourceKey GetMaterialKey(ResolvedMaterialPresentation source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ComputeKey("material-standard-lit", source.StableResourceFingerprint, _policy);
    }

    public bool TryAcquireTexture(
        ResolvedTexturePresentation source,
        out GodotDerivedTextureLease? lease,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureOperational();
        lease = null;
        error = null;
        GodotDerivedResourceKey key = GetTextureKey(source);
        if (_textures.TryGetValue(key, out NativeTextureEntry? existing))
        {
            existing.References++;
            _memoryTextureHits++;
            lease = new GodotDerivedTextureLease(this, key, existing.Texture, source,
                existing.Width, existing.Height, diskHit: false);
            return true;
        }

        byte[] pixels;
        int width;
        int height;
        bool diskHit = TryReadTextureArtifact(key, source, out width, out height, out pixels);
        if (diskHit)
        {
            _diskTextureHits++;
        }
        else
        {
            _textureMisses++;
            long started = Stopwatch.GetTimestamp();
            if (!TryDecode(source, out width, out height, out pixels, out error)) return false;
            _textureBuildTicks += Stopwatch.GetTimestamp() - started;
            _textureBuilds++;
            if (!TryWriteTextureArtifact(key, source, width, height, pixels, out error)) return false;
        }

        long loadStarted = Stopwatch.GetTimestamp();
        using Image image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
        if (image.IsEmpty())
        {
            error = "Godot rejected the validated derived RGBA artifact.";
            return false;
        }
        Texture2D texture = ImageTexture.CreateFromImage(image);
        _textureLoadTicks += Stopwatch.GetTimestamp() - loadStarted;
        var entry = new NativeTextureEntry(texture, width, height);
        _textures.Add(key, entry);
        lease = new GodotDerivedTextureLease(
            this, key, texture, source, width, height, diskHit);
        return true;
    }

    public bool TryAcquireMaterial(
        ResolvedMaterialPresentation source,
        out GodotDerivedMaterialLease? lease,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureOperational();
        lease = null;
        error = null;
        GodotDerivedResourceKey key = GetMaterialKey(source);
        if (_materials.TryGetValue(key, out NativeMaterialEntry? existing))
        {
            existing.References++;
            lease = new GodotDerivedMaterialLease(
                this, key, existing.Material, source);
            return true;
        }

        GodotDerivedTextureLease? textureLease = null;
        if (source.AlbedoTexture is not null &&
            !TryAcquireTexture(source.AlbedoTexture, out textureLease, out error))
            return false;
        try
        {
            ContentMaterialDefinition definition = source.Definition;
            var material = new StandardMaterial3D
            {
                AlbedoTexture = textureLease?.Texture,
                Roughness = definition.Roughness,
                Metallic = definition.Metallic,
            };
            if (definition.Emission > 0)
            {
                material.EmissionEnabled = true;
                material.Emission = Colors.White;
                material.EmissionEnergyMultiplier = definition.Emission;
            }
            _materials.Add(key, new NativeMaterialEntry(material, textureLease));
            _materialBuilds++;
            lease = new GodotDerivedMaterialLease(this, key, material, source);
            textureLease = null;
            return true;
        }
        finally { textureLease?.Dispose(); }
    }

    public string GetTextureArtifactPath(GodotDerivedResourceKey key)
    {
        if (!key.IsValid) throw new ArgumentException("The derived key is invalid.", nameof(key));
        string value = key.Value.ToString();
        return Path.Combine(_root, "textures", value[..2], value + ".rgba");
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        if (_textures.Count != 0 || _materials.Count != 0)
            throw new InvalidOperationException(
                "Derived resources cannot retire while native leases are live.");
        _disposed = true;
    }

    internal void ReleaseTexture(GodotDerivedResourceKey key)
    {
        EnsureOwner();
        if (!_textures.TryGetValue(key, out NativeTextureEntry? entry) || --entry.References != 0)
            return;
        _textures.Remove(key);
        entry.Texture.Dispose();
    }

    internal void ReleaseMaterial(GodotDerivedResourceKey key)
    {
        EnsureOwner();
        if (!_materials.TryGetValue(key, out NativeMaterialEntry? entry) || --entry.References != 0)
            return;
        _materials.Remove(key);
        entry.Texture?.Dispose();
        entry.Material.Dispose();
    }

    private bool TryReadTextureArtifact(
        GodotDerivedResourceKey key,
        ResolvedTexturePresentation source,
        out int width,
        out int height,
        out byte[] pixels)
    {
        width = 0;
        height = 0;
        pixels = [];
        string path = GetTextureArtifactPath(key);
        if (!File.Exists(path)) return false;
        try
        {
            var info = new FileInfo(path);
            if (info.Length is < TextureHeaderBytes or > MaximumArtifactBytes) return Corrupt(path);
            byte[] bytes = File.ReadAllBytes(path);
            var reader = new ArtifactReader(bytes);
            if (!reader.TryUInt64(out ulong magic) || magic != TextureMagic ||
                !reader.TryUInt32(out uint version) ||
                    version != GodotDerivedResourcePolicy.CurrentFormatVersion ||
                !reader.TryHash(out ContentHash storedKey) || storedKey != key.Value ||
                !reader.TryHash(out ContentHash sourceHash) || sourceHash != source.File.Sha256 ||
                !reader.TryInt32(out width) || width != source.Raster.Width ||
                !reader.TryInt32(out height) || height != source.Raster.Height ||
                !reader.TryInt32(out int dataLength) || dataLength < 0 ||
                dataLength != checked(width * height * 4) ||
                !reader.TryHash(out ContentHash payloadHash) ||
                !reader.TryBytes(dataLength, out ReadOnlySpan<byte> data) || !reader.Complete ||
                ContentHash.Compute(data) != payloadHash)
                return Corrupt(path);
            pixels = data.ToArray();
            _derivedBytesRead += bytes.Length;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            OverflowException or ArgumentException)
        {
            return Corrupt(path);
        }
    }

    private bool TryWriteTextureArtifact(
        GodotDerivedResourceKey key,
        ResolvedTexturePresentation source,
        int width,
        int height,
        ReadOnlySpan<byte> pixels,
        out string? error)
    {
        error = null;
        string path = GetTextureArtifactPath(key);
        string? temporary = null;
        try
        {
            string directory = Path.GetDirectoryName(path)!;
            EnsureSafeDirectory(_root, directory);
            byte[] bytes = new byte[checked(TextureHeaderBytes + pixels.Length)];
            int offset = 0;
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset, 8), TextureMagic); offset += 8;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4),
                GodotDerivedResourcePolicy.CurrentFormatVersion); offset += 4;
            key.Value.WriteDigest(bytes.AsSpan(offset, 32)); offset += 32;
            source.File.Sha256.WriteDigest(bytes.AsSpan(offset, 32)); offset += 32;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), width); offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), height); offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), pixels.Length); offset += 4;
            ContentHash.Compute(pixels).WriteDigest(bytes.AsSpan(offset, 32)); offset += 32;
            pixels.CopyTo(bytes.AsSpan(offset));
            temporary = Path.Combine(directory, $".{key.Value}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(
                       temporary, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None,
                       64 * 1024, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            temporary = null;
            _derivedBytesWritten += bytes.Length;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException)
        {
            error = $"The derived texture artifact could not be published: {exception.GetType().Name}.";
            return false;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static bool TryDecode(
        ResolvedTexturePresentation source,
        out int width,
        out int height,
        out byte[] pixels,
        out string? error)
    {
        width = 0;
        height = 0;
        pixels = [];
        error = null;
        using var image = new Image();
        Error status = source.File.Kind switch
        {
            ContentFileKind.PngTexture => image.LoadPngFromBuffer(source.Bytes.ToArray()),
            ContentFileKind.WebpTexture => image.LoadWebpFromBuffer(source.Bytes.ToArray()),
            _ => Error.FileUnrecognized,
        };
        if (status != Error.Ok)
        {
            error = $"Godot rejected the validated source texture: {status}.";
            return false;
        }
        width = image.GetWidth();
        height = image.GetHeight();
        if (width != source.Raster.Width || height != source.Raster.Height)
        {
            error = "The decoded raster dimensions did not match the validated envelope.";
            return false;
        }
        if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
        pixels = image.GetData();
        if (pixels.LongLength != source.Raster.MaximumDecodedRgbaBytes)
        {
            error = "The decoded raster byte count exceeded or disagreed with its bounded envelope.";
            pixels = [];
            return false;
        }
        return true;
    }

    private static GodotDerivedResourceKey ComputeKey(
        string kind,
        ContentHash resourceFingerprint,
        in GodotDerivedResourcePolicy policy)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("UltraSim.Godot.Derived.F1.6.v1"u8);
        AppendText(hash, kind);
        AppendText(hash, GodotDerivedResourcePolicy.BackendIdentity);
        AppendUInt32(hash, GodotDerivedResourcePolicy.CurrentFormatVersion);
        AppendUInt32(hash, policy.PolicyVersion);
        AppendText(hash, policy.PlatformProfile);
        AppendText(hash, policy.ImportPolicy);
        hash.AppendData([(byte)policy.AtlasFamily]);
        Span<byte> digest = stackalloc byte[ContentHash.ByteLength];
        resourceFingerprint.WriteDigest(digest);
        hash.AppendData(digest);
        return new GodotDerivedResourceKey(ContentHash.FromDigest(hash.GetHashAndReset()));
    }

    private static void AppendText(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        AppendUInt32(hash, (uint)bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendUInt32(IncrementalHash hash, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private bool Corrupt(string path)
    {
        _corruptArtifacts++;
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return false;
    }

    private static void EnsureSafeDirectory(string baseRoot, string directory)
    {
        string fullBase = Path.GetFullPath(baseRoot);
        string fullDirectory = Path.GetFullPath(directory);
        string prefix = fullBase.EndsWith(Path.DirectorySeparatorChar)
            ? fullBase : fullBase + Path.DirectorySeparatorChar;
        if (!fullDirectory.Equals(fullBase, StringComparison.Ordinal) &&
            !fullDirectory.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("The derived-resource path escaped its writable root.");

        Directory.CreateDirectory(fullBase);
        if ((File.GetAttributes(fullBase) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The derived-resource root cannot be a reparse point.");
        if (fullDirectory.Equals(fullBase, StringComparison.Ordinal)) return;

        string relative = Path.GetRelativePath(fullBase, fullDirectory);
        string current = fullBase;
        foreach (string component in relative.Split(Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (component is "." or "..")
                throw new InvalidOperationException("The derived-resource path was not canonical.");
            current = Path.Combine(current, component);
            if (Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException(
                        "Derived-resource directories cannot be reparse points.");
                continue;
            }
            if (File.Exists(current))
                throw new InvalidOperationException("Derived-resource directories cannot be reparse points.");
            Directory.CreateDirectory(current);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(
                    "Derived-resource directories cannot be reparse points.");
        }
    }

    private void EnsureOperational()
    {
        EnsureOwner();
        if (_disposed) throw new ObjectDisposedException(nameof(GodotDerivedResourceStore));
    }

    private void EnsureOwner()
    {
        if (System.Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Godot derived resources are engine-thread-owned.");
    }

    private sealed class NativeTextureEntry(Texture2D texture, int width, int height)
    {
        internal Texture2D Texture { get; } = texture;
        internal int Width { get; } = width;
        internal int Height { get; } = height;
        internal int References { get; set; } = 1;
    }

    private sealed class NativeMaterialEntry(
        StandardMaterial3D material,
        GodotDerivedTextureLease? texture)
    {
        internal StandardMaterial3D Material { get; } = material;
        internal GodotDerivedTextureLease? Texture { get; } = texture;
        internal int References { get; set; } = 1;
    }

    private ref struct ArtifactReader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _offset;
        internal ArtifactReader(ReadOnlySpan<byte> bytes) => _bytes = bytes;
        internal bool Complete => _offset == _bytes.Length;
        internal bool TryUInt64(out ulong value)
        {
            value = 0;
            if (!Take(8, out ReadOnlySpan<byte> bytes)) return false;
            value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            return true;
        }
        internal bool TryUInt32(out uint value)
        {
            value = 0;
            if (!Take(4, out ReadOnlySpan<byte> bytes)) return false;
            value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            return true;
        }
        internal bool TryInt32(out int value)
        {
            value = 0;
            if (!Take(4, out ReadOnlySpan<byte> bytes)) return false;
            value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
            return true;
        }
        internal bool TryHash(out ContentHash value)
        {
            value = default;
            if (!Take(ContentHash.ByteLength, out ReadOnlySpan<byte> bytes)) return false;
            try { value = ContentHash.FromDigest(bytes); }
            catch (ArgumentException) { return false; }
            return true;
        }
        internal bool TryBytes(int length, out ReadOnlySpan<byte> value) => Take(length, out value);
        private bool Take(int length, out ReadOnlySpan<byte> value)
        {
            value = default;
            if (length < 0 || length > _bytes.Length - _offset) return false;
            value = _bytes.Slice(_offset, length);
            _offset += length;
            return true;
        }
    }
}

public sealed class GodotDerivedTextureLease : IDisposable
{
    private GodotDerivedResourceStore? _owner;
    private ResolvedTexturePresentation? _source;

    internal GodotDerivedTextureLease(
        GodotDerivedResourceStore owner,
        GodotDerivedResourceKey key,
        Texture2D texture,
        ResolvedTexturePresentation source,
        int width,
        int height,
        bool diskHit)
    {
        _owner = owner;
        _source = source;
        Key = key;
        Texture = texture;
        Width = width;
        Height = height;
        DiskHit = diskHit;
    }

    public GodotDerivedResourceKey Key { get; }
    public Texture2D Texture { get; }
    public int Width { get; }
    public int Height { get; }
    public bool DiskHit { get; }

    public void Dispose()
    {
        ResolvedTexturePresentation? source = Interlocked.Exchange(ref _source, null);
        GodotDerivedResourceStore? owner = Interlocked.Exchange(ref _owner, null);
        owner?.ReleaseTexture(Key);
        source?.Dispose();
    }
}

public sealed class GodotDerivedMaterialLease : IDisposable
{
    private GodotDerivedResourceStore? _owner;
    private ResolvedMaterialPresentation? _source;

    internal GodotDerivedMaterialLease(
        GodotDerivedResourceStore owner,
        GodotDerivedResourceKey key,
        StandardMaterial3D material,
        ResolvedMaterialPresentation source)
    {
        _owner = owner;
        _source = source;
        Key = key;
        Material = material;
    }

    public GodotDerivedResourceKey Key { get; }
    public StandardMaterial3D Material { get; }

    public void Dispose()
    {
        ResolvedMaterialPresentation? source = Interlocked.Exchange(ref _source, null);
        GodotDerivedResourceStore? owner = Interlocked.Exchange(ref _owner, null);
        owner?.ReleaseMaterial(Key);
        source?.Dispose();
    }
}

public readonly record struct GodotWorldOpaqueAtlasRegion(
    GodotDerivedResourceKey Source,
    Rect2I Pixels,
    Vector2 UvScale,
    Vector2 UvOffset);

public readonly record struct GodotWorldOpaqueAtlasSnapshot(
    ContentHash PersistentKey,
    int SourceTextures,
    int Pages,
    int Width,
    int Height,
    long SourcePixelBytes,
    long AtlasPixelBytes,
    double Occupancy,
    long BuildTicks)
{
    public double BuildMilliseconds => BuildTicks * 1_000d / Stopwatch.Frequency;
}

/// <summary>
/// Diagnostic F1.6 atlas candidate for finite, opaque RGBA world textures. It deliberately owns
/// transferred source leases and one page texture, but is not the selected production policy.
/// Logical texture identity and UVs remain outside this backend candidate.
/// </summary>
public sealed class GodotWorldOpaqueAtlasCandidate : IDisposable
{
    private const int MaximumSources = 16;
    private readonly GodotDerivedTextureLease[] _sources;
    private bool _disposed;

    private GodotWorldOpaqueAtlasCandidate(
        GodotDerivedTextureLease[] sources,
        ImageTexture page,
        GodotWorldOpaqueAtlasRegion[] regions,
        GodotWorldOpaqueAtlasSnapshot snapshot)
    {
        _sources = sources;
        Page = page;
        Regions = regions;
        Snapshot = snapshot;
    }

    public Texture2D Page { get; }
    public IReadOnlyList<GodotWorldOpaqueAtlasRegion> Regions { get; }
    public GodotWorldOpaqueAtlasSnapshot Snapshot { get; }

    public static bool TryBuildOwned(
        IReadOnlyList<GodotDerivedTextureLease>? sources,
        GodotDerivedResourcePolicy policy,
        out GodotWorldOpaqueAtlasCandidate? atlas,
        out string? error)
    {
        atlas = null;
        error = null;
        if (sources is null || sources.Count is < 1 or > MaximumSources ||
            !policy.IsValid || policy.AtlasFamily != GodotDerivedAtlasFamily.WorldOpaque)
            return Fail("The WorldOpaque atlas request is invalid or exceeds its bound.", out error);
        var owned = new GodotDerivedTextureLease[sources.Count];
        for (int index = 0; index < owned.Length; index++)
            owned[index] = sources[index] ?? throw new ArgumentException(
                "Atlas source leases cannot contain null.", nameof(sources));

        ImageTexture? page = null;
        try
        {
            int width = 0;
            int height = 0;
            long sourceBytes = 0;
            var sourcePixels = new byte[owned.Length][];
            for (int index = 0; index < owned.Length; index++)
            {
                GodotDerivedTextureLease source = owned[index];
                width = checked(width + source.Width);
                height = Math.Max(height, source.Height);
                sourceBytes = checked(sourceBytes + checked((long)source.Width * source.Height * 4));
                using Image image = source.Texture.GetImage();
                if (image.IsEmpty()) return Fail("An atlas source image was unavailable.", out error);
                if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
                byte[] pixels = image.GetData();
                if (pixels.LongLength != checked((long)source.Width * source.Height * 4) ||
                    !IsOpaque(pixels))
                    return Fail("WorldOpaque accepts exact opaque RGBA8 textures only.", out error);
                sourcePixels[index] = pixels;
            }
            long atlasBytes = checked((long)width * height * 4);
            if (width > ContentRasterArtifact.MaximumDimension ||
                height > ContentRasterArtifact.MaximumDimension ||
                atlasBytes > ContentRasterArtifact.MaximumDecodedBytes)
                return Fail("The WorldOpaque atlas exceeded the bounded raster envelope.", out error);

            long started = Stopwatch.GetTimestamp();
            var pixelsOut = new byte[checked((int)atlasBytes)];
            var regions = new GodotWorldOpaqueAtlasRegion[owned.Length];
            int x = 0;
            for (int index = 0; index < owned.Length; index++)
            {
                GodotDerivedTextureLease source = owned[index];
                byte[] pixels = sourcePixels[index];
                int rowBytes = checked(source.Width * 4);
                for (int row = 0; row < source.Height; row++)
                    pixels.AsSpan(row * rowBytes, rowBytes).CopyTo(
                        pixelsOut.AsSpan(checked((row * width + x) * 4), rowBytes));
                regions[index] = new GodotWorldOpaqueAtlasRegion(
                    source.Key,
                    new Rect2I(x, 0, source.Width, source.Height),
                    new Vector2((float)source.Width / width, (float)source.Height / height),
                    new Vector2((float)x / width, 0));
                x += source.Width;
            }
            using Image atlasImage = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixelsOut);
            if (atlasImage.IsEmpty()) return Fail("Godot rejected the WorldOpaque atlas page.", out error);
            page = ImageTexture.CreateFromImage(atlasImage);
            long buildTicks = Stopwatch.GetTimestamp() - started;
            ContentHash key = ComputeAtlasKey(owned, policy);
            atlas = new GodotWorldOpaqueAtlasCandidate(
                owned, page, regions,
                new GodotWorldOpaqueAtlasSnapshot(
                    key, owned.Length, 1, width, height, sourceBytes, atlasBytes,
                    atlasBytes == 0 ? 0 : (double)sourceBytes / atlasBytes,
                    buildTicks));
            page = null;
            return true;
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentException)
        {
            error = $"The WorldOpaque atlas could not be built: {exception.GetType().Name}.";
            return false;
        }
        finally
        {
            if (atlas is null)
            {
                page?.Dispose();
                for (int index = owned.Length - 1; index >= 0; index--)
                    owned[index]?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Page.Dispose();
        for (int index = _sources.Length - 1; index >= 0; index--)
            _sources[index].Dispose();
    }

    private static bool IsOpaque(ReadOnlySpan<byte> pixels)
    {
        for (int index = 3; index < pixels.Length; index += 4)
            if (pixels[index] != byte.MaxValue) return false;
        return true;
    }

    private static ContentHash ComputeAtlasKey(
        ReadOnlySpan<GodotDerivedTextureLease> sources,
        in GodotDerivedResourcePolicy policy)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("UltraSim.Godot.WorldOpaqueAtlas.F1.6.v1"u8);
        AppendText(hash, GodotDerivedResourcePolicy.BackendIdentity);
        AppendUInt32(hash, GodotDerivedResourcePolicy.CurrentFormatVersion);
        AppendUInt32(hash, policy.PolicyVersion);
        AppendText(hash, policy.PlatformProfile);
        AppendText(hash, policy.ImportPolicy);
        hash.AppendData([(byte)policy.AtlasFamily]);
        AppendUInt32(hash, checked((uint)sources.Length));
        Span<byte> digest = stackalloc byte[ContentHash.ByteLength];
        foreach (GodotDerivedTextureLease source in sources)
        {
            source.Key.Value.WriteDigest(digest);
            hash.AppendData(digest);
        }
        return ContentHash.FromDigest(hash.GetHashAndReset());
    }

    private static void AppendText(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        AppendUInt32(hash, checked((uint)bytes.Length));
        hash.AppendData(bytes);
    }

    private static void AppendUInt32(IncrementalHash hash, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}
