namespace UltraSim.Game.World;

/// <summary>Authorship/residency of one chunk in an exact SpatialSpaceId.</summary>
public enum WorldStructuralChunkStatus : byte
{
    None = 0,
    NotAuthoredPack,
    KnownPackButNotPrepared,
    AuthoredPackMissingChunk,
    PreparedChunk,
}

/// <summary>Immutable identity only; a status probe never lends Pack bytes or prepares content.</summary>
public readonly record struct WorldStructuralChunkProbe(
    WorldStructuralChunkStatus Status,
    WorldPackCoord OwnerPack,
    WorldPackFingerprint ExpectedPackFingerprint);

/// <summary>Detailed base-terrain cell state, not a complete vertical-column result.</summary>
public enum WorldBaseTerrainResolutionStatus : byte
{
    None = 0,
    NotAuthoredPack,
    KnownPackButNotPrepared,
    AuthoredPackMissingChunk,
    IntentionalVoid,
    Present,
}

/// <summary>Height/material are meaningful only for Present.</summary>
public readonly record struct WorldBaseTerrainResolution(
    WorldBaseTerrainResolutionStatus Status,
    WorldPackCoord OwnerPack,
    WorldPackFingerprint ExpectedPackFingerprint,
    int HeightMillimeters,
    ushort MaterialId);
