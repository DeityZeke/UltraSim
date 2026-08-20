using System;

namespace UltraSim.Discovery.Official
{
    public sealed record OfficialRegistryRegisterResponse(
        string LeaseId,
        string ServerId,
        string HeartbeatToken,
        DateTimeOffset ExpiresAtUtc,
        ServerListingTrustLevel TrustLevel,
        ServerListingVisibility Visibility,
        string? RegistrationId = null);
}
