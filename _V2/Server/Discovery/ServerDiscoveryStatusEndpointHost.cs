using System.Net;
using System.Text.Json;
using UltraSim.Discovery;

namespace UltraSim.Server.Discovery;

public sealed class ServerDiscoveryStatusEndpointHost : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Uri _endpointUri;
    private readonly string _listenPrefix;
    private readonly string _path;
    private readonly Func<ServerStatusSnapshot> _statusProvider;
    private readonly string? _serverId;
    private readonly string? _ownershipTokenHash;
    private readonly Func<DateTimeOffset> _clock;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _listenTask;
    private bool _disposed;

    public ServerDiscoveryStatusEndpointHost(
        string endpointUrl,
        Func<ServerStatusSnapshot> statusProvider,
        string? serverId = null,
        string? ownershipTokenHash = null,
        Func<DateTimeOffset>? clock = null)
    {
        _endpointUri = NormalizeEndpointUri(endpointUrl);
        _listenPrefix = BuildListenPrefix(_endpointUri);
        _path = string.IsNullOrWhiteSpace(_endpointUri.AbsolutePath)
            ? "/"
            : _endpointUri.AbsolutePath;
        _statusProvider = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));
        _serverId = NormalizeOptional(serverId);
        _ownershipTokenHash = NormalizeOptional(ownershipTokenHash);
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    public Uri EndpointUri => _endpointUri;

    public string ListenPrefix => _listenPrefix;

    public bool IsRunning => _listener.IsListening;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listener.IsListening)
            return;

        _listener.Prefixes.Add(_listenPrefix);
        _listener.Start();
        _listenTask = Task.Run(ListenLoopAsync);
    }

    public async ValueTask StopAsync()
    {
        if (!_listener.IsListening && _listenTask is null)
            return;

        _stop.Cancel();
        if (_listener.IsListening)
            _listener.Stop();

        if (_listenTask is not null)
        {
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (HttpListenerException)
            {
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stop.Cancel();
        if (_listener.IsListening)
            _listener.Stop();
        _listener.Close();
        _stop.Dispose();
    }

    private async Task ListenLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException) when (_stop.IsCancellationRequested || !_listener.IsListening)
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(context), _stop.Token);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(context.Request.HttpMethod, "GET"))
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            string requestPath = string.IsNullOrWhiteSpace(context.Request.Url?.AbsolutePath)
                ? "/"
                : context.Request.Url.AbsolutePath;
            if (!StringComparer.Ordinal.Equals(requestPath, _path))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            ServerDiscoveryPollStatusResponse response =
                ServerDiscoveryContractBuilder.BuildPollStatusResponse(
                    _statusProvider(),
                    _serverId,
                    _ownershipTokenHash,
                    heartbeatUtc: _clock());

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(
                    context.Response.OutputStream,
                    response,
                    JsonOptions,
                    _stop.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static Uri NormalizeEndpointUri(string endpointUrl)
    {
        if (!Uri.TryCreate(NormalizeRequired(endpointUrl, nameof(endpointUrl)), UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("Discovery status endpoint URL must be an absolute http URL for the embedded status host.", nameof(endpointUrl));
        }

        if (!string.IsNullOrWhiteSpace(uri.Query) || !string.IsNullOrWhiteSpace(uri.Fragment))
            throw new ArgumentException("Discovery status endpoint URL must not include a query string or fragment.", nameof(endpointUrl));

        if (uri.Port <= 0)
            throw new ArgumentException("Discovery status endpoint URL must include a port.", nameof(endpointUrl));

        return uri;
    }

    private static string BuildListenPrefix(Uri uri)
    {
        string host = uri.HostNameType == UriHostNameType.IPv6
            ? $"[{uri.Host}]"
            : uri.Host;
        return $"{uri.Scheme}://{host}:{uri.Port}/";
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        string? normalized = NormalizeOptional(value);
        if (normalized is null)
            throw new ArgumentException("Discovery value is required.", parameterName);

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
