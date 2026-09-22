using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace UltraSim.Client.Content;

public enum PresentationResourceResolutionError : byte
{
    None = 0,
    InvalidRequest,
    GenerationRetired,
    DefinitionNotFound,
    DefinitionKindMismatch,
    MaterialTextureMissing,
}

/// <summary>
/// Immutable engine-neutral texture leaf selected by one presentation generation. The exact
/// authorized source blob remains pinned until this lease is released.
/// </summary>
public sealed class ResolvedTexturePresentation : IDisposable
{
    private IDisposable? _lifetime;
    private readonly byte[] _bytes;

    internal ResolvedTexturePresentation(
        IDisposable lifetime,
        LogicalContentId logicalId,
        PresentationSourceKind source,
        UserResourcePackIdentity userResourcePack,
        ContentFileDescriptor file,
        ContentRasterArtifactInfo raster,
        byte[] bytes,
        PresentationGenerationId generation,
        ContentHash stableResourceFingerprint)
    {
        _lifetime = lifetime;
        LogicalId = logicalId;
        Source = source;
        UserResourcePack = userResourcePack;
        File = file;
        Raster = raster;
        _bytes = bytes;
        Bytes = bytes;
        Generation = generation;
        StableResourceFingerprint = stableResourceFingerprint;
    }

    public LogicalContentId LogicalId { get; }
    public PresentationSourceKind Source { get; }
    public UserResourcePackIdentity UserResourcePack { get; }
    public ContentFileDescriptor File { get; }
    public ContentRasterArtifactInfo Raster { get; }
    public ReadOnlyMemory<byte> Bytes { get; }
    public PresentationGenerationId Generation { get; }
    public ContentHash StableResourceFingerprint { get; }
    public bool IsDisposed => Volatile.Read(ref _lifetime) is null;

    internal ResolvedTexturePresentation WithLifetime(
        IDisposable lifetime,
        PresentationGenerationId generation) => new(
            lifetime, LogicalId, Source, UserResourcePack, File, Raster, _bytes,
            generation, StableResourceFingerprint);

    public void Dispose() => Interlocked.Exchange(ref _lifetime, null)?.Dispose();
}

/// <summary>
/// Immutable material truth plus the effective texture leaf selected independently through the
/// same presentation generation. Renderer-native material/atlas objects are deliberately absent.
/// </summary>
public sealed class ResolvedMaterialPresentation : IDisposable
{
    private IDisposable? _lifetime;

    internal ResolvedMaterialPresentation(
        IDisposable lifetime,
        LogicalContentId logicalId,
        PresentationSourceKind source,
        ContentMaterialDefinition definition,
        ContentHash definitionHash,
        ContentHash stableResourceFingerprint,
        PresentationGenerationId generation,
        ResolvedTexturePresentation? albedoTexture)
    {
        _lifetime = lifetime;
        LogicalId = logicalId;
        Source = source;
        Definition = definition;
        DefinitionHash = definitionHash;
        StableResourceFingerprint = stableResourceFingerprint;
        Generation = generation;
        AlbedoTexture = albedoTexture;
    }

    public LogicalContentId LogicalId { get; }
    public PresentationSourceKind Source { get; }
    public ContentMaterialDefinition Definition { get; }
    public ContentHash DefinitionHash { get; }
    public ContentHash StableResourceFingerprint { get; }
    public PresentationGenerationId Generation { get; }
    public ResolvedTexturePresentation? AlbedoTexture { get; }

    public void Dispose()
    {
        AlbedoTexture?.Dispose();
        Interlocked.Exchange(ref _lifetime, null)?.Dispose();
    }
}

internal static class PresentationResourceFingerprint
{
    internal static ContentHash Texture(
        LogicalContentId id,
        in ContentFileDescriptor file)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("UltraSim.Presentation.Texture.F1.6.v1"u8);
        AppendText(hash, id.Value!);
        hash.AppendData([(byte)file.Kind]);
        Span<byte> digest = stackalloc byte[ContentHash.ByteLength];
        file.Sha256.WriteDigest(digest);
        hash.AppendData(digest);
        return ContentHash.FromDigest(hash.GetHashAndReset());
    }

    internal static ContentHash Material(
        LogicalContentId id,
        ContentHash definitionHash,
        ContentHash textureFingerprint)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("UltraSim.Presentation.Material.F1.6.v1"u8);
        AppendText(hash, id.Value!);
        Span<byte> digest = stackalloc byte[ContentHash.ByteLength];
        definitionHash.WriteDigest(digest);
        hash.AppendData(digest);
        if (textureFingerprint.IsValid)
        {
            textureFingerprint.WriteDigest(digest);
            hash.AppendData(digest);
        }
        else hash.AppendData([0]);
        return ContentHash.FromDigest(hash.GetHashAndReset());
    }

    internal static void AppendText(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
