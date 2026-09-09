using System.Text;
using UltraSim.Game.Inspection;
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
    ActorState = 1 << 2,
}

public enum RemotePublicActorActionKind : byte
{
    None = 0,
    Interact = 1,
    Primary = 2,
}

public enum RemotePublicActorVerticalPhase : byte
{
    None = 0,
    Grounded = 1,
    Rising = 2,
    Falling = 3,
}

public enum RemotePublicPresentationApplyStatus : byte
{
    Applied = 1,
    Rejected = 2,
    CapacityExceeded = 3,
}

/// <summary>
/// One retained cold public-presentation metadata occurrence. A bounded display label and canonical
/// typed inspection facts follow only for a Present operation; hot movement stays independent.
/// </summary>
[NetworkMessage(144, "ultrasim.game.public-presentation-metadata", 4, NetworkLane.Control)]
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
    public uint MainHandPrefabId;
    public RemotePublicActorVerticalPhase VerticalPhase;
    public RemotePublicActorActionKind ActorAction;
    public ulong ActorActionSequence;
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
    public const int MaximumPublicPresentationMessageBytes = 768;
    public const RemotePublicPresentationFlags AdmittedPublicPresentationFlags =
        RemotePublicPresentationFlags.NameplateEligible |
        RemotePublicPresentationFlags.ItemProperties |
        RemotePublicPresentationFlags.ActorState;
    private static readonly UTF8Encoding StrictPublicPresentationUtf8 = new(false, true);

    public static bool TryWritePublicPresentationMetadata(
        Span<byte> destination,
        in RemotePublicPresentationMetadataHeader header,
        string displayLabel,
        InspectionPropertySet? properties,
        out int written)
    {
        written = 0;
        if (!IsValidPublicPresentationHeader(in header) || displayLabel is null)
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
                        ? properties is null
                        : properties is not null) ||
                    !TryWriteInspectionProperties(ref writer, properties))
                    return false;
                break;
            case RemotePublicPresentationOperation.Remove:
                if (displayLabel.Length != 0 || properties is not null) return false;
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
        out InspectionPropertySet? properties)
    {
        header = default;
        displayLabel = string.Empty;
        properties = null;
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
                !TryReadInspectionProperties(ref reader, out properties) || !reader.IsComplete)
                return false;
            displayLabel = Encoding.UTF8.GetString(label);
            if (!IsCanonicalPublicPresentationLabel(displayLabel) ||
                ((candidate.Flags & RemotePublicPresentationFlags.ItemProperties) != 0
                    ? properties is null
                    : properties is not null))
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
        ((value.Flags & RemotePublicPresentationFlags.ItemProperties) == 0 ||
         (value.Flags & RemotePublicPresentationFlags.ActorState) == 0) &&
        ((value.Flags & RemotePublicPresentationFlags.ActorState) != 0
            ? value.VerticalPhase is >= RemotePublicActorVerticalPhase.Grounded and
                    <= RemotePublicActorVerticalPhase.Falling &&
                value.ActorAction is >= RemotePublicActorActionKind.None and
                    <= RemotePublicActorActionKind.Primary &&
                (value.ActorAction == RemotePublicActorActionKind.None
                    ? value.ActorActionSequence == 0
                    : value.ActorActionSequence != 0)
            : value.MainHandPrefabId == 0 && value.VerticalPhase == RemotePublicActorVerticalPhase.None &&
                value.ActorAction == RemotePublicActorActionKind.None &&
                value.ActorActionSequence == 0) &&
        (value.Operation != RemotePublicPresentationOperation.Remove ||
         value.Flags == RemotePublicPresentationFlags.None &&
         value.MainHandPrefabId == 0 &&
         value.VerticalPhase == RemotePublicActorVerticalPhase.None &&
         value.ActorAction == RemotePublicActorActionKind.None &&
         value.ActorActionSequence == 0);

    private static bool IsCanonicalPublicPresentationLabel(string value)
        => IsCanonicalPublicPresentationText(
            value, MaximumPublicPresentationDisplayLabelUtf8Bytes);

    private static bool TryWriteInspectionProperties(
        ref NetworkPacketWriter writer,
        InspectionPropertySet? properties)
    {
        int count = properties?.Count ?? 0;
        if (count > InspectionPropertySet.MaximumProperties ||
            !writer.TryWriteByte(checked((byte)count)))
            return false;
        if (properties is null) return true;
        foreach (ref readonly InspectionPropertyFact fact in properties.Facts)
        {
            if (!writer.TryWriteUInt16(fact.Id.Value) ||
                !writer.TryWriteByte((byte)fact.Value.Kind))
                return false;
            switch (fact.Value.Kind)
            {
                case InspectionPropertyValueKind.Integer:
                case InspectionPropertyValueKind.MassGrams:
                    if (!writer.TryWriteInt64(fact.Value.First)) return false;
                    break;
                case InspectionPropertyValueKind.IntegerRange:
                case InspectionPropertyValueKind.Fraction:
                    if (!writer.TryWriteInt64(fact.Value.First) ||
                        !writer.TryWriteInt64(fact.Value.Second))
                        return false;
                    break;
                case InspectionPropertyValueKind.Token:
                    if (!writer.TryWriteBoundedUtf8(
                            fact.Value.Token!, InspectionPropertyValue.MaximumTokenUtf8Bytes))
                        return false;
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    private static bool TryReadInspectionProperties(
        ref NetworkPacketReader reader,
        out InspectionPropertySet? properties)
    {
        properties = null;
        if (!reader.TryReadByte(out byte count) || count > InspectionPropertySet.MaximumProperties)
            return false;
        if (count == 0) return true;
        var facts = new InspectionPropertyFact[count];
        ushort previous = 0;
        for (int index = 0; index < facts.Length; index++)
        {
            if (!reader.TryReadUInt16(out ushort id) || id <= previous ||
                !reader.TryReadByte(out byte kindByte))
                return false;
            previous = id;
            var kind = (InspectionPropertyValueKind)kindByte;
            InspectionPropertyValue value;
            switch (kind)
            {
                case InspectionPropertyValueKind.Integer:
                    if (!reader.TryReadInt64(out long integer)) return false;
                    value = InspectionPropertyValue.Integer(integer);
                    break;
                case InspectionPropertyValueKind.IntegerRange:
                    if (!reader.TryReadInt64(out long minimum) ||
                        !reader.TryReadInt64(out long maximum))
                        return false;
                    value = InspectionPropertyValue.IntegerRange(minimum, maximum);
                    break;
                case InspectionPropertyValueKind.Token:
                    if (!reader.TryReadBoundedUtf8Bytes(
                            InspectionPropertyValue.MaximumTokenUtf8Bytes,
                            out ReadOnlySpan<byte> tokenBytes) ||
                        !InspectionPropertyValue.TryToken(
                            Encoding.UTF8.GetString(tokenBytes), out value))
                        return false;
                    break;
                case InspectionPropertyValueKind.Fraction:
                    if (!reader.TryReadInt64(out long current) ||
                        !reader.TryReadInt64(out long fractionMaximum))
                        return false;
                    value = InspectionPropertyValue.Fraction(current, fractionMaximum);
                    break;
                case InspectionPropertyValueKind.MassGrams:
                    if (!reader.TryReadInt64(out long grams)) return false;
                    value = InspectionPropertyValue.MassGrams(grams);
                    break;
                default:
                    return false;
            }
            var fact = new InspectionPropertyFact(new InspectionPropertyId(id), value);
            if (!fact.IsCanonical) return false;
            facts[index] = fact;
        }
        return InspectionPropertySet.TryCreate(facts, out properties);
    }

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
