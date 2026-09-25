using System.Buffers.Binary;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication;

/// <summary>Server-owned exact structural identity, separate from package byte locations.</summary>
public sealed class RemoteStructuralWorldAuthorization
{
    private readonly Dictionary<WorldPackCoord, RemoteContentPackageRequirement> _packages;

    public RemoteStructuralWorldAuthorization(
        WorldStructuralManifest manifest,
        string manifestDefinitionId,
        SpatialSpaceId space,
        WorldCompositionRevision revision,
        WorldPackCoord initialPack,
        SpatialChunkKey initialCenter,
        IEnumerable<RemoteStructuralPackPackage>? packages = null)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(manifestDefinitionId) ||
            manifestDefinitionId != manifestDefinitionId.Trim() ||
            !space.IsValid || !revision.IsValid ||
            !manifest.TryGetPack(initialPack, out _) ||
            initialCenter.Space != space ||
            !WorldPackAddressing.Contains(initialPack, initialCenter.Coord))
            throw new ArgumentException("The structural advertisement identity is incomplete.");
        ManifestDefinitionId = manifestDefinitionId;
        Space = space;
        Revision = revision;
        InitialPack = initialPack;
        InitialCenter = initialCenter;
        _packages = new Dictionary<WorldPackCoord, RemoteContentPackageRequirement>();
        foreach (RemoteStructuralPackPackage package in packages ?? [])
        {
            if (!manifest.TryGetPack(package.Coord, out _) || package.Package is null ||
                !_packages.TryAdd(package.Coord, package.Package))
                throw new ArgumentException(
                    "Structural Pack sources must be unique coordinates in the manifest.",
                    nameof(packages));
        }
    }

    public WorldStructuralManifest Manifest { get; }
    public string ManifestDefinitionId { get; }
    public SpatialSpaceId Space { get; }
    public WorldCompositionRevision Revision { get; }
    public WorldPackCoord InitialPack { get; }
    public SpatialChunkKey InitialCenter { get; }
    public int AvailablePackageCount => _packages.Count;

    /// <summary>
    /// W7 only transports immutable base Pack artifacts. Refuse a deployment whose effective
    /// authority contains any structural replacement that those bytes cannot reproduce.
    /// </summary>
    public bool MatchesUnmodifiedAuthority(PreparedWorldComposition? authority)
    {
        if (authority is null || authority.Space != Space || authority.Revision != Revision ||
            authority.Manifest.Fingerprint != Manifest.Fingerprint ||
            !authority.TryValidateComplete(out _)) return false;
        for (int index = 0; index < authority.PreparedPackCount; index++)
        {
            if (!authority.TryGetPreparedPackAt(index, out PreparedWorldPack pack) ||
                !Manifest.TryGetPack(pack.Coord, out WorldPackFingerprint expected) ||
                pack.View.BaseWorldFingerprint != expected ||
                pack.View.Overlay.ChangedChunkCount != 0 ||
                pack.View.Overlay.ReplacesMultiInstances)
                return false;
        }
        return true;
    }

    /// <summary>Resolves a request coordinate only against current manifest truth and approved bytes.</summary>
    public bool TryCreateGrant(in RemoteStructuralPackRequestMessage request,
        out RemoteStructuralPackGrant? grant)
    {
        grant = null;
        WorldPackCoord coord = new(request.PackX, request.PackZ);
        if (!UltraSimGameProtocol.IsValidStructuralPackRequest(request) ||
            !Manifest.TryGetPack(coord, out WorldPackFingerprint fingerprint) ||
            !_packages.TryGetValue(coord, out RemoteContentPackageRequirement? package))
            return false;
        RemoteContentHash manifestHash = package.ManifestHash;
        var header = new RemoteStructuralPackGrantHeader
        {
            ApplicationSessionEpoch = request.ApplicationSessionEpoch,
            StructuralSequence = request.StructuralSequence,
            PackRequestSequence = request.PackRequestSequence,
            PackX = request.PackX,
            PackZ = request.PackZ,
            PackHash0 = fingerprint.Part0,
            PackHash1 = fingerprint.Part1,
            PackHash2 = fingerprint.Part2,
            PackHash3 = fingerprint.Part3,
            PackageManifestHash0 = manifestHash.Part0,
            PackageManifestHash1 = manifestHash.Part1,
            PackageManifestHash2 = manifestHash.Part2,
            PackageManifestHash3 = manifestHash.Part3,
            SourceCount = checked((byte)package.Sources.Count),
        };
        grant = new RemoteStructuralPackGrant(header, package.PackageId, package.Sources);
        return true;
    }

    public RemoteStructuralWorldAdvertisement CreateAdvertisement(
        ulong applicationSessionEpoch, ulong structuralSequence)
    {
        Span<byte> digest = stackalloc byte[WorldStructuralManifestFormat.FingerprintBytes];
        Manifest.Fingerprint.CopyTo(digest);
        var header = new RemoteStructuralWorldAdvertisementHeader
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            StructuralSequence = structuralSequence,
            SpatialSpace = Space.Value,
            WorldCompositionRevision = Revision.Value,
            ManifestHash0 = BinaryPrimitives.ReadUInt64LittleEndian(digest),
            ManifestHash1 = BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]),
            ManifestHash2 = BinaryPrimitives.ReadUInt64LittleEndian(digest[16..]),
            ManifestHash3 = BinaryPrimitives.ReadUInt64LittleEndian(digest[24..]),
            AnchorPackX = InitialPack.X,
            AnchorPackZ = InitialPack.Z,
            AnchorChunkX = InitialCenter.Coord.X,
            AnchorChunkZ = InitialCenter.Coord.Z,
        };
        if (!UltraSimGameProtocol.IsValidStructuralAdvertisementHeader(in header))
            throw new InvalidOperationException("The structural advertisement became invalid.");
        return new RemoteStructuralWorldAdvertisement(header, ManifestDefinitionId);
    }
}

public readonly record struct RemoteStructuralPackPackage(
    WorldPackCoord Coord, RemoteContentPackageRequirement Package);
