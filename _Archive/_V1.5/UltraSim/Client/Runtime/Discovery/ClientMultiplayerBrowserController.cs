using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UltraSim.Discovery;

namespace UltraSim.Client.Runtime.Discovery
{
    public sealed class ClientMultiplayerBrowserController
    {
        private int _selectedRowIndex;

        public ClientMultiplayerBrowserController(IServerDiscoveryQuery? officialQuery = null)
        {
            Browser = ClientDefaultServerBrowserFactory.CreateDefault(
                out _officialQuery,
                out _favoritesQuery,
                out _lanQuery,
                out _manualQuery,
                officialQuery);
            _selectedRowIndex = -1;
        }

        private readonly IServerDiscoveryQuery _officialQuery;
        private readonly InMemoryServerDiscoveryQuery _favoritesQuery;
        private readonly InMemoryServerDiscoveryQuery _lanQuery;
        private readonly InMemoryServerDiscoveryQuery _manualQuery;

        public ClientServerBrowserService Browser { get; }
        public int SelectedRowIndex => _selectedRowIndex;
        public bool CanInjectOfficialRows => _officialQuery is InMemoryServerDiscoveryQuery;

        public void SetOfficialRows(IReadOnlyList<ServerDiscoveryRow>? rows) =>
            (_officialQuery as InMemoryServerDiscoveryQuery)?.ReplaceRows(rows);

        public void SetFavoriteRows(IReadOnlyList<ServerDiscoveryRow>? rows) =>
            _favoritesQuery.ReplaceRows(rows);

        public void SetLanRows(IReadOnlyList<ServerDiscoveryRow>? rows) =>
            _lanQuery.ReplaceRows(rows);

        public void SetManualEndpoint(string host, int port, string displayName = "Direct Connect")
        {
            string resolvedHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            int resolvedPort = port <= 0 ? 7777 : port;

            _manualQuery.ReplaceRows(new[]
            {
                new ServerDiscoveryRow(
                    ProviderId: "manual",
                    ProviderKind: ServerDiscoveryProviderKind.Manual,
                    ServerId: "manual-default",
                    DisplayName: displayName,
                    ConnectHint: $"{resolvedHost}:{resolvedPort}"),
            });
        }

        public bool SelectProvider(string providerId) =>
            SelectProviderInternal(providerId);

        public bool SelectNextProvider() =>
            SelectAdjacentProvider(+1);

        public bool SelectPreviousProvider() =>
            SelectAdjacentProvider(-1);

        public async ValueTask<ServerBrowserSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            ServerBrowserSnapshot snapshot = await Browser.RefreshAsync(cancellationToken).ConfigureAwait(false);
            ClampSelection(snapshot);
            return Browser.GetSnapshot();
        }

        public ServerBrowserSnapshot GetSnapshot() =>
            Browser.GetSnapshot();

        public bool SelectRow(int rowIndex)
        {
            ServerBrowserSnapshot snapshot = Browser.GetSnapshot();
            if (rowIndex < 0 || rowIndex >= snapshot.Rows.Count)
                return false;

            _selectedRowIndex = rowIndex;
            return true;
        }

        public string BuildStatusText(ServerBrowserSnapshot snapshot)
        {
            string providerName = GetActiveProviderDisplayName(snapshot);

            if (!string.IsNullOrWhiteSpace(snapshot.LastError))
                return $"{providerName}: {BuildFriendlyErrorText(snapshot)} manual connect available.";

            if (snapshot.Rows.Count == 0)
                return $"{providerName}: no servers found. manual connect available.";

            if (snapshot.Rows.Count == 1)
                return $"{providerName}: 1 server available - {snapshot.Rows[0].DisplayName}.";

            return $"{providerName}: {snapshot.Rows.Count} servers available.";
        }

