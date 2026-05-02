using System.Security.Cryptography;

namespace UltraSim.Server.Accounting;

public sealed class Pbkdf2PasswordHashService : IPasswordHashService
{
    public const int DefaultIterations = 600_000;
    private const int SaltLength = 32;
    private const int HashLength = 32;
    private const string Prefix = "$us-pbkdf2-sha256$";

    private readonly int _iterations;

    public Pbkdf2PasswordHashService(int iterations = DefaultIterations)
    {
        if (iterations < 100_000)
            throw new ArgumentOutOfRangeException(nameof(iterations), "PBKDF2 iteration count is too low.");

        _iterations = iterations;
    }

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        Span<byte> salt = stackalloc byte[SaltLength];
        RandomNumberGenerator.Fill(salt);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            _iterations,
            HashAlgorithmName.SHA256,
            HashLength);
        return $"{Prefix}v=1$i={_iterations}$s={Base64UrlEncode(salt)}$h={Base64UrlEncode(hash)}";
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) || !TryParse(passwordHash, out int iterations, out byte[] salt, out byte[] expected))
            return false;

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string passwordHash)
    {
        return !TryParse(passwordHash, out int iterations, out _, out _) || iterations < _iterations;
    }

    private static bool TryParse(string passwordHash, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];
        if (string.IsNullOrWhiteSpace(passwordHash) || !passwordHash.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        string[] parts = passwordHash[Prefix.Length..].Split('$');
        if (parts.Length != 4 ||
            !string.Equals(parts[0], "v=1", StringComparison.Ordinal) ||
            !parts[1].StartsWith("i=", StringComparison.Ordinal) ||
            !parts[2].StartsWith("s=", StringComparison.Ordinal) ||
            !parts[3].StartsWith("h=", StringComparison.Ordinal) ||
            !int.TryParse(parts[1][2..], out iterations) ||
            iterations < 100_000)
        {
            return false;
        }

        return TryBase64UrlDecode(parts[2][2..], out salt) &&
            TryBase64UrlDecode(parts[3][2..], out hash) &&
            salt.Length >= 16 &&
            hash.Length >= 16;
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryBase64UrlDecode(string value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string padded = value.Replace('-', '+').Replace('_', '/');
        int padding = padded.Length % 4;
        if (padding != 0)
            padded = padded.PadRight(padded.Length + (4 - padding), '=');

        try
        {
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
