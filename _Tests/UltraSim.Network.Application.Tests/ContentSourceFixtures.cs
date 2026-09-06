using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

internal sealed class LoopbackHttpsContentHost : IDisposable
{
    private readonly TcpListener _listener;
    private readonly X509Certificate2 _certificate;
    private readonly string _route;
    private readonly byte[] _bytes;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _worker;
    private bool _disposed;

    internal LoopbackHttpsContentHost(string route, ReadOnlyMemory<byte> bytes)
    {
        if (string.IsNullOrEmpty(route) || route[0] != '/' || bytes.IsEmpty)
            throw new ArgumentException("The HTTPS fixture resource is invalid.");
        _route = route;
        _bytes = bytes.ToArray();
        _certificate = CreateCertificate();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUri = new Uri($"https://127.0.0.1:{port}/", UriKind.Absolute);
        ResourceUri = new Uri(BaseUri, route[1..]);
        _worker = Task.Run(RunAsync);
    }

    internal Uri BaseUri { get; }
    internal Uri ResourceUri { get; }

    internal RemoteCertificateValidationCallback CreatePinnedCertificateValidation()
    {
        string expected = _certificate.Thumbprint;
        return (_, certificate, _, _) => certificate is not null &&
            string.Equals(certificate.GetCertHashString(), expected, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopping.Cancel();
        _listener.Stop();
        try { _worker.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        _stopping.Dispose();
        _certificate.Dispose();
    }

    private async Task RunAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient connection;
            try { connection = await _listener.AcceptTcpClientAsync(_stopping.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) when (_stopping.IsCancellationRequested) { break; }
            await HandleAsync(connection).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(TcpClient connection)
    {
        using (connection)
        using (var stream = new SslStream(connection.GetStream(), leaveInnerStreamOpen: false))
        {
            try
            {
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _certificate,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                }, _stopping.Token).ConfigureAwait(false);
                byte[] request = new byte[8 * 1024];
                int length = 0;
                while (length < request.Length)
                {
                    int read = await stream.ReadAsync(
                        request.AsMemory(length, request.Length - length), _stopping.Token).ConfigureAwait(false);
                    if (read == 0) return;
                    length += read;
                    if (HeaderComplete(request.AsSpan(0, length))) break;
                }
                string firstLine = FirstLine(request.AsSpan(0, length));
                bool found = string.Equals(firstLine, $"GET {_route} HTTP/1.1", StringComparison.Ordinal);
                string status = found ? "200 OK" : "404 Not Found";
                int contentLength = found ? _bytes.Length : 0;
                byte[] header = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status}\r\n" +
                    "Content-Type: application/vnd.ultrasim.content-package\r\n" +
                    $"Content-Length: {contentLength}\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(header, _stopping.Token).ConfigureAwait(false);
                if (found) await stream.WriteAsync(_bytes, _stopping.Token).ConfigureAwait(false);
                await stream.FlushAsync(_stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is IOException or AuthenticationException)
            {
            }
        }
    }

    private static bool HeaderComplete(ReadOnlySpan<byte> bytes)
    {
        for (int index = 3; index < bytes.Length; index++)
            if (bytes[index - 3] == '\r' && bytes[index - 2] == '\n' &&
                bytes[index - 1] == '\r' && bytes[index] == '\n')
                return true;
        return false;
    }

    private static string FirstLine(ReadOnlySpan<byte> bytes)
    {
        for (int index = 1; index < bytes.Length; index++)
            if (bytes[index - 1] == '\r' && bytes[index] == '\n')
                return Encoding.ASCII.GetString(bytes[..(index - 1)]);
        return string.Empty;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using RSA key = RSA.Create(2_048);
        var request = new CertificateRequest(
            "CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature, true));
        using X509Certificate2 temporary = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        return new X509Certificate2(temporary.Export(X509ContentType.Pfx));
    }
}
