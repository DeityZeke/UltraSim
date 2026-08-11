using UltraSim.Discovery;

namespace UltraSim.Server.Discovery;

public sealed record ServerDiscoveryAdvertisedSettings(
    string ServerId,
    string DisplayName,
    string AdvertisedHost,
    int Port,
    ConnectTargetKind ConnectTargetKind = ConnectTargetKind.Direct,
    string? ProtocolVersion = null,
    string? ContentVersion = null,
    bool PasswordRequired = false,
    bool WhitelistOnly = false,
    string? Region = null,
    string? Description = null,
    bool IsPublic = true,
    IReadOnlyList<string>? Tags = null,
    string? PrimaryLanguage = null,
    IReadOnlyList<string>? Languages = null,
    string? WebsiteUrl = null,
    string? DiscordUrl = null,
    int? MaxUsers = null,
    bool Modded = false,
    ServerListingVisibility Visibility = ServerListingVisibility.Public,
    string? CompatibilityId = null,
    ServerCapabilityFlags CapabilityFlags = ServerCapabilityFlags.None,
    string? StatusEndpointUrl = null)
{
    public IReadOnlyList<string> TagsOrEmpty => Tags ?? Array.Empty<string>();

    public IReadOnlyList<string> LanguagesOrEmpty => Languages ?? Array.Empty<string>();
}
