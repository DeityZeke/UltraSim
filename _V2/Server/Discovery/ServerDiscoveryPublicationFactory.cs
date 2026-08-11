using System.Net.Http;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;

namespace UltraSim.Server.Discovery;

public static class ServerDiscoveryPublicationFactory
{
    public static ServerDiscoveryPublicationPlan CreateOfficialRegistryPlan(
        ServerDiscoveryPublicationSettings settings,
        HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(httpClient);

        DiscoveryPublishMode publishMode = settings.Enabled
            ? ParsePublishMode(settings.PublishMode)
            : DiscoveryPublishMode.Disabled;
        Uri registryUri = NormalizeRegistryUri(settings.RegistryUrl);
        httpClient.BaseAddress ??= registryUri;

        var publisher = new OfficialRegistryDiscoveryPublisher(
            httpClient,
            publishMode,
            settings.PublishToken,
            NormalizeOptional(settings.OwnershipTokenHash) is { } ownershipTokenHash
                ? () => ownershipTokenHash
                : null);

        return new ServerDiscoveryPublicationPlan(
            registryUri,
            publishMode,
            publisher,
            BuildAdvertisedSettings(settings),
            BuildPublisherOptions(settings, enabled: publishMode != DiscoveryPublishMode.Disabled));
    }

    public static ServerDiscoveryAdvertisedSettings BuildAdvertisedSettings(
        ServerDiscoveryPublicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string serverId = NormalizeOptional(settings.ServerId)
            ?? NormalizeRequired(settings.DisplayName, nameof(settings.DisplayName));
        string primaryLanguage = NormalizeOptional(settings.PrimaryLanguage) ?? "en-US";

        return new ServerDiscoveryAdvertisedSettings(
            ServerId: serverId,
            DisplayName: NormalizeRequired(settings.DisplayName, nameof(settings.DisplayName)),
            AdvertisedHost: NormalizeRequired(settings.AdvertisedHost, nameof(settings.AdvertisedHost)),
            Port: settings.Port,
            ProtocolVersion: NormalizeOptional(settings.ProtocolVersion),
            ContentVersion: NormalizeOptional(settings.ContentVersion),
            PasswordRequired: settings.PasswordRequired,
            WhitelistOnly: settings.WhitelistOnly,
            Region: NormalizeOptional(settings.Region),
            Description: NormalizeOptional(settings.Description),
            IsPublic: settings.Visibility == ServerListingVisibility.Public,
            Tags: SplitList(settings.Tags),
            PrimaryLanguage: primaryLanguage,
            Languages: [primaryLanguage],
            MaxUsers: settings.MaxUsers <= 0 ? null : settings.MaxUsers,
            Modded: settings.Modded,
            Visibility: settings.Visibility,
            CompatibilityId: NormalizeOptional(settings.CompatibilityId),
            CapabilityFlags: settings.CapabilityFlags,
            StatusEndpointUrl: NormalizeOptional(settings.StatusEndpointUrl));
    }

    public static ServerDiscoveryPublisherOptions BuildPublisherOptions(
        ServerDiscoveryPublicationSettings settings,
        bool? enabled = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ServerDiscoveryPublisherOptions(
            Enabled: enabled ?? settings.Enabled,
            HeartbeatInterval: TimeSpan.FromSeconds(Math.Max(1.0, settings.HeartbeatIntervalSeconds)),
            UnpublishOnStop: settings.UnpublishOnStop);
    }

    public static DiscoveryPublishMode ParsePublishMode(string? value)
    {
        string normalized = NormalizeOptional(value) ?? "unverified";
        return normalized.ToLowerInvariant() switch
        {
            "disabled" or "off" => DiscoveryPublishMode.Disabled,
            "unverified" => DiscoveryPublishMode.Unverified,
            "verified" => DiscoveryPublishMode.Verified,
            _ => throw new ArgumentException($"Unsupported discovery publish mode '{value}'.", nameof(value))
        };
    }

    private static Uri NormalizeRegistryUri(string? value)
    {
        string normalized = NormalizeRequired(value, nameof(value));
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Discovery registry URL must be an absolute http or https URL.", nameof(value));
        }

        return uri;
    }

    private static string[] SplitList(string? value)
    {
        string? normalized = NormalizeOptional(value);
        if (normalized is null)
            return [];

        return normalized
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        string? normalized = NormalizeOptional(value);
        return normalized ?? throw new ArgumentException("Discovery value is required.", parameterName);
    }

    private static string? NormalizeOptional(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
