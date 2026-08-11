using System.Net;
using System.Net.Sockets;
using UltraSim.Discovery;

namespace UltraSim.Server.Discovery;

public sealed class ServerLanDiscoveryResponderHost : IDisposable
{
    private readonly IPEndPoint _listenEndPoint;
    private readonly Func<ServerDiscoveryRow> _rowFactory;
    private UdpClient? _udp;
    private CancellationTokenSource? _stop;
    private Task? _receiveLoop;

    public ServerLanDiscoveryResponderHost(
        Func<ServerDiscoveryRow> rowFactory,
        IPEndPoint? listenEndPoint = null)
    {
        _rowFactory = rowFactory ?? throw new ArgumentNullException(nameof(rowFactory));
        _listenEndPoint = listenEndPoint ?? new IPEndPoint(IPAddress.Any, LanDiscoveryDefaults.Port);
    }

    public bool IsRunning => _receiveLoop is { IsCompleted: false };
    public IPEndPoint? LocalEndPoint => _udp?.Client.LocalEndPoint as IPEndPoint;

    public void Start()
    {
        if (IsRunning)
            return;

        _stop = new CancellationTokenSource();
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(_listenEndPoint);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_stop.Token));
    }

    public void Stop()
    {
        _stop?.Cancel();
        _udp?.Close();
        try
        {
            _receiveLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException or ObjectDisposedException or SocketException))
        {
        }
        finally
        {
            _udp?.Dispose();
            _udp = null;
            _stop?.Dispose();
            _stop = null;
            _receiveLoop = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        UdpClient udp = _udp ?? throw new InvalidOperationException("LAN discovery responder has not been started.");
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                continue;
            }

            if (!LanDiscoveryMessageCodec.TryDecodeRequest(result.Buffer, out LanDiscoveryRequest request))
                continue;

            ServerDiscoveryRow row = _rowFactory();
            byte[] response = LanDiscoveryMessageCodec.EncodeResponse(LanDiscoveryResponse.Create(request.Nonce, row));
            if (response.Length > LanDiscoveryDefaults.MaxDatagramBytes)
                continue;

            try
            {
                await udp.SendAsync(response, result.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
            }
        }
    }
}
