namespace UltraSim.Game.Replication;

/// <summary>
/// Stable identity of a concrete Game-owned public replication profile. This is not a Network
/// lane, message family, or transport policy.
/// </summary>
public enum GamePublicReplicationProfileId : byte
{
    MaintainedEntityRelation = 1,
    ChunkPublication = 2,
}

/// <summary>The spatial membership model owned by one public replication profile.</summary>
public enum GamePublicReplicationInterestModel : byte
{
    RecipientEntityRelation = 1,
    RecipientChunkSubscription = 2,
}

/// <summary>The current-state and change-lineage model owned by one public replication profile.</summary>
public enum GamePublicReplicationSourceModel : byte
{
    EntityLatestState = 1,
    ChunkRevisionHistory = 2,
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
    GamePublicReplicationGuarantee Guarantees);

/// <summary>
/// Explicit profile catalog. Profiles meet only after Game encoding at the bounded Network
/// delivery boundary; their internal AOI and state-lineage models intentionally remain concrete.
/// </summary>
public static class GamePublicReplicationProfiles
{
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
        RequiredGuarantees);

    public static GamePublicReplicationProfileDescriptor ChunkPublication { get; } = new(
        GamePublicReplicationProfileId.ChunkPublication,
        GamePublicReplicationInterestModel.RecipientChunkSubscription,
        GamePublicReplicationSourceModel.ChunkRevisionHistory,
        RequiredGuarantees);

    /// <summary>Selected public replication profile for the current UltraSim game.</summary>
    public static GamePublicReplicationProfileDescriptor Selected => ChunkPublication;

    public static int Count => 2;

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
            default:
                profile = default;
                return false;
        }
    }
}
