using System.Security.Cryptography;
using System.Text;

namespace UltraSim.Registry;

/// <summary>
/// Shared constant-time token comparison used for all registry secret comparisons.
/// </summary>
internal static class RegistryAuth
{
    /// <summary>
    /// Returns true if <paramref name="a"/> and <paramref name="b"/> are equal,
    /// using a constant-time comparison that does not leak length via timing.
    /// Both inputs are SHA-256 hashed before comparison so the byte arrays are
    /// always 32 bytes regardless of input length.
    /// </summary>
    public static bool ConstantTimeEquals(string a, string b)
    {
        byte[] aHash = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        byte[] bHash = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(aHash, bHash);
    }
}
