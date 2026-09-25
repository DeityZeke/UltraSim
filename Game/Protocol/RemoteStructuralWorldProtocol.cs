using System.Text;
using System.Collections.ObjectModel;
using UltraSim.Network;

namespace UltraSim.Game.Protocol;

public enum RemoteStructuralApplyStatus : byte
{
    Applied = 1,
    Rejected = 2,
}

/// <summary>Authenticated current structural identity; the Pack table remains in the manifest artifact.</summary>
[NetworkMessage(150, "ultrasim.game.structural-world-advertisement", 1, NetworkLane.Control)]
public partial struct RemoteStructuralWorldAdvertisementHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong StructuralSequence;
    public ulong SpatialSpace;
    public ulong WorldCompositionRevision;
    public ulong ManifestHash0;
    public ulong ManifestHash1;
    public ulong ManifestHash2;
    public ulong ManifestHash3;
    public int AnchorPackX;
    public int AnchorPackZ;
}

/// <summary>Exact World-level proof, distinct from generic ContentSetApplied.</summary>
[NetworkMessage(151, "ultrasim.game.structural-manifest-applied", 1, NetworkLane.Control)]
public partial struct RemoteStructuralManifestAppliedMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StructuralSequence;
    public ulong ManifestHash0;
    public ulong ManifestHash1;
    public ulong ManifestHash2;
    public ulong ManifestHash3;
    public RemoteStructuralApplyStatus Status;
}

/// <summary>The client names a manifest coordinate, never an expected fingerprint.</summary>
[NetworkMessage(152, "ultrasim.game.structural-pack-request", 1, NetworkLane.Control)]
public partial struct RemoteStructuralPackRequestMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StructuralSequence;
    public ulong PackRequestSequence;
    public int PackX;
    public int PackZ;
}

/// <summary>Grant header followed by one package ID and a bounded ordered approved-source list.</summary>
[NetworkMessage(153, "ultrasim.game.structural-pack-grant", 1, NetworkLane.Control)]
public partial struct RemoteStructuralPackGrantHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong StructuralSequence;
    public ulong PackRequestSequence;
    public int PackX;
    public int PackZ;
    public ulong PackHash0;
    public ulong PackHash1;
    public ulong PackHash2;
    public ulong PackHash3;
    public ulong PackageManifestHash0;
    public ulong PackageManifestHash1;
    public ulong PackageManifestHash2;
    public ulong PackageManifestHash3;
    public byte SourceCount;
}

/// <summary>Proof of a verified, bound, and published Pack; not proof of full semantic residency.</summary>
[NetworkMessage(154, "ultrasim.game.structural-pack-applied", 1, NetworkLane.Control)]
public partial struct RemoteStructuralPackAppliedMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StructuralSequence;
    public ulong PackRequestSequence;
    public int PackX;
    public int PackZ;
    public ulong PackHash0;
    public ulong PackHash1;
    public ulong PackHash2;
    public ulong PackHash3;
    public RemoteStructuralApplyStatus Status;
}

public readonly record struct RemoteStructuralWorldAdvertisement(
    RemoteStructuralWorldAdvertisementHeader Header,
    string ManifestDefinitionId);

public sealed class RemoteStructuralPackGrant
{
    private readonly ReadOnlyCollection<RemoteContentSource> _sources;

    public RemoteStructuralPackGrant(RemoteStructuralPackGrantHeader header, string packageId,
        IEnumerable<RemoteContentSource> sources)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(sources);
        RemoteContentSource[] copied = sources
            .Take(UltraSimGameProtocol.MaximumContentSourcesPerPackage + 1).ToArray();
        if (!UltraSimGameProtocol.IsValidStructuralGrantHeader(header) ||
            !IsValidPackageId(packageId) ||
            copied.Length is 0 or > UltraSimGameProtocol.MaximumContentSourcesPerPackage ||
            copied.Any(static source => !source.IsValid) ||
            copied.Distinct().Count() != copied.Length ||
            header.SourceCount != copied.Length)
            throw new ArgumentException("The structural Pack grant is not canonical.");
        _sources = Array.AsReadOnly(copied);
        Header = header;
        PackageId = packageId;
    }

    public RemoteStructuralPackGrantHeader Header { get; }
    public string PackageId { get; }
    public IReadOnlyList<RemoteContentSource> Sources => _sources;

    private static bool IsValidPackageId(string value) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() &&
        Encoding.UTF8.GetByteCount(value) <= UltraSimGameProtocol.MaximumContentPackageIdUtf8Bytes;
}

public static partial class UltraSimGameProtocol
{
    public const int MaximumStructuralAdvertisementBytes =
        RemoteStructuralWorldAdvertisementHeaderCodec.EncodedSize + 2 + 160;
    public const int MaximumStructuralGrantBytes = MaximumMessageBytes;

