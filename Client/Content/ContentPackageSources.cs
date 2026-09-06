using System.Net;
using System.Net.Http.Headers;

namespace UltraSim.Client.Content;

public readonly record struct ContentPackageSourceLimits
{
    public ContentPackageSourceLimits(
        long maximumArchiveBytes,
        int requestTimeoutMilliseconds,
        bool allowInsecureServerHostedHttp,
        bool allowPrivateNetworkTargets)
    {
        if (maximumArchiveBytes is < 1 or > int.MaxValue ||
            requestTimeoutMilliseconds is < 1 or > 300_000)
            throw new ArgumentOutOfRangeException(nameof(maximumArchiveBytes));
        MaximumArchiveBytes = maximumArchiveBytes;
        RequestTimeoutMilliseconds = requestTimeoutMilliseconds;
        AllowInsecureServerHostedHttp = allowInsecureServerHostedHttp;
        AllowPrivateNetworkTargets = allowPrivateNetworkTargets;
    }

    public static ContentPackageSourceLimits Default { get; } = new(
        maximumArchiveBytes: 768L * 1024 * 1024,
        requestTimeoutMilliseconds: 30_000,
        allowInsecureServerHostedHttp: false,
        allowPrivateNetworkTargets: false);

    public long MaximumArchiveBytes { get; }
    public int RequestTimeoutMilliseconds { get; }
    public bool AllowInsecureServerHostedHttp { get; }
    public bool AllowPrivateNetworkTargets { get; }

    internal static ContentPackageSourceLimits Resolve(ContentPackageSourceLimits limits) =>
        limits.MaximumArchiveBytes == 0 ? Default : limits;
}

public readonly record struct ContentPackageSourceSnapshot(
    ulong Requests,
    ulong Completed,
    ulong Rejected,
    long ArchiveBytesRead);

/// <summary>Reads canonical package envelopes below one explicitly configured local root.</summary>
public sealed class LocalContentPackageSource : IContentPackageSource
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private readonly string _root;
    private readonly ContentPackageSourceLimits _limits;
    private readonly ContentLimits _contentLimits;
    private ulong _requests;
    private ulong _completed;
    private ulong _rejected;
    private long _archiveBytesRead;

    public LocalContentPackageSource(
        string rootPath,
        ContentPackageSourceLimits limits = default,
        ContentLimits contentLimits = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("The package root is required.", nameof(rootPath));
        _root = Path.GetFullPath(rootPath);
        _limits = ContentPackageSourceLimits.Resolve(limits);
        _contentLimits = ContentLimits.Resolve(contentLimits);
        if (!Directory.Exists(_root) || IsReparsePoint(_root))
            throw new ArgumentException("The package root must be an existing ordinary directory.", nameof(rootPath));
    }

    public bool CanFetch(ContentSourceKind kind) => kind == ContentSourceKind.Local;

    public ContentPackageSourceSnapshot GetSnapshot() =>
        new(_requests, _completed, _rejected, _archiveBytesRead);

    public bool TryFetch(
        in ContentPackageFetchRequest request,
        out FetchedContentPackage? package,
        out ContentSourceFetchError error)
    {
        SaturatingIncrement(ref _requests);
        package = null;
        if (!request.IsValid || request.Source.Kind != ContentSourceKind.Local)
            return Reject(ContentSourceFetchError.InvalidRequest, out error);
        try
        {
            string relative = request.Source.Locator.Replace('/', Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(_root, relative));
            string prefix = _root.EndsWith(Path.DirectorySeparatorChar)
                ? _root
                : _root + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, PathComparison) || !PathIsSafe(path) || !File.Exists(path))
                return Reject(ContentSourceFetchError.Unavailable, out error);
            var info = new FileInfo(path);
            if (info.Length < 1 || info.Length > _limits.MaximumArchiveBytes || info.Length > int.MaxValue)
                return Reject(ContentSourceFetchError.CapacityExceeded, out error);
            byte[] bytes = File.ReadAllBytes(path);
            _archiveBytesRead = checked(_archiveBytesRead + bytes.LongLength);
            if (!ContentPackageArchive.TryDecodeCanonical(
                    bytes, request.PackageId, request.ManifestHash, out package, _contentLimits))
                return Reject(ContentSourceFetchError.SourceFailure, out error);
            SaturatingIncrement(ref _completed);
            error = ContentSourceFetchError.None;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           ArgumentException or NotSupportedException or OverflowException)
        {
            return Reject(ContentSourceFetchError.Unavailable, out error);
        }
    }

    private bool PathIsSafe(string path)
    {
        string? current = path;
        while (current is not null && current.StartsWith(_root, PathComparison))
        {
            if (File.Exists(current) || Directory.Exists(current))
                if (IsReparsePoint(current)) return false;
            if (string.Equals(current, _root, PathComparison)) return true;
            current = Path.GetDirectoryName(current);
        }
        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }

    private bool Reject(ContentSourceFetchError value, out ContentSourceFetchError error)
    {
        SaturatingIncrement(ref _rejected);
        error = value;
        return false;
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }
}

