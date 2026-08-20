using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UltraSim.Discovery;

namespace UltraSim.Client.Runtime.Discovery
{
    public sealed class ClientServerBrowserService
    {
        private readonly IReadOnlyList<ServerBrowserProviderDefinition> _providers;
        private List<ServerDiscoveryRow> _rows = new();
        private bool _isRefreshing;
        private DateTimeOffset? _lastRefreshUtc;
        private string? _lastError;

        public ClientServerBrowserService(
            IReadOnlyList<ServerBrowserProviderDefinition> providers,
            string? initialProviderId = null)
        {
            if (providers == null)
                throw new ArgumentNullException(nameof(providers));
            if (providers.Count == 0)
                throw new ArgumentException("At least one provider is required.", nameof(providers));

            _providers = providers;
            ActiveProviderId = ResolveInitialProviderId(initialProviderId);
        }

        public string ActiveProviderId { get; private set; }

        public bool SelectProvider(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
                return false;

            if (!_providers.Any(staticDefinition => string.Equals(staticDefinition.ProviderId, providerId, StringComparison.Ordinal)))
                return false;

            ActiveProviderId = providerId;
            _rows = new List<ServerDiscoveryRow>();
            _lastError = null;
            _lastRefreshUtc = null;
            return true;
        }

        public async ValueTask<ServerBrowserSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            if (_isRefreshing)
                return GetSnapshot();

            ServerBrowserProviderDefinition provider = GetActiveProvider();

            _isRefreshing = true;
            _lastError = null;

            try
            {
                IReadOnlyList<ServerDiscoveryRow> rows = await provider.Query.QueryAsync(cancellationToken).ConfigureAwait(false);
                _rows = rows.ToList();
                _lastRefreshUtc = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                _rows = new List<ServerDiscoveryRow>();
                _lastError = ex.Message;
            }
            finally
            {
                _isRefreshing = false;
            }

            return GetSnapshot();
        }

        public ServerBrowserSnapshot GetSnapshot()
        {
            List<ServerBrowserProviderSnapshot> providers = new(_providers.Count);
            for (int i = 0; i < _providers.Count; i++)
            {
                ServerBrowserProviderDefinition provider = _providers[i];
                providers.Add(new ServerBrowserProviderSnapshot(
                    provider.ProviderId,
                    provider.DisplayName,
                    provider.ProviderKind,
                    IsSelected: string.Equals(provider.ProviderId, ActiveProviderId, StringComparison.Ordinal)));
            }

            return new ServerBrowserSnapshot(
                ActiveProviderId,
                providers,
                _rows,
                _isRefreshing,
                _lastRefreshUtc,
                _lastError);
        }

        private ServerBrowserProviderDefinition GetActiveProvider()
        {
            for (int i = 0; i < _providers.Count; i++)
            {
                if (string.Equals(_providers[i].ProviderId, ActiveProviderId, StringComparison.Ordinal))
                    return _providers[i];
            }

            return _providers[0];
        }

        private string ResolveInitialProviderId(string? initialProviderId)
        {
            if (!string.IsNullOrWhiteSpace(initialProviderId))
            {
                for (int i = 0; i < _providers.Count; i++)
                {
                    if (string.Equals(_providers[i].ProviderId, initialProviderId, StringComparison.Ordinal))
                        return _providers[i].ProviderId;
                }
            }

            return _providers[0].ProviderId;
        }
    }
}
