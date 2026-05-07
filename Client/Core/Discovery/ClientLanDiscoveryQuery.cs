using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using UltraSim.Discovery;

namespace UltraSim.Client;

public sealed class ClientLanDiscoveryQuery : IServerDiscoveryQuery
{
    private readonly ClientLanDiscoveryOptions _options;

    public ClientLanDiscoveryQuery(ClientLanDiscoveryOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<IReadOnlyList<ServerDiscoveryRow>> QueryAsync(CancellationToken cancellationToken = default)
    {
        string nonce = Guid.NewGuid().ToString("N");
        byte[] requestBytes = LanDiscoveryMessageCodec.EncodeRequest(
            LanDiscoveryRequest.Create(nonce, _options.CompatibilityId));

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(_options.ListenEndPoint);

        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < _options.QueryEndpoints.Count; i++)
        {
            IPEndPoint endpoint = _options.QueryEndpoints[i];
            await udp.SendAsync(requestBytes, endpoint, cancellationToken).ConfigureAwait(false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        var rows = new List<ServerDiscoveryRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (rows.Count < _options.MaxResponses)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!LanDiscoveryMessageCodec.TryDecodeResponse(result.Buffer, out LanDiscoveryResponse response) ||
                response.Nonce != nonce)
            {
                continue;
            }

            ServerDiscoveryRow row = NormalizeRow(response.Row, result.RemoteEndPoint, (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds));
            string key = $"{row.Host}:{row.Port}";
            if (seen.Add(key))
                rows.Add(row);
        }

        return rows;
    }

    private static ServerDiscoveryRow NormalizeRow(ServerDiscoveryRow row, IPEndPoint remoteEndPoint, int pingMs)
    {
        string host = string.IsNullOrWhiteSpace(row.Host)
            ? remoteEndPoint.Address.ToString()
            : row.Host.Trim();

        return row with
        {
            ProviderId = "lan",
            ProviderKind = ServerDiscoveryProviderKind.Lan,
            Host = host,
            PingMs = row.PingMs ?? Math.Max(0, pingMs),
            LastSeenUtc = DateTimeOffset.UtcNow,
        };
    }
}