    public static bool TryWriteStructuralAdvertisement(Span<byte> destination,
        in RemoteStructuralWorldAdvertisement advertisement, out int written)
    {
        written = 0;
        if (!IsValidStructuralAdvertisementHeader(advertisement.Header) ||
            string.IsNullOrWhiteSpace(advertisement.ManifestDefinitionId) ||
            advertisement.ManifestDefinitionId != advertisement.ManifestDefinitionId.Trim() ||
            Encoding.UTF8.GetByteCount(advertisement.ManifestDefinitionId) > 160)
            return false;
        var writer = new NetworkPacketWriter(destination);
        RemoteStructuralWorldAdvertisementHeader header = advertisement.Header;
        if (!RemoteStructuralWorldAdvertisementHeaderCodec.TryWrite(
                ref writer, in header) ||
            !writer.TryWriteBoundedUtf8(advertisement.ManifestDefinitionId, 160) ||
            writer.WrittenCount > MaximumStructuralAdvertisementBytes)
            return false;
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadStructuralAdvertisement(ReadOnlySpan<byte> source,
        out RemoteStructuralWorldAdvertisement advertisement)
    {
        advertisement = default;
        if (source.Length > MaximumStructuralAdvertisementBytes) return false;
        var reader = new NetworkPacketReader(source);
        if (!RemoteStructuralWorldAdvertisementHeaderCodec.TryRead(ref reader,
                out RemoteStructuralWorldAdvertisementHeader header) ||
            !IsValidStructuralAdvertisementHeader(header) ||
            !reader.TryReadBoundedUtf8Bytes(160, out ReadOnlySpan<byte> idBytes) ||
            idBytes.IsEmpty || !reader.IsComplete)
            return false;
        string id = Encoding.UTF8.GetString(idBytes);
        if (id != id.Trim()) return false;
        advertisement = new(header, id);
        return true;
    }

    public static bool IsValidStructuralAdvertisementHeader(
        in RemoteStructuralWorldAdvertisementHeader header) =>
        header.ApplicationSessionEpoch != 0 && header.StructuralSequence != 0 &&
        header.SpatialSpace != 0 && header.WorldCompositionRevision != 0 &&
        (header.ManifestHash0 | header.ManifestHash1 |
         header.ManifestHash2 | header.ManifestHash3) != 0;

    public static bool IsValidStructuralManifestApplied(in RemoteStructuralManifestAppliedMessage proof) =>
        proof.ApplicationSessionEpoch != 0 && proof.StructuralSequence != 0 &&
        (proof.ManifestHash0 | proof.ManifestHash1 | proof.ManifestHash2 | proof.ManifestHash3) != 0 &&
        proof.Status is RemoteStructuralApplyStatus.Applied or RemoteStructuralApplyStatus.Rejected;

    public static bool IsValidStructuralPackRequest(in RemoteStructuralPackRequestMessage request) =>
        request.ApplicationSessionEpoch != 0 && request.StructuralSequence != 0 &&
        request.PackRequestSequence != 0;

    public static bool IsValidStructuralGrantHeader(in RemoteStructuralPackGrantHeader grant) =>
        grant.ApplicationSessionEpoch != 0 && grant.StructuralSequence != 0 &&
        grant.PackRequestSequence != 0 &&
        (grant.PackHash0 | grant.PackHash1 | grant.PackHash2 | grant.PackHash3) != 0 &&
        (grant.PackageManifestHash0 | grant.PackageManifestHash1 |
         grant.PackageManifestHash2 | grant.PackageManifestHash3) != 0 &&
        grant.SourceCount is > 0 and <= MaximumContentSourcesPerPackage;

    public static bool IsValidStructuralPackApplied(in RemoteStructuralPackAppliedMessage proof) =>
        proof.ApplicationSessionEpoch != 0 && proof.StructuralSequence != 0 &&
        proof.PackRequestSequence != 0 &&
        (proof.PackHash0 | proof.PackHash1 | proof.PackHash2 | proof.PackHash3) != 0 &&
        proof.Status is RemoteStructuralApplyStatus.Applied or RemoteStructuralApplyStatus.Rejected;

    public static bool TryWriteStructuralPackGrant(Span<byte> destination,
        RemoteStructuralPackGrant grant, out int written)
    {
        written = 0;
        ArgumentNullException.ThrowIfNull(grant);
        var writer = new NetworkPacketWriter(destination);
        RemoteStructuralPackGrantHeader header = grant.Header;
        if (!RemoteStructuralPackGrantHeaderCodec.TryWrite(ref writer, in header) ||
            !writer.TryWriteBoundedUtf8(grant.PackageId, MaximumContentPackageIdUtf8Bytes))
            return false;
        foreach (RemoteContentSource source in grant.Sources)
            if (!writer.TryWriteByte((byte)source.Kind) ||
                !writer.TryWriteBoundedUtf8(source.Locator, MaximumContentSourceLocatorUtf8Bytes))
                return false;
        written = writer.WrittenCount;
        return written <= MaximumStructuralGrantBytes;
    }

    public static bool TryReadStructuralPackGrant(ReadOnlySpan<byte> source,
        out RemoteStructuralPackGrant? grant)
    {
        grant = null;
        if (source.Length > MaximumStructuralGrantBytes) return false;
        var reader = new NetworkPacketReader(source);
        if (!RemoteStructuralPackGrantHeaderCodec.TryRead(ref reader,
                out RemoteStructuralPackGrantHeader header) ||
            !IsValidStructuralGrantHeader(header) ||
            !reader.TryReadBoundedUtf8Bytes(MaximumContentPackageIdUtf8Bytes,
                out ReadOnlySpan<byte> packageBytes) || packageBytes.IsEmpty)
            return false;
        var sources = new RemoteContentSource[header.SourceCount];
        for (int index = 0; index < sources.Length; index++)
        {
            if (!reader.TryReadByte(out byte kind) ||
                !reader.TryReadBoundedUtf8Bytes(MaximumContentSourceLocatorUtf8Bytes,
                    out ReadOnlySpan<byte> locatorBytes) || locatorBytes.IsEmpty)
                return false;
            sources[index] = new((RemoteContentSourceKind)kind, Encoding.UTF8.GetString(locatorBytes));
        }
        if (!reader.IsComplete) return false;
        try
        {
            grant = new RemoteStructuralPackGrant(header, Encoding.UTF8.GetString(packageBytes), sources);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

}
