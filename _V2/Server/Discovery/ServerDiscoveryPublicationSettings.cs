using UltraSim.Discovery;

namespace UltraSim.Server.Discovery;

public sealed record ServerDiscoveryPublicationSettings(
    bool Enabled,
    string RegistryUrl,
    string PublishMode,
    string ServerId,
    string DisplayName,
    string AdvertisedHost,
    int Port,
    int MaxUsers,
    string? Description = null,
    string? Region = null,
    string? PrimaryLanguage = null,
    string? Tags = null,
    double HeartbeatIntervalSeconds = 15.0,
    bool UnpublishOnStop = true,
    string? StatusEndpointUrl = null,
    string? PublishToken = null,
    string? OwnershipTokenHash = null,
    string? ProtocolVersion = null,
    string? ContentVersion = null,
    bool PasswordRequired = false,
    bool WhitelistOnly = false,
    bool Modded = false,
    ServerListingVisibility Visibility = ServerListingVisibility.Public,
    string? CompatibilityId = null,
    ServerCapabilityFlags CapabilityFlags = ServerCapabilityFlags.None);
