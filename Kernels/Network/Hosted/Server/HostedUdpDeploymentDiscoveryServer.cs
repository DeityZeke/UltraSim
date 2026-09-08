using System.Security.Cryptography;

namespace UltraSim.Network;

internal static class HostedUdpDeploymentDiscoveryServer
{
    internal static int WriteAnnouncement(
        Span<byte> destination,
        NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> nonce,
        NetworkServerIdentity identity)
    {
        ReadOnlySpan<byte> publicKey = identity.PublicKey;
        if (nonce.Length != HostedUdpDeploymentDiscoveryCodec.NonceBytes ||
            publicKey.Length is < 64 or > HostedUdpDeploymentDiscoveryCodec.MaximumPublicKeyBytes)
            throw new ArgumentException("The deployment announcement identity is invalid.");
        Span<byte> signed = stackalloc byte[
            HostedUdpDeploymentDiscoveryCodec.MaximumSignedPrefixBytes];
        int signedLength = HostedUdpDeploymentDiscoveryCodec.WriteSignedPrefix(
            signed, manifest, nonce, publicKey);
        Span<byte> signature = stackalloc byte[
            HostedUdpDeploymentDiscoveryCodec.SignatureBytes];
        if (!identity.TrySign(signed[..signedLength], signature, out int signatureLength) ||
            signatureLength != HostedUdpDeploymentDiscoveryCodec.SignatureBytes)
            throw new CryptographicException("The deployment candidate could not be signed.");
        return HostedUdpDeploymentDiscoveryCodec.WriteAnnouncement(
            destination, manifest, nonce, publicKey, signature);
    }
}
