using UltraSim.Discovery;

namespace UltraSim.Server.Discovery;

public static class ServerDiscoveryContractBuilder
{
    public static ServerDiscoveryRow BuildRow(
        ServerDiscoveryAdvertisedSettings settings,
        ServerStatusSnapshot status,
        string providerId = "lan",
        ServerDiscoveryProviderKind providerKind = ServerDiscoveryProviderKind.Lan,
        ServerListingTrustLevel trustLevel = ServerListingTrustLevel.Unverified,
        ServerListingHealthState healthState = ServerListingHealthState.Online,
        DateTimeOffset? lastSeenUtc = null)
    {
        ServerAdvertisedInfo advertised = BuildAdvertisedInfo(settings);
        ServerDiscoveryStatusInfo statusInfo = BuildStatusInfo(status);

        return new ServerDiscoveryRow(
            ProviderId: NormalizeRequired(providerId, nameof(providerId)),
            ProviderKind: providerKind,
            ServerId: advertised.ServerId,
            DisplayName: advertised.DisplayName,
            Host: settings.AdvertisedHost,
            Port: settings.Port,
            TrustLevel: trustLevel,
            HealthState: healthState,
            Visibility: advertised.Visibility,
            ConnectTargetKind: advertised.ConnectTargetKind,
            Region: advertised.Region,
            PrimaryLanguage: advertised.PrimaryLanguage,
            CurrentUsers: statusInfo.CurrentUsers,
            MaxUsers: advertised.MaxUsers,
            PeakUsers: statusInfo.PeakUsers,
            QueueLength: statusInfo.QueueLength,
            Uptime: statusInfo.Uptime,
            PasswordRequired: advertised.PasswordRequired,
            WhitelistOnly: advertised.WhitelistOnly,
            ConnectHint: advertised.ConnectHint,
            CompatibilityId: advertised.CompatibilityId,
            VersionLabel: advertised.ContentVersion ?? advertised.ProtocolVersion,
            Tags: advertised.TagsOrEmpty,
            CapabilityFlags: advertised.CapabilityFlags,
            LastSeenUtc: lastSeenUtc ?? DateTimeOffset.UtcNow);
    }

    public static ServerAdvertisedInfo BuildAdvertisedInfo(ServerDiscoveryAdvertisedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string serverId = NormalizeRequired(settings.ServerId, nameof(settings.ServerId));
        string displayName = NormalizeRequired(settings.DisplayName, nameof(settings.DisplayName));
        string advertisedHost = NormalizeRequired(settings.AdvertisedHost, nameof(settings.AdvertisedHost));
        if (settings.Port <= 0 || settings.Port > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(settings.Port), "Discovery advertised port must be between 1 and 65535.");

        ServerCapabilityFlags flags = settings.CapabilityFlags;
        if (settings.ConnectTargetKind == ConnectTargetKind.Direct)
            flags |= ServerCapabilityFlags.DirectConnect;
        if (settings.PasswordRequired)
            flags |= ServerCapabilityFlags.PasswordRequired;
        if (settings.WhitelistOnly)
            flags |= ServerCapabilityFlags.WhitelistOnly;
        if (settings.Modded)
            flags |= ServerCapabilityFlags.Mods;

        return new ServerAdvertisedInfo(
            ServerId: serverId,
            DisplayName: displayName,
            ConnectHint: $"{advertisedHost}:{settings.Port}",
            ConnectTargetKind: settings.ConnectTargetKind,
            ProtocolVersion: NormalizeOptional(settings.ProtocolVersion),
            ContentVersion: NormalizeOptional(settings.ContentVersion),
            PasswordRequired: settings.PasswordRequired,
            WhitelistOnly: settings.WhitelistOnly,
            Region: NormalizeOptional(settings.Region),
            Description: NormalizeOptional(settings.Description),
            IsPublic: settings.IsPublic,
            Tags: NormalizeList(settings.TagsOrEmpty),
            PrimaryLanguage: NormalizeOptional(settings.PrimaryLanguage),
            Languages: NormalizeList(settings.LanguagesOrEmpty),
            WebsiteUrl: NormalizeOptional(settings.WebsiteUrl),
            DiscordUrl: NormalizeOptional(settings.DiscordUrl),
            MaxUsers: settings.MaxUsers,
            Modded: settings.Modded,
            Visibility: settings.Visibility,
            CompatibilityId: NormalizeOptional(settings.CompatibilityId),
            CapabilityFlags: flags,
            StatusEndpointUrl: NormalizeOptional(settings.StatusEndpointUrl));
    }

    public static ServerDiscoveryStatusInfo BuildStatusInfo(
        ServerStatusSnapshot status,
        int? queueLength = null,
        int? rolling24HourUsers = null,
        DateTimeOffset? heartbeatUtc = null)
    {
        return new ServerDiscoveryStatusInfo(
            IsAcceptingConnections: status.IsRunning && status.IsAcceptingConnections,
            CurrentUsers: Math.Max(0, status.ActiveSessions),
            PeakUsers: Math.Max(0, status.PeakSessions),
            Uptime: status.Uptime < TimeSpan.Zero ? TimeSpan.Zero : status.Uptime,
            Status: status.State.ToString(),
            Rolling24HourUsers: rolling24HourUsers,
            QueueLength: queueLength,
            LastHeartbeatUtc: heartbeatUtc);
    }

    public static ServerDiscoveryPollStatusResponse BuildPollStatusResponse(
        ServerStatusSnapshot status,
        string? serverId = null,
        string? ownershipTokenHash = null,
        int? queueLength = null,
        int? rolling24HourUsers = null,
        DateTimeOffset? heartbeatUtc = null)
    {
        return new ServerDiscoveryPollStatusResponse(
            NormalizeOptional(serverId),
            BuildStatusInfo(status, queueLength, rolling24HourUsers, heartbeatUtc),
            NormalizeOptional(ownershipTokenHash));
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        string? normalized = NormalizeOptional(value);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Discovery value is required.", parameterName);

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string[] NormalizeList(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return Array.Empty<string>();

        var normalized = new List<string>(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            string? value = NormalizeOptional(values[i]);
            if (value is not null)
                normalized.Add(value);
        }

        return normalized.Count == 0 ? Array.Empty<string>() : normalized.ToArray();
    }
}
