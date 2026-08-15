using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Game.Protocol;

public enum RemoteAuthenticationStatus : byte
{
    Accepted = 0,
    Rejected = 1,
}

public enum RemoteUiOperationKind : byte
{
    ColdOpen = 0,
    HotOpen = 1,
    Update = 2,
    Close = 3,
}

public enum RemoteUiApplyStatus : byte
{
    Applied = 0,
    Duplicate = 1,
    Rejected = 2,
}

[NetworkProtocol(2)]
public static partial class GeneratedUltraSimGameProtocol
{
}

[NetworkMessage(100, "ultrasim.game.authentication-request", 1, NetworkLane.Control)]
public partial struct RemoteAuthenticationRequestHeader
{
    public ulong AttemptId;
}

[NetworkMessage(101, "ultrasim.game.authentication-result", 1, NetworkLane.Control)]
public partial struct RemoteAuthenticationResultMessage
{
    public ulong AttemptId;
    public RemoteAuthenticationStatus Status;
    public ulong ParticipantId;
    public ulong ApplicationSessionEpoch;
}

[NetworkMessage(102, "ultrasim.game.ui-operation", 1, NetworkLane.Bulk)]
public partial struct RemoteUiOperationHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong OperationId;
    public RemoteUiOperationKind Kind;
}

[NetworkMessage(103, "ultrasim.game.ui-apply-result", 1, NetworkLane.Control)]
public partial struct RemoteUiApplyResultMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong OperationId;
    public ulong ViewInstanceId;
    public ulong ModelSequence;
    public RemoteUiApplyStatus Status;
}

[NetworkMessage(104, "ultrasim.game.ui-action", 1, NetworkLane.Control)]
public partial struct RemoteUiActionHeader
{
    public ulong ApplicationSessionEpoch;
}

[NetworkMessage(105, "ultrasim.game.session-detach", 1, NetworkLane.Control)]
public partial struct RemoteSessionDetachMessage
{
    public ulong ApplicationSessionEpoch;
}

public enum RemoteAuthoritativeInputKind : byte
{
    ApplyTickMarkerDelta = 0,
    SetInputVelocity = 1,
}

/// <summary>Header for one complete bounded redundant input bundle.</summary>
[NetworkMessage(106, "ultrasim.game.authoritative-input", 1, NetworkLane.Input)]
public partial struct RemoteAuthoritativeInputBundleHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public byte SampleCount;
}

/// <summary>One client intent repeated in newest bundles until owner acknowledgement.</summary>
public readonly record struct RemoteAuthoritativeInputSample(
    ulong Sequence,
    ulong TargetSimulationTick,
    long ExpiresAtWorldTimeMicroseconds,
    RemoteAuthoritativeInputKind Kind,
    int Value);

/// <summary>Latest authoritative state for the one controlled N0.6 entity.</summary>
[NetworkMessage(107, "ultrasim.game.owner-state", 1, NetworkLane.OwnerState)]
public partial struct RemoteOwnerStateMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public uint EntityIndex;
    public uint EntityGeneration;
    public ulong SimulationTick;
    public ulong FrameSequence;
    public ulong LastProcessedInputSequence;
    public int TickMarker;
    public int InputVelocity;
    public uint ClockRevision;
    public bool IsPresent;
}

/// <summary>Reliable cadence/time interpretation effective from a declared authoritative tick.</summary>
[NetworkMessage(108, "ultrasim.game.clock-anchor", 1, NetworkLane.Control)]
public partial struct RemoteClockAnchorMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public uint ClockRevision;
    public ulong EffectiveFromSimulationTick;
    public int SimulationFrequencyHz;
    public long WorldTimeMicroseconds;
    public int WorldTimeFractionQuanta;
}

/// <summary>Accepted game application protocol manifest and bounded dynamic codecs through N0.6.</summary>
public static class UltraSimGameProtocol
{
    public const int MaximumMessageBytes = 16_384;
    public const int MaximumIdentityBytes = 1_024;
    public const int MaximumInputRedundancy = 3;

