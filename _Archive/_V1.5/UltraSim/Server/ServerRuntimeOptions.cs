using UltraSim.Discovery;
using UltraSim.Server.Engines.Accounting;
using UltraSim.Shared.Gameplay;

namespace UltraSim.Server
{
    public sealed record ServerRuntimeOptions(
        string ProfileName,
        int TickMilliseconds = 50,
        bool StartTransportOnStart = true,
        bool StopTransportOnStop = true,
        bool SaveOnStop = false,
        float AutoSaveEverySeconds = 0f,
        string? SaveDirectory = null,
        string? PublicServerId = null,
        string? PublicDisplayName = null,
        string? PublicConnectHint = null,
        string? PublicPrimaryLanguage = null,
        string[]? PublicLanguages = null,
        string? PublicWebsiteUrl = null,
        string? PublicDiscordUrl = null,
        int? PublicMaxUsers = null,
        string? PublicCompatibilityId = null,
        string? PublicProtocolVersion = null,
        string? PublicContentVersion = null,
        string? PublicStatusEndpointUrl = null,
        string? PublicOwnershipTokenHash = null,
        bool PublicVisible = true,
        bool PasswordRequired = false,
        bool WhitelistOnly = false,
        string? PublicRegion = null,
        string? PublicDescription = null,
        string[]? PublicTags = null,
        ServerListingVisibility PublicVisibility = ServerListingVisibility.Public,
        ServerCapabilityFlags PublicCapabilityFlags = ServerCapabilityFlags.None,
        long ChatMinTicksBetweenMessages = 8,
        bool EnableChatCommands = true,
        bool EnableDenseLocalChatRecipientCap = true,
        int DenseLocalChatCapStartRecipients = 24,
        int DenseLocalChatCapMaxRecipients = 8,
        bool EnableDenseLocalChatRateCap = true,
        int DenseLocalChatRateCapStartRecipients = 24,
        int DenseLocalChatRateCapMinTicks = 60,
        bool EnableChatBatching = true,
        ushort DefaultPlayerArchetypeId = PlayerArchetypeIds.Default,
        int PlayerSpawnSpacingGrid = 4,
        int PlayerSpawnWrapCount = 0,
        bool EnablePlayerChunkAoi = true,
        // 32 tiles × 0.5m/tile = 16m per chunk side, matching the terrain chunk size.
        int PlayerChunkSizeGridUnits = 32,
        int PlayerChunkAoiRadius = 1,
        bool EnablePlayerRecipientSelectorFastPath = true,
        bool EnablePlayerRecipientSelectorChunkGroupCacheKey = true,
        bool EnablePlayerPriorityLod = true,
        int PlayerPriorityLodNearCadenceTicks = 1,
        int PlayerPriorityLodOuterCadenceTicks = 2,
        int PlayerPriorityLodRing1MaxChunks = 3,
        int PlayerPriorityLodFarCadenceTicks = 3,
        bool EnablePlayerDenseObserverCadence = false,
        // Start threshold tuned to 15 based on benchmark: at spawn-grid topology, chunks peak at
        // ~30 subscribers. Threshold of 15 activates on most populated chunks (-24% tick_avg,
        // -73% rx bandwidth at 1280 CCU). Original default of 64 never fired in practice.
        int PlayerDenseObserverCadenceStartSubscribers = 15,
        int PlayerDenseObserverCadenceStepSubscribers = 32,
        int PlayerDenseObserverCadenceMaxTicks = 4,
        // Dense observer recipient cap: disabled by default.
        // Benchmarks show net-negative perf at realistic chunk densities (~30 subscribers/chunk):
        // the two-pass priority-recipient selection loop costs more than the ~20% fanout reduction
        // (30→24 recipients). Re-evaluate if subscriber counts reliably reach 64+ or if the
        // priority-chunk-radius selection logic is reworked.
        bool EnablePlayerDenseObserverRecipientCap = false,
        int PlayerDenseObserverRecipientCapStartSubscribers = 64,
        int PlayerDenseObserverRecipientCapMaxRecipients = 24,
        int PlayerDenseObserverRecipientPriorityChunkRadius = 0,
        int PlayerAoiDespawnGraceTicks = 0,
        bool EnablePlayerDualCorrectionStreams = true,
        bool EnablePlayerCorrectionBatching = true,
        bool EnablePlayerCorrectionDeadband = true,
        int PlayerCorrectionDeadbandUnits = 0,
        int PlayerCorrectionMaxSilentTicks = 1,
        int PlayerReplicationBuildBatchPacketLimit = 512,
        int MaxReplicationBuildWorkers = 0,
        int PlayerObserverStreamCadenceTicks = 1,
        bool EnableObserverSpatialFirstBuild = false,
        IAccountAuthenticationProvider? AccountAuthenticationProvider = null)
    {
        public static ServerRuntimeOptions Default { get; } = new("default");
    }
}
