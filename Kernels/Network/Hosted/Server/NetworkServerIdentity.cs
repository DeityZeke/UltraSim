using System.Security.Cryptography;

namespace UltraSim.Network;

/// <summary>Long-lived server signing identity pinned by a ProtocolLoopback client.</summary>
public sealed class NetworkServerIdentity : IDisposable
{
    private readonly ECDsa _signingKey;
    private readonly byte[] _publicKey;
    private bool _disposed;

    private NetworkServerIdentity(ECDsa signingKey)
    {
        _signingKey = signingKey;
        _publicKey = signingKey.ExportSubjectPublicKeyInfo();
    }

    public static NetworkServerIdentity CreateEphemeral() =>
        new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>Restores one deployment-owned P-256 signing identity.</summary>
    public static bool TryCreateFromPkcs8(
        ReadOnlySpan<byte> privateKey,
        out NetworkServerIdentity? identity)
    {
        identity = null;
        ECDsa? signingKey = null;
        try
        {
            signingKey = ECDsa.Create();
            signingKey.ImportPkcs8PrivateKey(privateKey, out int read);
            if (read != privateKey.Length || signingKey.KeySize != 256)
                throw new CryptographicException(
                    "The deployment identity must be one canonical P-256 PKCS#8 key.");
            identity = new NetworkServerIdentity(signingKey);
            signingKey = null;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            signingKey?.Dispose();
        }
    }

    public ReadOnlySpan<byte> PublicKey => _disposed
        ? throw new ObjectDisposedException(nameof(NetworkServerIdentity))
        : _publicKey;

    /// <summary>Exports deployment-owned private material for explicit secure persistence.</summary>
    public byte[] ExportPkcs8PrivateKey()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NetworkServerIdentity));
        return _signingKey.ExportPkcs8PrivateKey();
    }

    internal bool TrySign(ReadOnlySpan<byte> transcript, Span<byte> signature, out int written)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NetworkServerIdentity));
        return _signingKey.TrySignData(transcript, signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation, out written);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _signingKey.Dispose();
        CryptographicOperations.ZeroMemory(_publicKey);
        _disposed = true;
    }
}
