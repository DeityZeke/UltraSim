using System.Security.Cryptography;

namespace UltraSim.Client.Content;

public static class ContentPathValidation
{
    public const int Sha256HexLength = 64;

    public static bool TryNormalizeRelativePath(string? relativePath, out string normalizedPath, out string error)
    {
        normalizedPath = string.Empty;
        string value = (relativePath ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            error = "Content path is required.";
            return false;
        }

        if (Path.IsPathRooted(value) || value.Contains(':', StringComparison.Ordinal))
        {
            error = "Content path must be relative.";
            return false;
        }

        string[] parts = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "Content path is required.";
            return false;
        }

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part is "." or "..")
            {
                error = "Content path cannot contain traversal segments.";
                return false;
            }

            if (part.Any(char.IsControl))
            {
                error = "Content path cannot contain control characters.";
                return false;
            }
        }

        normalizedPath = string.Join('/', parts);
        error = string.Empty;
        return true;
    }

    public static bool TryNormalizeSha256Hex(string? hashHex, out string normalizedHashHex, out string error)
    {
        normalizedHashHex = string.Empty;
        string value = (hashHex ?? string.Empty).Trim();
        if (value.Length != Sha256HexLength)
        {
            error = "SHA-256 hash must be 64 hexadecimal characters.";
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
            {
                error = "SHA-256 hash must be hexadecimal.";
                return false;
            }
        }

        normalizedHashHex = value.ToLowerInvariant();
        error = string.Empty;
        return true;
    }

    public static bool ValidateSizeAndSha256(ReadOnlySpan<byte> bytes, long expectedSizeBytes, string expectedSha256Hex, out string error)
    {
        if (expectedSizeBytes < 0)
        {
            error = "Expected asset size cannot be negative.";
            return false;
        }

        if (bytes.Length != expectedSizeBytes)
        {
            error = $"Asset size mismatch. Expected {expectedSizeBytes} bytes, got {bytes.Length}.";
            return false;
        }

        if (!TryNormalizeSha256Hex(expectedSha256Hex, out string normalizedHash, out error))
        {
            return false;
        }

        string actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actualHash, normalizedHash, StringComparison.Ordinal))
        {
            error = "Asset SHA-256 hash mismatch.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool ValidateSha256(ReadOnlySpan<byte> bytes, string expectedSha256Hex, out string error)
    {
        if (!TryNormalizeSha256Hex(expectedSha256Hex, out string normalizedHash, out error))
        {
            return false;
        }

        string actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actualHash, normalizedHash, StringComparison.Ordinal))
        {
            error = "Asset SHA-256 hash mismatch.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
