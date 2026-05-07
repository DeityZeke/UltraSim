using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UltraSim.Discovery.Official
{
    public sealed class OfficialRegistryDiscoveryPublisher : IServerDiscoveryPublisher
    {
        private readonly HttpClient _httpClient;
        private readonly DiscoveryPublishMode _publishMode;
        private readonly string? _publishToken;
        private readonly Func<string?>? _ownershipTokenHashProvider;
        private string? _leaseId;
        private string? _heartbeatToken;
        private string? _serverId;

        public OfficialRegistryDiscoveryPublisher(
            HttpClient httpClient,
            DiscoveryPublishMode publishMode,
            string? publishToken = null,
            Func<string?>? ownershipTokenHashProvider = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _publishMode = publishMode;
            _publishToken = string.IsNullOrWhiteSpace(publishToken) ? null : publishToken.Trim();
            _ownershipTokenHashProvider = ownershipTokenHashProvider;
        }

        public async ValueTask PublishAsync(
            ServerAdvertisedInfo advertisedInfo,
            ServerDiscoveryStatusInfo statusInfo,
            CancellationToken cancellationToken = default)
        {
            if (_publishMode == DiscoveryPublishMode.Disabled)
                return;

            if (string.IsNullOrWhiteSpace(_leaseId) || string.IsNullOrWhiteSpace(_heartbeatToken))
            {
                OfficialRegistryRegisterRequest register = new(
                    _publishMode,
                    advertisedInfo,
                    statusInfo,
                    _publishToken,
                    _ownershipTokenHashProvider?.Invoke());

                using HttpResponseMessage registerResponse = await _httpClient.PostAsJsonAsync(
                    "/api/v1/servers/register",
                    register,
                    cancellationToken).ConfigureAwait(false);
                registerResponse.EnsureSuccessStatusCode();

                OfficialRegistryRegisterResponse payload =
                    (await registerResponse.Content.ReadFromJsonAsync<OfficialRegistryRegisterResponse>(cancellationToken: cancellationToken).ConfigureAwait(false))!;

                _leaseId = payload.LeaseId;
                _heartbeatToken = payload.HeartbeatToken;
                _serverId = payload.ServerId;
                return;
            }

            OfficialRegistryHeartbeatRequest heartbeat = new(
                _heartbeatToken!,
                statusInfo,
                advertisedInfo,
                _ownershipTokenHashProvider?.Invoke());

            using HttpResponseMessage heartbeatResponse = await _httpClient.PutAsJsonAsync(
                $"/api/v1/servers/{_leaseId}",
                heartbeat,
                cancellationToken).ConfigureAwait(false);

            if (heartbeatResponse.IsSuccessStatusCode)
                return;

            _leaseId = null;
            _heartbeatToken = null;
            _serverId = null;

            heartbeatResponse.EnsureSuccessStatusCode();
        }

        public async ValueTask UnpublishAsync(
            string serverId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_leaseId) || string.IsNullOrWhiteSpace(_heartbeatToken))
                return;

            using HttpRequestMessage request = new(HttpMethod.Delete, $"/api/v1/servers/{_leaseId}");
            request.Headers.Add("X-Heartbeat-Token", _heartbeatToken);
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            _leaseId = null;
            _heartbeatToken = null;
            _serverId = null;
        }

        public async ValueTask SendPresenceAsync(
            ServerDiscoveryPresenceSignal signal,
            ServerDiscoveryStatusInfo? statusInfo = null,
            CancellationToken cancellationToken = default)
        {
            if (_publishMode == DiscoveryPublishMode.Disabled)
                return;

            if (string.IsNullOrWhiteSpace(_serverId) || string.IsNullOrWhiteSpace(_heartbeatToken))
                return;

            OfficialRegistryPresenceRequest presence = new(
                _heartbeatToken!,
                (OfficialRegistryPresenceSignalKind)(byte)signal,
                statusInfo,
                OwnershipTokenHashHint: _ownershipTokenHashProvider?.Invoke());

            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                $"/api/v1/servers/{Uri.EscapeDataString(_serverId!)}/presence",
                presence,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
        }
    }
}
