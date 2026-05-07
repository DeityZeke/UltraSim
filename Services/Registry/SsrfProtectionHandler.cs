using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace UltraSim.Registry;

/// <summary>
/// HTTP DelegatingHandler that enforces SSRF protection at poll time by resolving
/// the request hostname and rejecting any address that falls in a blocked range.
///
/// This catches what static URL validation at registration cannot: hostnames that
/// appear public but resolve to private IPs, and DNS rebinding (where a hostname
/// that resolved to a public IP at registration later resolves to an internal one).
///
/// Fail-closed: DNS resolution failure blocks the request rather than allowing it.
/// </summary>
internal sealed class SsrfProtectionHandler : DelegatingHandler
{
    private readonly ILogger<SsrfProtectionHandler> _logger;
    private readonly IOptions<OfficialRegistryOptions> _options;

    public SsrfProtectionHandler(
        ILogger<SsrfProtectionHandler> logger,
        IOptions<OfficialRegistryOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Uri? uri = request.RequestUri;
        if (uri != null)
        {
            bool allowPrivateTargets = _options.Value.AllowPrivateStatusEndpointTargets;
            string host = uri.Host;

            // Block literal hostnames and IP literals that are in blocked ranges.
            if (SsrfGuard.IsBlockedHost(host, allowPrivateTargets))
            {
                _logger.LogWarning("SSRF blocked: host literal '{Host}' is not allowed.", host);
                throw new InvalidOperationException($"Polling target host '{host}' is not allowed.");
            }

            // For non-IP hostnames, resolve and check every returned address.
            if (!IPAddress.TryParse(host, out _))
            {
                IPAddress[]? addresses = null;
                try
                {
                    IPHostEntry entry = await Dns.GetHostEntryAsync(host, cancellationToken)
                        .ConfigureAwait(false);
                    addresses = entry.AddressList;
                }
                catch (SocketException ex)
                {
                    // Fail closed: if we can't resolve, we won't poll.
                    _logger.LogWarning(
                        "SSRF blocked: DNS resolution failed for host '{Host}': {Message}",
                        host, ex.Message);
                    throw new InvalidOperationException(
                        $"Could not resolve polling target '{host}'. Request blocked.");
                }

                foreach (IPAddress address in addresses)
                {
                    if (!SsrfGuard.IsBlockedAddress(address, allowPrivateTargets))
                        continue;

                    _logger.LogWarning(
                        "SSRF blocked: host '{Host}' resolved to blocked address {Address}.",
                        host, address);
                    throw new InvalidOperationException(
                        $"Polling target '{host}' resolved to a blocked address. Request blocked.");
                }
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
