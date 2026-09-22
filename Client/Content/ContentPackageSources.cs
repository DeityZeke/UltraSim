using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;

namespace UltraSim.Client.Content;

public readonly record struct ContentPackageSourceLimits
{
    public ContentPackageSourceLimits(
        long maximumArchiveBytes,
        int requestTimeoutMilliseconds,
        bool allowInsecureServerHostedHttp,
        bool allowPrivateNetworkTargets)
        : this(maximumArchiveBytes, requestTimeoutMilliseconds, allowInsecureServerHostedHttp,
            allowPrivateNetworkTargets, maximumConcurrentRequests: 4)
    {
    }

    public ContentPackageSourceLimits(
        long maximumArchiveBytes,
        int requestTimeoutMilliseconds,
        bool allowInsecureServerHostedHttp,
        bool allowPrivateNetworkTargets,
        int maximumConcurrentRequests)
    {
        if (maximumArchiveBytes is < 1 or > int.MaxValue ||
            requestTimeoutMilliseconds is < 1 or > 300_000 || maximumConcurrentRequests is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(maximumArchiveBytes));
        MaximumArchiveBytes = maximumArchiveBytes;
        RequestTimeoutMilliseconds = requestTimeoutMilliseconds;
        AllowInsecureServerHostedHttp = allowInsecureServerHostedHttp;
        AllowPrivateNetworkTargets = allowPrivateNetworkTargets;
        MaximumConcurrentRequests = maximumConcurrentRequests;
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
    public int MaximumConcurrentRequests { get; }

    internal static ContentPackageSourceLimits Resolve(ContentPackageSourceLimits limits) =>
        limits.MaximumArchiveBytes == 0 ? Default : limits;
}

public readonly record struct ContentPackageSourceSnapshot(
    ulong Requests,
    ulong Completed,
    ulong Rejected,
    long ArchiveBytesRead,
    long ManifestBytesRead,
    long PayloadBytesRead,
    int PeakConcurrentRequests);

/// <summary>Injectable name resolution used by the connection-time private-target policy.</summary>
public interface IContentHostResolver
{
    ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

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
        new(_requests, _completed, _rejected, _archiveBytesRead, 0, 0, 1);

    public bool TryFetch(
        in ContentPackageFetchRequest request,
        out FetchedContentPackage? package,
        out ContentSourceFetchError error) =>
        TryFetch(in request, CancellationToken.None, out package, out error);

    public bool TryFetch(
        in ContentPackageFetchRequest request,
        CancellationToken cancellationToken,
        out FetchedContentPackage? package,
        out ContentSourceFetchError error)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            byte[] bytes = File.ReadAllBytesAsync(path, cancellationToken)
                .GetAwaiter().GetResult();
            _archiveBytesRead = checked(_archiveBytesRead + bytes.LongLength);
            if (!ContentPackageArchive.TryDecodeCanonical(
                    bytes, request.PackageId, request.ManifestHash, out package, _contentLimits))
                return Reject(ContentSourceFetchError.SourceFailure, out error);
            SaturatingIncrement(ref _completed);
            error = ContentSourceFetchError.None;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           ArgumentException or NotSupportedException or OverflowException)
        {
            return Reject(ContentSourceFetchError.Unavailable, out error);
        }
    }

    /// <summary>
    /// Reads one explicitly selected local user-pack archive through the same rooted/reparse-safe
    /// source boundary, but applies the dedicated resource-pack manifest policy.
    /// </summary>
    public bool TryFetchUserResourcePack(
        in UserResourcePackFetchRequest request,
        out FetchedContentPackage? package,
        out ContentSourceFetchError error)
    {
        SaturatingIncrement(ref _requests);
        package = null;
        if (!request.IsValid)
            return Reject(ContentSourceFetchError.InvalidRequest, out error);
        try
        {
            string relative = request.Locator.Value!.Replace('/', Path.DirectorySeparatorChar);
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
            if (!ContentPackageArchive.TryDecodeUserResourcePackCanonical(
                    bytes, request.Identity.PackageId, request.Identity.ManifestHash,
                    out package, _contentLimits))
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
/// Bounded HTTP(S) package source. URLs are authenticated authorization hints only;
/// the canonical envelope, manifest, payload hashes, and passive validators decide acceptance.
/// </summary>
public sealed class HttpContentPackageSource : IContentPackageFileSource, IDisposable
{
    private readonly HttpClient _client;
    private readonly ContentPackageSourceLimits _limits;
    private readonly ContentLimits _contentLimits;
    private long _requests;
    private long _completed;
    private long _rejected;
    private long _archiveBytesRead;
    private long _manifestBytesRead;
    private long _payloadBytesRead;
    private int _activeRequests;
    private int _peakConcurrentRequests;
    private bool _disposed;

    public HttpContentPackageSource(
        ContentPackageSourceLimits limits = default,
        ContentLimits contentLimits = default)
    {
        _limits = ContentPackageSourceLimits.Resolve(limits);
        _contentLimits = ContentLimits.Resolve(contentLimits);
        _client = new HttpClient(CreateDefaultHandler(
            _limits, SystemContentHostResolver.Instance, certificateValidation: null), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public HttpContentPackageSource(
        RemoteCertificateValidationCallback certificateValidation,
        ContentPackageSourceLimits limits = default,
        ContentLimits contentLimits = default)
        : this(SystemContentHostResolver.Instance, certificateValidation, limits, contentLimits)
    {
        ArgumentNullException.ThrowIfNull(certificateValidation);
    }

    public HttpContentPackageSource(
        IContentHostResolver resolver,
        RemoteCertificateValidationCallback? certificateValidation,
        ContentPackageSourceLimits limits = default,
        ContentLimits contentLimits = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _limits = ContentPackageSourceLimits.Resolve(limits);
        _contentLimits = ContentLimits.Resolve(contentLimits);
        _client = new HttpClient(CreateDefaultHandler(_limits, resolver, certificateValidation), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public bool CanFetch(ContentSourceKind kind) =>
        kind is ContentSourceKind.ServerHostedStatic or ContentSourceKind.ExternalHttps;

    public ContentPackageSourceSnapshot GetSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ContentPackageSourceSnapshot(
            ToUnsigned(Interlocked.Read(ref _requests)),
            ToUnsigned(Interlocked.Read(ref _completed)),
            ToUnsigned(Interlocked.Read(ref _rejected)),
            Interlocked.Read(ref _archiveBytesRead),
            Interlocked.Read(ref _manifestBytesRead),
            Interlocked.Read(ref _payloadBytesRead),
            Volatile.Read(ref _peakConcurrentRequests));
    }

    public bool TryFetch(
        in ContentPackageFetchRequest request,
        out FetchedContentPackage? package,
        out ContentSourceFetchError error) =>
        TryFetch(in request, CancellationToken.None, out package, out error);

    public bool TryFetch(
        in ContentPackageFetchRequest request,
        CancellationToken cancellationToken,
        out FetchedContentPackage? package,
        out ContentSourceFetchError error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _requests);
        package = null;
        if (!request.IsValid || !TryAuthorizeUri(request.Source, out Uri? uri))
            return Reject(ContentSourceFetchError.InvalidRequest, out error);

        try
        {
            if (!TryRead(uri!, _limits.MaximumArchiveBytes,
                    "application/vnd.ultrasim.content-package", cancellationToken,
                    out byte[] bytes, out error))
                return false;
            Interlocked.Add(ref _archiveBytesRead, bytes.LongLength);
            if (!ContentPackageArchive.TryDecodeCanonical(
                    bytes, request.PackageId, request.ManifestHash, out package, _contentLimits))
                return Reject(ContentSourceFetchError.SourceFailure, out error);
            Interlocked.Increment(ref _completed);
            error = ContentSourceFetchError.None;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
                                           OperationCanceledException or OverflowException)
        {
            return Reject(ContentSourceFetchError.Unavailable, out error);
        }
    }

    public bool CanFetchFiles(in ContentSourceDescriptor source) =>
        CanFetch(source.Kind) && Uri.TryCreate(source.Locator, UriKind.Absolute, out Uri? uri) &&
        uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal) && string.IsNullOrEmpty(uri.Query);

    public bool TryFetchManifest(
        in ContentPackageManifestFetchRequest request,
        out ContentPackageManifest? manifest,
        out ContentSourceFetchError error) =>
        TryFetchManifest(in request, CancellationToken.None, out manifest, out error);

    public bool TryFetchManifest(
        in ContentPackageManifestFetchRequest request,
        CancellationToken cancellationToken,
        out ContentPackageManifest? manifest,
        out ContentSourceFetchError error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _requests);
        manifest = null;
        if (!request.IsValid || !TryAuthorizeRoot(request.Source, out Uri? root))
            return Reject(ContentSourceFetchError.InvalidRequest, out error);
        Uri uri = new(root!, "manifest.usmanifest");
        if (!TryRead(uri, _limits.MaximumArchiveBytes,
                "application/vnd.ultrasim.content-manifest", cancellationToken,
                out byte[] bytes, out error))
            return false;
        Interlocked.Add(ref _manifestBytesRead, bytes.LongLength);
        if (!ContentPackageManifestEnvelope.TryDecodeCanonical(
                bytes, request.PackageId, request.ManifestHash, out manifest, _contentLimits))
            return Reject(ContentSourceFetchError.CandidateMismatch, out error);
        Interlocked.Increment(ref _completed);
        error = ContentSourceFetchError.None;
        return true;
    }

    public bool TryFetchFile(
        in ContentFileFetchRequest request,
        out ContentFilePayload payload,
        out ContentSourceFetchError error) =>
        TryFetchFile(in request, CancellationToken.None, out payload, out error);

    public bool TryFetchFile(
        in ContentFileFetchRequest request,
        CancellationToken cancellationToken,
        out ContentFilePayload payload,
        out ContentSourceFetchError error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _requests);
        payload = default;
        if (!request.IsValid || !TryAuthorizeRoot(request.Source, out Uri? root))
            return Reject(ContentSourceFetchError.InvalidRequest, out error);
        Uri uri = new(root!, $"blobs/{request.File.Sha256}.blob");
        if (!TryRead(uri, request.File.SizeBytes, "application/octet-stream",
                cancellationToken, out byte[] bytes, out error))
            return false;
        Interlocked.Add(ref _payloadBytesRead, bytes.LongLength);
        if (!ContentPackageValidator.TryValidatePayload(request.File, bytes))
            return Reject(ContentSourceFetchError.CandidateMismatch, out error);
        payload = new ContentFilePayload(request.File.Path, bytes);
        Interlocked.Increment(ref _completed);
        error = ContentSourceFetchError.None;
        return true;
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

    private bool TryAuthorizeRoot(ContentSourceDescriptor source, out Uri? root)
    {
        root = null;
        if (!TryAuthorizeUri(source, out Uri? parsed) ||
            !parsed!.AbsolutePath.EndsWith("/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(parsed.Query))
            return false;
        root = parsed;
        return true;
    }

    private static bool IsObviouslyPrivate(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out IPAddress? address)) return false;
        return !IsPublicAddress(address);
    }

    private bool TryRead(
        Uri uri,
        long maximumBytes,
        string accept,
        CancellationToken cancellationToken,
        out byte[] bytes,
        out ContentSourceFetchError error)
    {
        bytes = [];
        int active = Interlocked.Increment(ref _activeRequests);
        UpdateMaximum(ref _peakConcurrentRequests, active);
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellation.CancelAfter(_limits.RequestTimeoutMilliseconds);
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            using HttpResponseMessage response = _client.Send(
                message, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri != uri)
                return Reject(ContentSourceFetchError.Unavailable, out error);
            long? advertised = response.Content.Headers.ContentLength;
            if (advertised.HasValue &&
                (advertised.Value <= 0 || advertised.Value > maximumBytes || advertised.Value > int.MaxValue))
                return Reject(ContentSourceFetchError.CapacityExceeded, out error);
            using Stream responseStream = response.Content.ReadAsStream(cancellation.Token);
            using var destination = new MemoryStream(advertised.HasValue ? checked((int)advertised.Value) : 0);
            var buffer = new byte[64 * 1024];
            while (true)
            {
                int read = responseStream.ReadAsync(buffer.AsMemory(), cancellation.Token)
                    .AsTask().GetAwaiter().GetResult();
                if (read == 0) break;
                if (destination.Length + read > maximumBytes)
                    return Reject(ContentSourceFetchError.CapacityExceeded, out error);
                destination.Write(buffer, 0, read);
            }
            if (advertised.HasValue && destination.Length != advertised.Value)
                return Reject(ContentSourceFetchError.SourceFailure, out error);
            bytes = destination.ToArray();
            error = ContentSourceFetchError.None;
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
                                           OperationCanceledException or OverflowException or SocketException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Reject(ContentSourceFetchError.Unavailable, out error);
        }
        finally
        {
            Interlocked.Decrement(ref _activeRequests);
        }
    }

    private bool Reject(ContentSourceFetchError value, out ContentSourceFetchError error)
    {
        Interlocked.Increment(ref _rejected);
        error = value;
        return false;
    }

    private static HttpMessageHandler CreateDefaultHandler(
        ContentPackageSourceLimits limits,
        IContentHostResolver resolver,
        RemoteCertificateValidationCallback? certificateValidation)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            MaxConnectionsPerServer = limits.MaximumConcurrentRequests,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromMilliseconds(Math.Min(10_000, limits.RequestTimeoutMilliseconds)),
            UseCookies = false,
        };
        if (certificateValidation is not null)
            handler.SslOptions.RemoteCertificateValidationCallback = certificateValidation;
        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            IReadOnlyList<IPAddress> addresses;
            if (IPAddress.TryParse(context.DnsEndPoint.Host, out IPAddress? literal))
                addresses = [literal];
            else
                addresses = await resolver.ResolveAsync(
                    context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
            if (addresses.Count == 0 ||
                !limits.AllowPrivateNetworkTargets && addresses.Any(static value => !IsPublicAddress(value)))
                throw new HttpRequestException("The content host resolved to a prohibited network target.");

            Exception? last = null;
            foreach (IPAddress address in addresses.OrderBy(static value => value.ToString(), StringComparer.Ordinal))
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(
                        new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception exception) when (exception is SocketException or OperationCanceledException)
                {
                    last = exception;
                    socket.Dispose();
                }
            }
            throw new HttpRequestException("No admitted content endpoint was reachable.", last);
        };
        return handler;
    }

    private static bool IsPublicAddress(IPAddress source)
    {
        IPAddress address = source.IsIPv4MappedToIPv6 ? source.MapToIPv4() : source;
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal ||
            address.IsIPv6Multicast)
            return false;
        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return bytes[0] != 0 && bytes[0] < 224 &&
                bytes[0] != 10 && bytes[0] != 127 &&
                !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127) &&
                !(bytes[0] == 169 && bytes[1] == 254) &&
                !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) &&
                !(bytes[0] == 192 && bytes[1] == 168);
        return bytes.Length == 16 && (bytes[0] & 0xFE) != 0xFC;
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        while (true)
        {
            int current = Volatile.Read(ref target);
            if (current >= value || Interlocked.CompareExchange(ref target, value, current) == current) return;
        }
    }

    private static ulong ToUnsigned(long value) => value <= 0 ? 0UL : checked((ulong)value);

    private sealed class SystemContentHostResolver : IContentHostResolver
    {
        internal static SystemContentHostResolver Instance { get; } = new();

        public async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken) =>
            await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
    }
}