/// <summary>
/// One-request-at-a-time HTTP(S) package source. URLs are authenticated authorization hints only;
/// the canonical envelope, manifest, payload hashes, and passive validators decide acceptance.
/// </summary>
public sealed class HttpContentPackageSource : IContentPackageSource, IDisposable
{
    private readonly HttpClient _client;
    private readonly ContentPackageSourceLimits _limits;
    private readonly ContentLimits _contentLimits;
    private ulong _requests;
    private ulong _completed;
    private ulong _rejected;
    private long _archiveBytesRead;
    private bool _disposed;

    public HttpContentPackageSource(
        ContentPackageSourceLimits limits = default,
        ContentLimits contentLimits = default)
        : this(CreateDefaultHandler(), limits, contentLimits)
    {
    }

    public HttpContentPackageSource(
        HttpMessageHandler handler,
        ContentPackageSourceLimits limits = default,
        ContentLimits contentLimits = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _limits = ContentPackageSourceLimits.Resolve(limits);
        _contentLimits = ContentLimits.Resolve(contentLimits);
        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public bool CanFetch(ContentSourceKind kind) =>
        kind is ContentSourceKind.ServerHostedStatic or ContentSourceKind.ExternalHttps;

    public ContentPackageSourceSnapshot GetSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ContentPackageSourceSnapshot(_requests, _completed, _rejected, _archiveBytesRead);
    }

    public bool TryFetch(
        in ContentPackageFetchRequest request,
        out FetchedContentPackage? package,
        out ContentSourceFetchError error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SaturatingIncrement(ref _requests);
        package = null;
        if (!request.IsValid || !TryAuthorizeUri(request.Source, out Uri? uri))
            return Reject(ContentSourceFetchError.InvalidRequest, out error);

        try
        {
            using var cancellation = new CancellationTokenSource(_limits.RequestTimeoutMilliseconds);
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
                "application/vnd.ultrasim.content-package"));
            using HttpResponseMessage response = _client.Send(
                message, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri != uri)
                return Reject(ContentSourceFetchError.Unavailable, out error);
            long? advertised = response.Content.Headers.ContentLength;
            if (advertised.HasValue &&
                (advertised.Value <= 0 || advertised.Value > _limits.MaximumArchiveBytes ||
                 advertised.Value > int.MaxValue))
                return Reject(ContentSourceFetchError.CapacityExceeded, out error);
            using Stream responseStream = response.Content.ReadAsStream(cancellation.Token);
            using var archive = new MemoryStream(advertised.HasValue ? checked((int)advertised.Value) : 0);
            var buffer = new byte[64 * 1024];
            while (true)
            {
                int read = responseStream.ReadAsync(buffer.AsMemory(), cancellation.Token)
                    .AsTask().GetAwaiter().GetResult();
                if (read == 0) break;
                if (archive.Length + read > _limits.MaximumArchiveBytes)
                    return Reject(ContentSourceFetchError.CapacityExceeded, out error);
                archive.Write(buffer, 0, read);
            }
            if (advertised.HasValue && archive.Length != advertised.Value)
                return Reject(ContentSourceFetchError.SourceFailure, out error);
            byte[] bytes = archive.ToArray();
            _archiveBytesRead = checked(_archiveBytesRead + bytes.LongLength);
            if (!ContentPackageArchive.TryDecodeCanonical(
                    bytes, request.PackageId, request.ManifestHash, out package, _contentLimits))
                return Reject(ContentSourceFetchError.SourceFailure, out error);
            SaturatingIncrement(ref _completed);
            error = ContentSourceFetchError.None;
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
                                           OperationCanceledException or OverflowException)
        {
            return Reject(ContentSourceFetchError.Unavailable, out error);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
    }

    private bool TryAuthorizeUri(ContentSourceDescriptor source, out Uri? uri)
    {
        uri = null;
        if (source.Kind is not (ContentSourceKind.ServerHostedStatic or ContentSourceKind.ExternalHttps) ||
            !Uri.TryCreate(source.Locator, UriKind.Absolute, out Uri? parsed) ||
            !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Fragment))
            return false;
        if (source.Kind == ContentSourceKind.ExternalHttps && parsed.Scheme != Uri.UriSchemeHttps)
            return false;
        if (source.Kind == ContentSourceKind.ServerHostedStatic &&
            parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return false;
        if (parsed.Scheme == Uri.UriSchemeHttp && !_limits.AllowInsecureServerHostedHttp)
            return false;
        if (!_limits.AllowPrivateNetworkTargets && IsObviouslyPrivate(parsed.Host))
            return false;
        uri = parsed;
        return true;
    }

    private static bool IsObviouslyPrivate(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out IPAddress? address)) return false;
        if (IPAddress.IsLoopback(address)) return true;
        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes[0] == 10 || bytes[0] == 127 ||
                bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                bytes[0] == 192 && bytes[1] == 168 ||
                bytes[0] == 169 && bytes[1] == 254;
        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.Equals(IPAddress.IPv6Loopback) ||
            bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC;
    }

    private bool Reject(ContentSourceFetchError value, out ContentSourceFetchError error)
    {
        SaturatingIncrement(ref _rejected);
        error = value;
        return false;
    }

    private static HttpMessageHandler CreateDefaultHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        MaxConnectionsPerServer = 1,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        UseCookies = false,
    };

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }
}