        public IReadOnlyList<string> BuildServerListEntries(ServerBrowserSnapshot snapshot, int maxRows = 12)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.LastError))
                return new[] { BuildFriendlyErrorListEntry(snapshot) };

            if (snapshot.Rows.Count == 0)
                return new[] { "No servers available." };

            int limit = Math.Clamp(maxRows, 1, snapshot.Rows.Count);
            string[] lines = new string[limit];

            for (int i = 0; i < limit; i++)
            {
                ServerDiscoveryRow row = snapshot.Rows[i];
                string users = row.CurrentUsers.HasValue ? row.CurrentUsers.Value.ToString(CultureInfo.InvariantCulture) : "?";
                string firstLine = $"{row.DisplayName}{BuildTagText(row)}";
                string secondLine = $"Players: {users}{BuildSecondaryInfo(row)}";
                lines[i] = $"{firstLine}\n{secondLine}";
            }

            return lines;
        }

        public string BuildServerDetailText(ServerBrowserSnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.LastError))
                return $"Registry unavailable\n{BuildFriendlyErrorText(snapshot)}";

            if (snapshot.Rows.Count == 0 || _selectedRowIndex < 0)
                return "Select a provider or enter a direct connect target below.";

            ServerDiscoveryRow row = GetSelectedRow(snapshot);
            string users = row.CurrentUsers?.ToString(CultureInfo.InvariantCulture) ?? "?";
            string peak = row.PeakUsers?.ToString(CultureInfo.InvariantCulture) ?? "?";
            string ping = row.PingMs?.ToString(CultureInfo.InvariantCulture) ?? "--";

            return
                $"{row.DisplayName}\n" +
                $"Region: {row.Region ?? "Unknown"}\n" +
                $"Users: {users} / Peak: {peak}\n" +
                $"24h Players: --\n" +
                $"Ping: {ping} ms\n" +
                $"Password: {(row.PasswordRequired ? "Yes" : "No")}\n" +
                $"Whitelist: {(row.WhitelistOnly ? "Yes" : "No")}\n" +
                $"Connect: {row.ConnectHint ?? "Unavailable"}";
        }

        public bool TryGetSelectedConnectTarget(ServerBrowserSnapshot snapshot, out string host, out int port)
        {
            host = string.Empty;
            port = 0;

            if (snapshot.Rows.Count == 0 || _selectedRowIndex < 0)
                return false;

            return TryParseConnectHint(GetSelectedRow(snapshot).ConnectHint, out host, out port);
        }

        public bool TryGetPreferredConnectTarget(ServerBrowserSnapshot snapshot, out string host, out int port)
        {
            if (_selectedRowIndex >= 0 && TryParseConnectHint(GetSelectedRow(snapshot).ConnectHint, out host, out port))
                return true;

            if (TryResolveConnectTarget(snapshot.Rows, out host, out port))
                return true;

            ServerBrowserSnapshot fallback = Browser.GetSnapshot();
            if (TryResolveConnectTarget(fallback.Rows, out host, out port))
                return true;

            IReadOnlyList<ServerDiscoveryRow> manualRows = _manualQuery.QueryAsync().GetAwaiter().GetResult();
            return TryResolveConnectTarget(manualRows, out host, out port);
        }

        private string GetActiveProviderDisplayName(ServerBrowserSnapshot snapshot)
        {
            for (int i = 0; i < snapshot.Providers.Count; i++)
            {
                if (snapshot.Providers[i].IsSelected)
                    return snapshot.Providers[i].DisplayName;
            }

            return snapshot.ActiveProviderId;
        }

        private bool SelectAdjacentProvider(int direction)
        {
            ServerBrowserSnapshot snapshot = Browser.GetSnapshot();
            if (snapshot.Providers.Count == 0)
                return false;

            int currentIndex = 0;
            for (int i = 0; i < snapshot.Providers.Count; i++)
            {
                if (snapshot.Providers[i].IsSelected)
                {
                    currentIndex = i;
                    break;
                }
            }

            int nextIndex = (currentIndex + direction + snapshot.Providers.Count) % snapshot.Providers.Count;
            return SelectProviderInternal(snapshot.Providers[nextIndex].ProviderId);
        }

        private bool SelectProviderInternal(string providerId)
        {
            bool changed = Browser.SelectProvider(providerId);
            if (changed)
                _selectedRowIndex = -1;
            return changed;
        }

        private void ClampSelection(ServerBrowserSnapshot snapshot)
        {
            if (snapshot.Rows.Count == 0)
            {
                _selectedRowIndex = -1;
                return;
            }

            if (_selectedRowIndex >= snapshot.Rows.Count)
                _selectedRowIndex = snapshot.Rows.Count - 1;
        }

        private ServerDiscoveryRow GetSelectedRow(ServerBrowserSnapshot snapshot)
        {
            ClampSelection(snapshot);
            return snapshot.Rows[_selectedRowIndex];
        }

        private static bool TryResolveConnectTarget(IReadOnlyList<ServerDiscoveryRow> rows, out string host, out int port)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (TryParseConnectHint(rows[i].ConnectHint, out host, out port))
                    return true;
            }

            host = string.Empty;
            port = 0;
            return false;
        }

        private static bool TryParseConnectHint(string? connectHint, out string host, out int port)
        {
            host = string.Empty;
            port = 0;

            if (string.IsNullOrWhiteSpace(connectHint))
                return false;

            string value = connectHint.Trim();
            int colonIndex = value.LastIndexOf(':');
            if (colonIndex <= 0 || colonIndex >= value.Length - 1)
                return false;

            string parsedHost = value.Substring(0, colonIndex).Trim();
            string parsedPort = value.Substring(colonIndex + 1).Trim();

            if (string.IsNullOrWhiteSpace(parsedHost))
                return false;

            if (!int.TryParse(parsedPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPortValue))
                return false;

            host = parsedHost;
            port = parsedPortValue;
            return true;
        }

        private static string BuildTagText(ServerDiscoveryRow row)
        {
            List<string> tags = new();

            if (row.PasswordRequired)
                tags.Add("[pw]");

            if (row.WhitelistOnly)
                tags.Add("[wl]");

            if (!string.IsNullOrWhiteSpace(row.Region))
                tags.Add($"[{row.Region}]");

            return tags.Count == 0 ? string.Empty : "  " + string.Join(" ", tags);
        }

        private static string BuildSecondaryInfo(ServerDiscoveryRow row)
        {
            List<string> parts = new();

            if (row.Uptime.HasValue)
                parts.Add($"UpTime {FormatUptime(row.Uptime.Value)}");

            if (row.PingMs.HasValue)
                parts.Add($"{row.PingMs.Value.ToString(CultureInfo.InvariantCulture)}ms");

            return parts.Count == 0 ? string.Empty : "    " + string.Join("    ", parts);
        }

        private static string BuildFriendlyErrorListEntry(ServerBrowserSnapshot snapshot) =>
            string.Equals(snapshot.ActiveProviderId, "official", StringComparison.Ordinal)
                ? "Could not connect to server registry."
                : "Could not refresh server list.";

        private static string BuildFriendlyErrorText(ServerBrowserSnapshot snapshot) =>
            string.Equals(snapshot.ActiveProviderId, "official", StringComparison.Ordinal)
                ? "Could not connect to server registry. Please try again later."
                : "Could not refresh the selected server list. Please try again later.";

        private static string FormatUptime(TimeSpan uptime)
        {
            if (uptime.TotalDays >= 1.0)
                return $"{(int)uptime.TotalDays}d {uptime.Hours}h";

            if (uptime.TotalHours >= 1.0)
                return $"{uptime.Hours}h {uptime.Minutes}m";

            if (uptime.TotalMinutes >= 1.0)
                return $"{uptime.Minutes}m";

            return $"{Math.Max(0, uptime.Seconds)}s";
        }
    }
}