    /// <summary>Default semantic UI payload bound after reserving the largest application envelope.</summary>
    public static UiSemanticWireLimits DefaultUiWireLimits => new(
        MaximumMessageBytes - RemoteUiOperationHeaderCodec.EncodedSize,
        UiCompileLimits.Default);

    private static readonly NetworkProtocolManifest s_manifest = NetworkProtocolManifest.Create(
        version: 2,
        [
            RemoteAuthenticationRequestHeaderCodec.Descriptor with
                { MaximumEncodedBytes = MaximumMessageBytes },
            RemoteAuthenticationResultMessageCodec.Descriptor,
            RemoteUiOperationHeaderCodec.Descriptor with
                { MaximumEncodedBytes = MaximumMessageBytes },
            RemoteUiApplyResultMessageCodec.Descriptor,
            RemoteUiActionHeaderCodec.Descriptor with
                { MaximumEncodedBytes = MaximumMessageBytes },
            RemoteSessionDetachMessageCodec.Descriptor,
            RemoteAuthoritativeInputBundleHeaderCodec.Descriptor with
                { MaximumEncodedBytes = RemoteAuthoritativeInputBundleHeaderCodec.EncodedSize +
                    MaximumInputRedundancy * (sizeof(ulong) * 2 + sizeof(long) + sizeof(byte) + sizeof(int)) },
            RemoteOwnerStateMessageCodec.Descriptor,
            RemoteClockAnchorMessageCodec.Descriptor,
        ]);

    public static NetworkProtocolManifest Manifest => s_manifest;

