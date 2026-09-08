using System.Text;
using UltraSim.Network;

namespace UltraSim.Game.Protocol;

public enum RemotePublicPresentationOperation : byte
{
    Present = 1,
    Remove = 2,
}

[Flags]
public enum RemotePublicPresentationFlags : byte
{
    None = 0,
    NameplateEligible = 1 << 0,
    ItemProperties = 1 << 1,
}

public enum RemotePublicPresentationApplyStatus : byte
{
    Applied = 1,
    Rejected = 2,
    CapacityExceeded = 3,
}

/// <summary>
/// One retained cold public-presentation metadata occurrence. Bounded display/detail text follows
/// only for a Present operation; moving public state remains in the independent chunk protocol.
/// </summary>
[NetworkMessage(144, "ultrasim.game.public-presentation-metadata", 2, NetworkLane.Control)]
public partial struct RemotePublicPresentationMetadataHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong ServerGenerationId;
    public ulong DeliveryId;
    public ulong MetadataRevision;
    public uint PublicEntityIndex;
    public uint PublicEntityGeneration;
    public RemotePublicPresentationOperation Operation;
    public RemotePublicPresentationFlags Flags;
}

/// <summary>Exact client application result for one cold metadata delivery.</summary>
[NetworkMessage(145, "ultrasim.game.public-presentation-applied", 1, NetworkLane.Control)]
public partial struct RemotePublicPresentationAppliedMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong ServerGenerationId;
    public ulong DeliveryId;
    public RemotePublicPresentationApplyStatus Status;
}

public static partial class UltraSimGameProtocol
{
    public const int MaximumPublicPresentationDisplayLabelUtf8Bytes = 128;
    public const int MaximumPublicPresentationDetailUtf8Bytes = 512;
    public const int MaximumPublicPresentationMessageBytes = 768;
    public const RemotePublicPresentationFlags AdmittedPublicPresentationFlags =
        RemotePublicPresentationFlags.NameplateEligible |
        RemotePublicPresentationFlags.ItemProperties;
    private static readonly UTF8Encoding StrictPublicPresentationUtf8 = new(false, true);

    public static bool TryWritePublicPresentationMetadata(
        Span<byte> destination,
        in RemotePublicPresentationMetadataHeader header,
        string displayLabel,
        string detailText,
        out int written)
    {
        written = 0;
        if (!IsValidPublicPresentationHeader(in header) || displayLabel is null ||
            detailText is null)
            return false;
        var writer = new NetworkPacketWriter(destination);
        if (!RemotePublicPresentationMetadataHeaderCodec.TryWrite(ref writer, in header))
            return false;
        switch (header.Operation)
        {
            case RemotePublicPresentationOperation.Present:
                if (!IsCanonicalPublicPresentationLabel(displayLabel) ||
                    !writer.TryWriteBoundedUtf8(
                        displayLabel, MaximumPublicPresentationDisplayLabelUtf8Bytes) ||
                    ((header.Flags & RemotePublicPresentationFlags.ItemProperties) != 0
                        ? !IsCanonicalPublicPresentationText(
                                detailText, MaximumPublicPresentationDetailUtf8Bytes) ||
                          !writer.TryWriteBoundedUtf8(
                                detailText, MaximumPublicPresentationDetailUtf8Bytes)
                        : detailText.Length != 0 ||
                          !writer.TryWriteBoundedUtf8(
                                string.Empty, MaximumPublicPresentationDetailUtf8Bytes)))
                    return false;
                break;
            case RemotePublicPresentationOperation.Remove:
                if (displayLabel.Length != 0 || detailText.Length != 0) return false;
                break;
            default:
                return false;
        }
        written = writer.WrittenCount;
        return written <= MaximumPublicPresentationMessageBytes;
    }

    public static bool TryReadPublicPresentationMetadata(
        ReadOnlySpan<byte> source,
        out RemotePublicPresentationMetadataHeader header,
        out string displayLabel,
        out string detailText)
    {
        header = default;
        displayLabel = string.Empty;
        detailText = string.Empty;
        if (source.Length > MaximumPublicPresentationMessageBytes) return false;
        var reader = new NetworkPacketReader(source);
        if (!RemotePublicPresentationMetadataHeaderCodec.TryRead(
                ref reader, out RemotePublicPresentationMetadataHeader candidate) ||
            !IsValidPublicPresentationHeader(in candidate))
            return false;
        if (candidate.Operation == RemotePublicPresentationOperation.Present)
        {
            if (!reader.TryReadBoundedUtf8Bytes(
                    MaximumPublicPresentationDisplayLabelUtf8Bytes,
                    out ReadOnlySpan<byte> label) || label.IsEmpty ||
                !reader.TryReadBoundedUtf8Bytes(
                    MaximumPublicPresentationDetailUtf8Bytes,
                    out ReadOnlySpan<byte> details) || !reader.IsComplete)
                return false;
            displayLabel = Encoding.UTF8.GetString(label);
            detailText = Encoding.UTF8.GetString(details);
            if (!IsCanonicalPublicPresentationLabel(displayLabel) ||
                ((candidate.Flags & RemotePublicPresentationFlags.ItemProperties) != 0
                    ? !IsCanonicalPublicPresentationText(
                        detailText, MaximumPublicPresentationDetailUtf8Bytes)
                    : detailText.Length != 0))
                return false;
        }
        else if (!reader.IsComplete)
            return false;
        header = candidate;
        return true;
    }

    public static bool IsValidPublicPresentationApplied(
        in RemotePublicPresentationAppliedMessage value) =>
        value.ApplicationSessionEpoch != 0 && value.ServerGenerationId != 0 &&
        value.DeliveryId != 0 &&
        value.Status is >= RemotePublicPresentationApplyStatus.Applied and
            <= RemotePublicPresentationApplyStatus.CapacityExceeded;

    private static bool IsValidPublicPresentationHeader(
        in RemotePublicPresentationMetadataHeader value) =>
        value.ApplicationSessionEpoch != 0 && value.ServerGenerationId != 0 &&
        value.DeliveryId != 0 && value.MetadataRevision != 0 &&
        value.PublicEntityGeneration != 0 &&
        (value.Operation is RemotePublicPresentationOperation.Present or
            RemotePublicPresentationOperation.Remove) &&
        (value.Operation != RemotePublicPresentationOperation.Present ||
         value.Flags != RemotePublicPresentationFlags.None) &&
        (value.Flags & ~AdmittedPublicPresentationFlags) == 0 &&
        (value.Operation != RemotePublicPresentationOperation.Remove ||
         value.Flags == RemotePublicPresentationFlags.None);

    private static bool IsCanonicalPublicPresentationLabel(string value)
        => IsCanonicalPublicPresentationText(
            value, MaximumPublicPresentationDisplayLabelUtf8Bytes);

    private static bool IsCanonicalPublicPresentationText(string value, int maximumUtf8Bytes)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim()) return false;
        foreach (char character in value)
            if (char.IsControl(character)) return false;
        try
        {
            return StrictPublicPresentationUtf8.GetByteCount(value) <= maximumUtf8Bytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
