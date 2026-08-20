using System.Net;
using System.Net.Sockets;

namespace UltraSim.Registry;

/// <summary>
/// Shared SSRF protection logic used at registration (URL validation) and at poll time
/// (DelegatingHandler DNS resolution check).
/// </summary>
internal static class SsrfGuard
{
    /// <summary>
    /// Returns true if the host string (IP literal or hostname) should be blocked.
    /// For IP literals the decision is immediate. For hostnames, only the literal
    /// string "localhost" is caught here — resolved addresses must be checked separately
    /// via <see cref="IsBlockedAddress"/> at poll time.
    /// </summary>
    public static bool IsBlockedHost(string host, bool allowPrivateTargets = false)
    {
        if (allowPrivateTargets)
            return false;

        if (StringComparer.OrdinalIgnoreCase.Equals(host, "localhost"))
            return true;

        // If it's an IP literal, evaluate immediately.
        if (IPAddress.TryParse(host, out IPAddress? address))
            return IsBlockedAddress(address, allowPrivateTargets);

        return false;
    }

    /// <summary>
    /// Returns true if the resolved IP address is in a range that should never be
    /// reachable from the registry's polling infrastructure.
    /// Covers: loopback, RFC 1918 private, link-local (169.254/16, fe80::/10),
    /// unique-local IPv6 (fc00::/7), and unspecified (0.0.0.0/8).
    /// </summary>
    public static bool IsBlockedAddress(IPAddress address, bool allowPrivateTargets = false)
    {
        if (allowPrivateTargets)
            return false;

        if (IPAddress.IsLoopback(address))
            return true;

        byte[] bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10)
                return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;
            // 169.254.0.0/16 (link-local)
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;
            // 0.0.0.0/8 (unspecified)
            if (bytes[0] == 0)
                return true;
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fe80::/10 (link-local)
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                return true;
            // fc00::/7 (unique-local)
            if ((bytes[0] & 0xfe) == 0xfc)
                return true;
        }

        return false;
    }
}
