using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace UltraSim.Client;

public readonly record struct ClientDirectConnectRequest(string Host, int Port)
{
    public const int MinPort = IPEndPoint.MinPort;
    public const int MaxPort = IPEndPoint.MaxPort;

    public static bool TryCreate(string? host, string? portText, out ClientDirectConnectRequest request, out string error)
    {
        string normalizedHost = (host ?? string.Empty).Trim();
        if (normalizedHost.Length == 0)
        {
            request = default;
            error = "Host is required.";
            return false;
        }

        if (!int.TryParse((portText ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port) ||
            port < MinPort ||
            port > MaxPort)
        {
            request = default;
            error = $"Port must be between {MinPort} and {MaxPort}.";
            return false;
        }

        request = new ClientDirectConnectRequest(normalizedHost, port);
        error = string.Empty;
        return true;
    }

    public bool TryResolve(out IPEndPoint endPoint, out string error)
    {
        if (IPAddress.TryParse(Host, out IPAddress? address))
        {
            endPoint = new IPEndPoint(address, Port);
            error = string.Empty;
            return true;
        }

        IPHostEntry entry;
        try
        {
            entry = Dns.GetHostEntry(Host);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            endPoint = default!;
            error = $"Could not resolve host '{Host}'.";
            return false;
        }

        foreach (IPAddress candidate in entry.AddressList)
        {
            if (candidate.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                endPoint = new IPEndPoint(candidate, Port);
                error = string.Empty;
                return true;
            }
        }

        endPoint = default!;
        error = $"Host '{Host}' has no IPv4 address.";
        return false;
    }
}
