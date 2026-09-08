using System.Security.Cryptography;

namespace UltraSim.Network;

internal static class HostedUdpDeploymentDiscoveryCodec
{
    private const byte RequestKind = 0xD1;
    private const byte AnnouncementKind = 0xD2;
    private static ReadOnlySpan<byte> SignatureDomain =>
        "UltraSim/deployment-key/v1"u8;
    internal const int NonceBytes = 32;
    internal const int MaximumPublicKeyBytes = 128;
    internal const int RequestBytes = 256;
    internal const int SignatureBytes = 64;
    internal static int MaximumSignedPrefixBytes =>
        SignatureDomain.Length + sizeof(ushort) + sizeof(ulong) + NonceBytes +
        sizeof(ushort) + MaximumPublicKeyBytes;

    internal static void WriteRequest(
        Span<byte> destination,
        NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> nonce)
    {
        if (destination.Length < RequestBytes || nonce.Length != NonceBytes)
            throw new ArgumentException("The deployment discovery request buffer is invalid.");
        Span<byte> request = destination[..RequestBytes];
        request.Clear();
        var writer = new NetworkPacketWriter(request);
        if (!writer.TryWriteByte(RequestKind) ||
            !writer.TryWriteUInt16(manifest.Version) ||
            !writer.TryWriteUInt64(manifest.Fingerprint) ||
            !writer.TryWriteBytes(nonce))
            throw new InvalidOperationException("The deployment discovery request cannot be encoded.");
    }

    internal static bool TryReadRequest(
        ReadOnlySpan<byte> source,
        NetworkProtocolManifest manifest,
        out ReadOnlySpan<byte> nonce)
    {
        nonce = default;
        if (source.Length != RequestBytes) return false;
        var reader = new NetworkPacketReader(source);
        if (!reader.TryReadByte(out byte kind) || kind != RequestKind ||
            !reader.TryReadUInt16(out ushort version) || version != manifest.Version ||
            !reader.TryReadUInt64(out ulong fingerprint) || fingerprint != manifest.Fingerprint ||
            !reader.TryReadBytes(NonceBytes, out nonce) ||
            !reader.TryReadBytes(reader.Remaining, out ReadOnlySpan<byte> padding))
        {
            nonce = default;
            return false;
        }
        for (int index = 0; index < padding.Length; index++)
            if (padding[index] != 0)
            {
                nonce = default;
                return false;
            }
        return true;
    }

    internal static int WriteAnnouncement(
        Span<byte> destination,
        NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey,
        ReadOnlySpan<byte> signature)
    {
        if (nonce.Length != NonceBytes ||
            publicKey.Length is < 64 or > MaximumPublicKeyBytes ||
            signature.Length != SignatureBytes)
            throw new ArgumentException("The deployment announcement identity is invalid.");
        var writer = new NetworkPacketWriter(destination);
        if (!writer.TryWriteByte(AnnouncementKind) ||
            !writer.TryWriteUInt16(manifest.Version) ||
            !writer.TryWriteUInt64(manifest.Fingerprint) ||
            !writer.TryWriteBytes(nonce) ||
            !writer.TryWriteUInt16(checked((ushort)publicKey.Length)) ||
            !writer.TryWriteBytes(publicKey) ||
            !writer.TryWriteBytes(signature))
            throw new InvalidOperationException("The deployment announcement buffer is too small.");
        return writer.WrittenCount;
    }

    internal static bool TryReadAnnouncement(
        ReadOnlySpan<byte> source,
        NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> expectedNonce,
        out ReadOnlySpan<byte> publicKey)
    {
        publicKey = default;
        var reader = new NetworkPacketReader(source);
        if (!reader.TryReadByte(out byte kind) || kind != AnnouncementKind ||
            !reader.TryReadUInt16(out ushort version) || version != manifest.Version ||
            !reader.TryReadUInt64(out ulong fingerprint) || fingerprint != manifest.Fingerprint ||
            !reader.TryReadBytes(NonceBytes, out ReadOnlySpan<byte> nonce) ||
            !nonce.SequenceEqual(expectedNonce) ||
            !reader.TryReadUInt16(out ushort publicLength) ||
            publicLength is < 64 or > MaximumPublicKeyBytes ||
            !reader.TryReadBytes(publicLength, out publicKey) ||
            !reader.TryReadBytes(SignatureBytes, out ReadOnlySpan<byte> signature) ||
            !reader.IsComplete)
        {
            publicKey = default;
            return false;
        }
        try
        {
            using ECDsa candidate = ECDsa.Create();
            candidate.ImportSubjectPublicKeyInfo(publicKey, out int read);
            if (read != publicKey.Length || candidate.KeySize != 256)
                throw new CryptographicException();
            Span<byte> signed = stackalloc byte[
                SignatureDomain.Length + sizeof(ushort) + sizeof(ulong) + NonceBytes +
                sizeof(ushort) + MaximumPublicKeyBytes];
            int signedLength = WriteSignedPrefix(signed, manifest, nonce, publicKey);
            if (!candidate.VerifyData(signed[..signedLength], signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new CryptographicException();
            return true;
        }
        catch (CryptographicException)
        {
            publicKey = default;
            return false;
        }
    }

    internal static int WriteSignedPrefix(
        Span<byte> destination,
        NetworkProtocolManifest manifest,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey)
    {
        var writer = new NetworkPacketWriter(destination);
        if (!writer.TryWriteBytes(SignatureDomain) ||
            !writer.TryWriteUInt16(manifest.Version) ||
            !writer.TryWriteUInt64(manifest.Fingerprint) ||
            !writer.TryWriteBytes(nonce) ||
            !writer.TryWriteUInt16(checked((ushort)publicKey.Length)) ||
            !writer.TryWriteBytes(publicKey))
            throw new InvalidOperationException("The deployment signature transcript is too small.");
        return writer.WrittenCount;
    }
}
