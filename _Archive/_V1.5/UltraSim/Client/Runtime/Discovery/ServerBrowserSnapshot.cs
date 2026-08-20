using System;
using System.Collections.Generic;
using UltraSim.Discovery;

namespace UltraSim.Client.Runtime.Discovery
{
    public sealed record ServerBrowserSnapshot(
        string ActiveProviderId,
        IReadOnlyList<ServerBrowserProviderSnapshot> Providers,
        IReadOnlyList<ServerDiscoveryRow> Rows,
        bool IsRefreshing,
        DateTimeOffset? LastRefreshUtc,
        string? LastError);
}