    public static bool TryWriteAuthenticationRequest(
        Span<byte> destination,
        ulong attemptId,
        string provider,
        string credential,
        out int written)
    {
        written = 0;
        var writer = new NetworkPacketWriter(destination);
        var header = new RemoteAuthenticationRequestHeader { AttemptId = attemptId };
        if (!RemoteAuthenticationRequestHeaderCodec.TryWrite(ref writer, in header) ||
            !writer.TryWriteBoundedUtf8(provider, MaximumIdentityBytes) ||
            !writer.TryWriteBoundedUtf8(credential, MaximumIdentityBytes)) return false;
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadAuthenticationRequest(
        ReadOnlySpan<byte> source,
        out RemoteAuthenticationRequestHeader header,
        out string provider,
        out string credential)
    {
        header = default;
        provider = credential = string.Empty;
        var reader = new NetworkPacketReader(source);
        if (!RemoteAuthenticationRequestHeaderCodec.TryRead(ref reader, out header) ||
            header.AttemptId == 0 ||
            !reader.TryReadBoundedUtf8Bytes(MaximumIdentityBytes, out ReadOnlySpan<byte> providerBytes) ||
            !reader.TryReadBoundedUtf8Bytes(MaximumIdentityBytes, out ReadOnlySpan<byte> credentialBytes) ||
            !reader.IsComplete) return false;
        provider = System.Text.Encoding.UTF8.GetString(providerBytes);
        credential = System.Text.Encoding.UTF8.GetString(credentialBytes);
        return provider.Length != 0 && credential.Length != 0;
    }

    public static bool TryWriteUiOperation(
        Span<byte> destination,
        in RemoteUiOperationHeader header,
        ReadOnlySpan<byte> semanticPayload,
        out int written)
    {
        written = 0;
        var writer = new NetworkPacketWriter(destination);
        if (!RemoteUiOperationHeaderCodec.TryWrite(ref writer, in header) ||
            !writer.TryWriteBytes(semanticPayload)) return false;
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadUiOperation(
        ReadOnlySpan<byte> source,
        out RemoteUiOperationHeader header,
        out ReadOnlySpan<byte> semanticPayload)
    {
        var reader = new NetworkPacketReader(source);
        if (!RemoteUiOperationHeaderCodec.TryRead(ref reader, out header) ||
            header.ApplicationSessionEpoch == 0 || header.OperationId == 0 ||
            !reader.TryReadBytes(reader.Remaining, out semanticPayload) || semanticPayload.Length == 0)
        {
            header = default;
            semanticPayload = default;
            return false;
        }
        return true;
    }

    public static bool TryWriteUiAction(
        Span<byte> destination,
        ulong applicationSessionEpoch,
        ReadOnlySpan<byte> semanticPayload,
        out int written)
    {
        written = 0;
        var writer = new NetworkPacketWriter(destination);
        var header = new RemoteUiActionHeader { ApplicationSessionEpoch = applicationSessionEpoch };
        if (!RemoteUiActionHeaderCodec.TryWrite(ref writer, in header) ||
            !writer.TryWriteBytes(semanticPayload)) return false;
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadUiAction(
        ReadOnlySpan<byte> source,
        out RemoteUiActionHeader header,
        out ReadOnlySpan<byte> semanticPayload)
    {
        var reader = new NetworkPacketReader(source);
        if (!RemoteUiActionHeaderCodec.TryRead(ref reader, out header) ||
            header.ApplicationSessionEpoch == 0 ||
            !reader.TryReadBytes(reader.Remaining, out semanticPayload) || semanticPayload.Length == 0)
        {
            header = default;
            semanticPayload = default;
            return false;
        }
        return true;
    }

    public static bool TryWriteAuthoritativeInputBundle(
        Span<byte> destination,
        ulong applicationSessionEpoch,
        StateEpoch stateEpoch,
        ReadOnlySpan<RemoteAuthoritativeInputSample> samples,
        out int written)
    {
        written = 0;
        if (applicationSessionEpoch == 0 || !stateEpoch.IsValid || samples.IsEmpty ||
            samples.Length > MaximumInputRedundancy) return false;
        var writer = new NetworkPacketWriter(destination);
        var header = new RemoteAuthoritativeInputBundleHeader
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            StateEpoch = stateEpoch.Value,
            SampleCount = checked((byte)samples.Length),
        };
        if (!RemoteAuthoritativeInputBundleHeaderCodec.TryWrite(ref writer, in header)) return false;
        ulong previous = 0;
        foreach (ref readonly RemoteAuthoritativeInputSample sample in samples)
        {
            if (sample.Sequence == 0 || sample.Sequence <= previous ||
                sample.TargetSimulationTick == ulong.MaxValue ||
                sample.ExpiresAtWorldTimeMicroseconds < 0 ||
                (uint)sample.Kind > (uint)RemoteAuthoritativeInputKind.SetInputVelocity ||
                !writer.TryWriteUInt64(sample.Sequence) ||
                !writer.TryWriteUInt64(sample.TargetSimulationTick) ||
                !writer.TryWriteInt64(sample.ExpiresAtWorldTimeMicroseconds) ||
                !writer.TryWriteByte((byte)sample.Kind) ||
                !writer.TryWriteInt32(sample.Value)) return false;
            previous = sample.Sequence;
        }
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadAuthoritativeInputBundle(
        ReadOnlySpan<byte> source,
        out RemoteAuthoritativeInputBundleHeader header,
        Span<RemoteAuthoritativeInputSample> samples,
        out int count)
    {
        header = default;
        count = 0;
        var reader = new NetworkPacketReader(source);
        if (!RemoteAuthoritativeInputBundleHeaderCodec.TryRead(ref reader, out header) ||
            header.ApplicationSessionEpoch == 0 || header.StateEpoch == 0 ||
            header.SampleCount == 0 || header.SampleCount > MaximumInputRedundancy ||
            header.SampleCount > samples.Length) return false;
        ulong previous = 0;
        for (int index = 0; index < header.SampleCount; index++)
        {
            if (!reader.TryReadUInt64(out ulong sequence) || sequence == 0 || sequence <= previous ||
                !reader.TryReadUInt64(out ulong targetTick) || targetTick == ulong.MaxValue ||
                !reader.TryReadInt64(out long expiry) || expiry < 0 ||
                !reader.TryReadByte(out byte kind) || kind > (byte)RemoteAuthoritativeInputKind.SetInputVelocity ||
                !reader.TryReadInt32(out int value))
            {
                samples[..index].Clear();
                header = default;
                return false;
            }
            samples[index] = new(sequence, targetTick, expiry,
                (RemoteAuthoritativeInputKind)kind, value);
            previous = sequence;
        }
        if (!reader.IsComplete)
        {
            samples[..header.SampleCount].Clear();
            header = default;
            return false;
        }
        count = header.SampleCount;
        return true;
    }
}
