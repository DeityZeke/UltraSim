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

[NetworkProtocol(1)]
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

/// <summary>Accepted N0.4 application protocol manifest and bounded dynamic codecs.</summary>
public static class UltraSimGameProtocol
{
    public const int MaximumMessageBytes = 16_384;
    public const int MaximumIdentityBytes = 1_024;

    /// <summary>Default semantic UI payload bound after reserving the largest application envelope.</summary>
    public static UiSemanticWireLimits DefaultUiWireLimits => new(
        MaximumMessageBytes - RemoteUiOperationHeaderCodec.EncodedSize,
        UiCompileLimits.Default);

    private static readonly NetworkProtocolManifest s_manifest = NetworkProtocolManifest.Create(
        version: 1,
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
}
