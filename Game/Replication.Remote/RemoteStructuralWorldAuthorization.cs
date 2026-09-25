using System.Buffers.Binary;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication;

/// <summary>Server-owned exact structural identity, separate from package byte locations.</summary>
public sealed class RemoteStructuralWorldAuthorization
{
    public RemoteStructuralWorldAuthorization(
        WorldStructuralManifest manifest,
        string manifestDefinitionId,
        SpatialSpaceId space,
        WorldCompositionRevision revision,
        WorldPackCoord initialPack)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(manifestDefinitionId) ||
            manifestDefinitionId != manifestDefinitionId.Trim() ||
            !space.IsValid || !revision.IsValid ||
            !manifest.TryGetPack(initialPack, out _))
            throw new ArgumentException("The structural advertisement identity is incomplete.");
        ManifestDefinitionId = manifestDefinitionId;
        Space = space;
        Revision = revision;
        InitialPack = initialPack;
    }

    public WorldStructuralManifest Manifest { get; }
    public string ManifestDefinitionId { get; }
    public SpatialSpaceId Space { get; }
    public WorldCompositionRevision Revision { get; }
    public WorldPackCoord InitialPack { get; }

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
        };
        if (!UltraSimGameProtocol.IsValidStructuralAdvertisementHeader(in header))
            throw new InvalidOperationException("The structural advertisement became invalid.");
        return new RemoteStructuralWorldAdvertisement(header, ManifestDefinitionId);
    }
}
