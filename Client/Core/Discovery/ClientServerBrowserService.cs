using UltraSim.Discovery;

namespace UltraSim.Client;

public sealed class ClientServerBrowserService
{
    private readonly IReadOnlyList<ServerBrowserProviderDefinition> _providers;
    private IReadOnlyList<ServerDiscoveryRow> _rows = Array.Empty<ServerDiscoveryRow>();
    private bool _isRefreshing;
    private DateTimeOffset? _lastRefreshUtc;
    private string? _lastError;

    public ClientServerBrowserService(
        IReadOnlyList<ServerBrowserProviderDefinition> providers,
        string? initialProviderId = null)
    {
        if (providers is null)
        {
            throw new ArgumentNullException(nameof(providers));
        }

        if (providers.Count == 0)
        {
            throw new ArgumentException("At least one discovery provider is required.", nameof(providers));
        }

        _providers = providers;
        ActiveProviderId = ResolveInitialProviderId(initialProviderId);
    }

    public string ActiveProviderId { get; private set; }

    public bool SelectProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return false;
        }

        string normalized = providerId.Trim();
        if (!ProviderExists(normalized))
        {
            return false;
        }

        ActiveProviderId = normalized;
        _rows = Array.Empty<ServerDiscoveryRow>();
        _lastError = null;
        _lastRefreshUtc = null;
        return true;
    }

    public async ValueTask<ServerBrowserSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_isRefreshing)
        {
            return GetSnapshot();
        }

        ServerBrowserProviderDefinition provider = GetActiveProvider();
        _isRefreshing = true;
        _lastError = null;

        try
        {
            _rows = await provider.Query.QueryAsync(cancellationToken).ConfigureAwait(false);
            _lastRefreshUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _rows = Array.Empty<ServerDiscoveryRow>();
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
        var providers = new ServerBrowserProviderSnapshot[_providers.Count];
        for (int i = 0; i < _providers.Count; i++)
        {
            ServerBrowserProviderDefinition provider = _providers[i];
            providers[i] = new ServerBrowserProviderSnapshot(
                provider.ProviderId,
                provider.DisplayName,
                provider.ProviderKind,
                string.Equals(provider.ProviderId, ActiveProviderId, StringComparison.Ordinal));
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
            {
                return _providers[i];
            }
        }

        return _providers[0];
    }

    private bool ProviderExists(string providerId)
    {
        for (int i = 0; i < _providers.Count; i++)
        {
            if (string.Equals(_providers[i].ProviderId, providerId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private string ResolveInitialProviderId(string? initialProviderId)
    {
        if (!string.IsNullOrWhiteSpace(initialProviderId))
        {
            string normalized = initialProviderId.Trim();
            if (ProviderExists(normalized))
            {
                return normalized;
            }
        }

        return _providers[0].ProviderId;
    }
}
