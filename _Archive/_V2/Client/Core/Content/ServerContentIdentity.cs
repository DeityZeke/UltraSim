using System.Security.Cryptography;
using System.Text;

namespace UltraSim.Client.Content;

public readonly record struct ServerContentIdentity(string ServerId, string CacheNamespace)
{
    public const int MaxServerIdLength = 256;

    public static bool TryCreate(string? serverId, out ServerContentIdentity identity, out string error)
    {
        string normalizedServerId = (serverId ?? string.Empty).Trim();
        if (normalizedServerId.Length == 0)
        {
            identity = default;
            error = "Server identity is required.";
            return false;
        }

        if (normalizedServerId.Length > MaxServerIdLength)
        {
            identity = default;
            error = $"Server identity cannot exceed {MaxServerIdLength} characters.";
            return false;
        }

        if (normalizedServerId.Any(char.IsControl))
        {
            identity = default;
            error = "Server identity cannot contain control characters.";
            return false;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedServerId));
        string cacheNamespace = "server-" + Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
        identity = new ServerContentIdentity(normalizedServerId, cacheNamespace);
        error = string.Empty;
        return true;
    }
}
