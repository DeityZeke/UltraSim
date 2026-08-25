namespace UltraSim.Game.Replication;

/// <summary>
/// Stable identity of a concrete Game-owned public replication profile. This is not a Network
/// lane, message family, or transport policy.
/// </summary>
public enum GamePublicReplicationProfileId : byte
{
    MaintainedEntityRelation = 1,
    ChunkPublication = 2,
    DirtyEntitySpatialFanout = 3,
    DirtyChunkSpatialFanout = 4,
    DirtyEntityChunkBatchFanout = 5,
}

/// <summary>The spatial membership model owned by one public replication profile.</summary>
public enum GamePublicReplicationInterestModel : byte
{
    RecipientEntityRelation = 1,
    RecipientChunkSubscription = 2,
    DirtyEntitySpatialDiscovery = 3,
    DirtyChunkSpatialDiscovery = 4,
    DirtyEntityChunkBatchDiscovery = 5,
}

/// <summary>The current-state and change-lineage model owned by one public replication profile.</summary>
public enum GamePublicReplicationSourceModel : byte
{
    EntityLatestState = 1,
    ChunkRevisionHistory = 2,
}

/// <summary>
/// Semantic domain covered by this catalog. Immutable terrain, frozen structures, and static
/// decoration remain WorldPack/static-layer content and are intentionally not replication profiles.
/// </summary>
public enum GamePublicReplicationDomain : byte
{
    DynamicPublicState = 1,
}

/// <summary>Current-game selection status of one concrete dynamic-public profile.</summary>
public enum GamePublicReplicationProfileDisposition : byte
{
    SelectedCurrentGame = 1,
    RetainedAlternative = 2,
    ExperimentalReference = 3,
}

/// <summary>
/// Cross-profile semantic guarantees. Concrete profiles retain their own interest, cursor,
/// recovery, and codec types; these flags do not introduce a generic replication interface.
/// </summary>
[Flags]
public enum GamePublicReplicationGuarantee : ushort
{
    None = 0,
    AuthoritativeSourceTruth = 1 << 0,
    RecipientDisclosure = 1 << 1,
    StableIdentityAndLifetime = 1 << 2,
    ApplicationProof = 1 << 3,
    ImpairmentRecovery = 1 << 4,
    EventualSemanticConvergence = 1 << 5,
    BoundedMemory = 1 << 6,
}

/// <summary>Finite description of one concrete Game-owned public replication profile.</summary>
public readonly record struct GamePublicReplicationProfileDescriptor(
    GamePublicReplicationProfileId Id,
    GamePublicReplicationInterestModel InterestModel,
    GamePublicReplicationSourceModel SourceModel,
    GamePublicReplicationDomain Domain,
    GamePublicReplicationProfileDisposition Disposition,
    GamePublicReplicationGuarantee Guarantees);

/// <summary>
/// Explicit profile catalog. Profiles meet only after Game encoding at the bounded Network
/// delivery boundary; their internal AOI and state-lineage models intentionally remain concrete.
/// </summary>
public static class GamePublicReplicationProfiles
{
    /// <summary>
    /// Runtime transfer of one logical public object between these profile identity lineages is not
    /// supported. Such a feature first requires a stable cross-profile Game identity, ownership
    /// lineage, and client-side cross-profile ordering/deduplication semantics.
    /// </summary>
    public static bool RuntimeOwnershipTransitionsSupported => false;

    public const GamePublicReplicationGuarantee RequiredGuarantees =
        GamePublicReplicationGuarantee.AuthoritativeSourceTruth |
        GamePublicReplicationGuarantee.RecipientDisclosure |
        GamePublicReplicationGuarantee.StableIdentityAndLifetime |
        GamePublicReplicationGuarantee.ApplicationProof |
        GamePublicReplicationGuarantee.ImpairmentRecovery |
        GamePublicReplicationGuarantee.EventualSemanticConvergence |
        GamePublicReplicationGuarantee.BoundedMemory;

    public static GamePublicReplicationProfileDescriptor MaintainedEntityRelation { get; } = new(
        GamePublicReplicationProfileId.MaintainedEntityRelation,
        GamePublicReplicationInterestModel.RecipientEntityRelation,
        GamePublicReplicationSourceModel.EntityLatestState,
        GamePublicReplicationDomain.DynamicPublicState,
        GamePublicReplicationProfileDisposition.RetainedAlternative,
        RequiredGuarantees);

    public static GamePublicReplicationProfileDescriptor ChunkPublication { get; } = new(
        GamePublicReplicationProfileId.ChunkPublication,
        GamePublicReplicationInterestModel.RecipientChunkSubscription,
        GamePublicReplicationSourceModel.ChunkRevisionHistory,
        GamePublicReplicationDomain.DynamicPublicState,
        GamePublicReplicationProfileDisposition.SelectedCurrentGame,
        RequiredGuarantees);

    public static GamePublicReplicationProfileDescriptor DirtyEntitySpatialFanout { get; } = new(
        GamePublicReplicationProfileId.DirtyEntitySpatialFanout,
        GamePublicReplicationInterestModel.DirtyEntitySpatialDiscovery,
        GamePublicReplicationSourceModel.EntityLatestState,
        GamePublicReplicationDomain.DynamicPublicState,
        GamePublicReplicationProfileDisposition.RetainedAlternative,
        RequiredGuarantees);

    public static GamePublicReplicationProfileDescriptor DirtyChunkSpatialFanout { get; } = new(
        GamePublicReplicationProfileId.DirtyChunkSpatialFanout,
        GamePublicReplicationInterestModel.DirtyChunkSpatialDiscovery,
        GamePublicReplicationSourceModel.ChunkRevisionHistory,
        GamePublicReplicationDomain.DynamicPublicState,
        GamePublicReplicationProfileDisposition.ExperimentalReference,
        RequiredGuarantees);

    public static GamePublicReplicationProfileDescriptor DirtyEntityChunkBatchFanout { get; } = new(
        GamePublicReplicationProfileId.DirtyEntityChunkBatchFanout,
        GamePublicReplicationInterestModel.DirtyEntityChunkBatchDiscovery,
        GamePublicReplicationSourceModel.EntityLatestState,
        GamePublicReplicationDomain.DynamicPublicState,
        GamePublicReplicationProfileDisposition.ExperimentalReference,
        RequiredGuarantees);

    /// <summary>
    /// Selected dynamic-public replication profile for the current UltraSim game. This does not
    /// select an owner for immutable WorldPack/static layers or every future dynamic semantic class.
    /// </summary>
    public static GamePublicReplicationProfileDescriptor Selected => ChunkPublication;

    public static int Count => 5;

    public static bool TryGet(
        GamePublicReplicationProfileId id,
        out GamePublicReplicationProfileDescriptor profile)
    {
        switch (id)
        {
            case GamePublicReplicationProfileId.MaintainedEntityRelation:
                profile = MaintainedEntityRelation;
                return true;
            case GamePublicReplicationProfileId.ChunkPublication:
                profile = ChunkPublication;
                return true;
            case GamePublicReplicationProfileId.DirtyEntitySpatialFanout:
                profile = DirtyEntitySpatialFanout;
                return true;
            case GamePublicReplicationProfileId.DirtyChunkSpatialFanout:
                profile = DirtyChunkSpatialFanout;
                return true;
            case GamePublicReplicationProfileId.DirtyEntityChunkBatchFanout:
                profile = DirtyEntityChunkBatchFanout;
                return true;
            default:
                profile = default;
                return false;
        }
    }
}
