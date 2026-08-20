using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UltraSim.Discovery.Official
{
    public sealed class OfficialRegistryDiscoveryQuery : IServerDiscoveryQuery
    {
        private readonly HttpClient _httpClient;
        private readonly object _queryLock = new();
        private OfficialRegistryServerQuery _query = new();

        public OfficialRegistryDiscoveryQuery(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public void SetQuery(OfficialRegistryServerQuery? query)
        {
            lock (_queryLock)
            {
                _query = query ?? new OfficialRegistryServerQuery();
            }
        }

        public async ValueTask<IReadOnlyList<ServerDiscoveryRow>> QueryAsync(CancellationToken cancellationToken = default)
        {
            string requestUri = BuildRequestUri();
            ServerDiscoveryRow[]? rows = await _httpClient.GetFromJsonAsync<ServerDiscoveryRow[]>(
                requestUri,
                cancellationToken).ConfigureAwait(false);
            return rows ?? Array.Empty<ServerDiscoveryRow>();
        }

        private string BuildRequestUri()
        {
            OfficialRegistryServerQuery query;
            lock (_queryLock)
            {
                query = _query;
            }

            List<string> parts = new();
            Append(parts, "search", query.Search);
            Append(parts, "region", query.Region);
            Append(parts, "language", query.Language);
            if (query.TrustLevel.HasValue)
                Append(parts, "trustLevel", ((int)query.TrustLevel.Value).ToString());
            if (query.PasswordRequired.HasValue)
                Append(parts, "passwordRequired", query.PasswordRequired.Value ? "true" : "false");
            if (query.WhitelistOnly.HasValue)
                Append(parts, "whitelistOnly", query.WhitelistOnly.Value ? "true" : "false");
            Append(parts, "compatibilityId", query.CompatibilityId);
            Append(parts, "sortBy", query.SortBy);
            if (query.Descending)
                Append(parts, "descending", "true");
            if (query.Offset > 0)
                Append(parts, "offset", query.Offset.ToString());
            if (query.Limit > 0 && query.Limit != 100)
                Append(parts, "limit", query.Limit.ToString());

            if (parts.Count == 0)
                return "/api/v1/servers";

            StringBuilder builder = new("/api/v1/servers?");
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                    builder.Append('&');

                builder.Append(parts[i]);
            }

            return builder.ToString();
        }

        private static void Append(List<string> parts, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value.Trim())}");
        }
    }
}
