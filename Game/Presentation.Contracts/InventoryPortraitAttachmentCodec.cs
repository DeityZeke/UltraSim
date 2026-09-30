using System.Buffers.Binary;
using System.Text;
using UltraSim.Framework.Definitions;

namespace UltraSim.Game.Presentation.Contracts;

/// <summary>
/// Bounded typed Game/client boundary for one Inventory row's public portrait identity. UI only
/// carries an opaque attachment; it never parses Definition IDs from display/model values.
/// </summary>
public static class InventoryPortraitAttachmentCodec
{
    public const ushort AttachmentKind = 1;
    private const byte Version = 1;
    private const int MaximumIdentityBytes = 96;
    private const int MaximumPayloadBytes = 1 + 3 * (1 + MaximumIdentityBytes) + 12;

    public static byte[] Encode(SemanticPresentationReference reference, PublicFallbackBounds bounds)
    {
        if (!reference.IsValid || reference.Role != PresentationRoleId.InventoryPortrait || !bounds.IsValid)
            throw new ArgumentException("An admitted Inventory portrait and public fallback are required.");
        byte[] domain = Encoding.ASCII.GetBytes(reference.Domain.Value);
        byte[] definition = Encoding.ASCII.GetBytes(reference.Definition.Value);
        byte[] role = Encoding.ASCII.GetBytes(reference.Role.Value);
        var bytes = new byte[1 + 3 + domain.Length + definition.Length + role.Length + 12];
        int offset = 0;
        bytes[offset++] = Version;
        Write(domain); Write(definition); Write(role);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), bounds.WidthMillimeters); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), bounds.HeightMillimeters); offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), bounds.DepthMillimeters);
        return bytes;

        void Write(byte[] token)
        {
            if (token.Length is <= 0 or > MaximumIdentityBytes)
                throw new ArgumentException("The portrait identity exceeds its bounded representation.");
            bytes[offset++] = checked((byte)token.Length);
            token.CopyTo(bytes, offset); offset += token.Length;
        }
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload,
        out SemanticPresentationReference reference, out PublicFallbackBounds bounds)
    {
        reference = default; bounds = default;
        if (payload.Length is < 16 or > MaximumPayloadBytes || payload[0] != Version) return false;
        int offset = 1;
        if (!TryReadToken(payload, ref offset, out string domain) ||
            !TryReadToken(payload, ref offset, out string definition) ||
            !TryReadToken(payload, ref offset, out string role) ||
            payload.Length - offset != 12) return false;
        var decodedBounds = new PublicFallbackBounds(
            BinaryPrimitives.ReadInt32BigEndian(payload[offset..]),
            BinaryPrimitives.ReadInt32BigEndian(payload[(offset + 4)..]),
            BinaryPrimitives.ReadInt32BigEndian(payload[(offset + 8)..]));
        if (!decodedBounds.IsValid) return false;
        try
        {
            var decoded = new SemanticPresentationReference(new(domain), new(definition), new(role));
            if (decoded.Role != PresentationRoleId.InventoryPortrait) return false;
            reference = decoded; bounds = decodedBounds; return true;
        }
        catch (ArgumentException) { return false; }

    }

    private static bool TryReadToken(ReadOnlySpan<byte> payload, ref int offset, out string token)
    {
        token = string.Empty;
        if (offset >= payload.Length) return false;
        int count = payload[offset++];
        if (count is <= 0 or > MaximumIdentityBytes || count > payload.Length - offset) return false;
        ReadOnlySpan<byte> source = payload.Slice(offset, count);
        foreach (byte value in source) if (value > 0x7f) return false;
        token = Encoding.ASCII.GetString(source);
        offset += count;
        return true;
    }
}
