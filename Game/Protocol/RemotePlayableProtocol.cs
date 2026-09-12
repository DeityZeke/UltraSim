using UltraSim.Game.Admission;
using UltraSim.Game.Input;
using UltraSim.Network;

namespace UltraSim.Game.Protocol;

public enum RemoteGuestAdmissionStatus : byte
{
    None = 0,
    Accepted = 1,
    Rejected = 2,
}

public enum RemoteGuestAdmissionApplyStatus : byte
{
    None = 0,
    Applied = 1,
    Rejected = 2,
}

/// <summary>Wire-stable projection of the finite playable Game action result.</summary>
public enum RemotePlayableActionStatus : byte
{
    NotRequested = 0,
    Accepted = 1,
    InvalidCommand = 2,
    StaleCommand = 3,
    StaleControlSession = 4,
    MissingActor = 5,
    MissingTarget = 6,
    UnauthorizedContainer = 7,
    OutOfRange = 8,
    OutsidePreparedWorld = 9,
    InvariantViolation = 10,
    Blocked = 11,
    Unsupported = 12,
    NotGrounded = 13,
    RateLimited = 14,
}

[Flags]
public enum RemotePlayableCommandFlags : byte
{
    None = 0,
    FaceRequested = 1 << 0,
    FacingRequiresAcceptedLocomotion = 1 << 1,
    OpenInventoryRequested = 1 << 2,
    OpenServerMonitorRequested = 1 << 3,
}

public readonly record struct RemotePublicEntityTarget(uint Index, uint Generation)
{
    public bool IsValid => Generation != 0;
}

/// <summary>Untrusted guest request; the bounded UTF-8 display name follows this header.</summary>
[NetworkMessage(135, "ultrasim.game.guest-admission-request", 1, NetworkLane.Event)]
public partial struct RemoteGuestAdmissionRequestHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong AttemptSequence;
    public byte Body;
}

/// <summary>
/// Retained guest-admission result. The canonical accepted name or private rejection text follows.
/// </summary>
[NetworkMessage(136, "ultrasim.game.guest-admission-result", 1, NetworkLane.Event)]
public partial struct RemoteGuestAdmissionResultHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong AttemptSequence;
    public RemoteGuestAdmissionStatus Status;
    public byte Rejection;
    public ulong ParticipantId;
    public ulong ControlSessionEpoch;
    public ulong ServerGenerationId;
    public uint PublicEntityIndex;
    public uint PublicEntityGeneration;
    public byte Body;
}

/// <summary>Explicit client semantic application proof for the retained admission result.</summary>
[NetworkMessage(137, "ultrasim.game.guest-admission-applied", 1, NetworkLane.Control)]
public partial struct RemoteGuestAdmissionAppliedMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong AttemptSequence;
    public RemoteGuestAdmissionApplyStatus Status;
}

/// <summary>
/// One real retained GamePlayerCommand. A public entity is only an untrusted interaction nomination.
/// </summary>
[NetworkMessage(138, "ultrasim.game.player-command", 2, NetworkLane.Event)]
public partial struct RemotePlayableCommandMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong ControlSessionEpoch;
    public ulong CommandSequence;
    public byte LocomotionKind;
    public short DirectX;
    public short DirectZ;
    public long DestinationXMillimeters;
    public long DestinationZMillimeters;
    public ushort HeldActions;
    public ushort PressedActions;
    public uint TargetPublicEntityIndex;
    public uint TargetPublicEntityGeneration;
    public RemotePlayableCommandFlags Flags;
    public long FaceXMillimeters;
    public long FaceZMillimeters;
}

/// <summary>Latest complete owner-private controlled-actor result after an authoritative tick.</summary>
[NetworkMessage(139, "ultrasim.game.playable-owner-state", 2, NetworkLane.OwnerState)]
public partial struct RemotePlayableOwnerStateMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong ControlSessionEpoch;
    public ulong ServerGenerationId;
    public ulong StateSequence;
    public ulong SimulationTick;
    public ulong LastProcessedCommandSequence;
    public uint PublicEntityIndex;
    public uint PublicEntityGeneration;
    public long GlobalXMillimeters;
    public int YMillimeters;
    public long GlobalZMillimeters;
    public ushort OrientationUnits;
    public RemotePlayableActionStatus Admission;
    public RemotePlayableActionStatus Locomotion;
    public RemotePlayableActionStatus Jump;
    public RemotePlayableActionStatus Primary;
    public RemotePlayableActionStatus Interaction;
    public byte Body;
}

public static partial class UltraSimGameProtocol
{
    public const int MaximumGuestDisplayNameUtf8Bytes = 96;
    public const int MaximumGuestFeedbackUtf8Bytes = 256;
    public const int MaximumGuestAdmissionMessageBytes = 512;

    public static bool TryWriteGuestAdmissionRequest(
        Span<byte> destination,
        ulong applicationSessionEpoch,
        ulong attemptSequence,
        in GameGuestAdmissionRequest request,
        out int written)
    {
        written = 0;
        if (applicationSessionEpoch == 0 || attemptSequence == 0 ||
            request.Body is not (GameGuestBody.HumanFemale or GameGuestBody.HumanMale) ||
            request.DisplayName is null)
            return false;
        var header = new RemoteGuestAdmissionRequestHeader
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            AttemptSequence = attemptSequence,
            Body = (byte)request.Body,
        };
        var writer = new NetworkPacketWriter(destination);
        if (!RemoteGuestAdmissionRequestHeaderCodec.TryWrite(ref writer, in header) ||
            !writer.TryWriteBoundedUtf8(request.DisplayName, MaximumGuestDisplayNameUtf8Bytes) ||
            writer.WrittenCount > MaximumGuestAdmissionMessageBytes)
            return false;
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadGuestAdmissionRequest(
        ReadOnlySpan<byte> source,
        out RemoteGuestAdmissionRequestHeader header,
        out GameGuestAdmissionRequest request)
    {
        header = default;
        request = default;
        if (source.Length > MaximumGuestAdmissionMessageBytes) return false;
        var reader = new NetworkPacketReader(source);
        if (!RemoteGuestAdmissionRequestHeaderCodec.TryRead(
                ref reader, out RemoteGuestAdmissionRequestHeader staged) ||
            staged.ApplicationSessionEpoch == 0 || staged.AttemptSequence == 0 ||
            staged.Body is not ((byte)GameGuestBody.HumanFemale) and
                not ((byte)GameGuestBody.HumanMale) ||
            !reader.TryReadBoundedUtf8Bytes(
                MaximumGuestDisplayNameUtf8Bytes, out ReadOnlySpan<byte> name) ||
            name.IsEmpty || !reader.IsComplete)
            return false;
        header = staged;
        request = new GameGuestAdmissionRequest(
            (GameGuestBody)staged.Body,
            System.Text.Encoding.UTF8.GetString(name));
        return true;
    }

    public static bool TryWriteGuestAdmissionResult(
        Span<byte> destination,
        in RemoteGuestAdmissionResultHeader header,
        string text,
        out int written)
    {
        written = 0;
        bool accepted = header.Status == RemoteGuestAdmissionStatus.Accepted;
        bool valid = header.ApplicationSessionEpoch != 0 && header.AttemptSequence != 0 &&
            !string.IsNullOrWhiteSpace(text) &&
            (accepted
                ? header.Rejection == 0 && header.ParticipantId != 0 &&
                    header.ControlSessionEpoch != 0 && header.ServerGenerationId != 0 &&
                    header.PublicEntityGeneration != 0 &&
                    header.Body is (byte)GameGuestBody.HumanFemale or (byte)GameGuestBody.HumanMale
                : header.Status == RemoteGuestAdmissionStatus.Rejected &&
                    header.Rejection != 0 && header.ParticipantId == 0 &&
                    header.ControlSessionEpoch == 0 && header.ServerGenerationId == 0 &&
                    header.PublicEntityIndex == 0 && header.PublicEntityGeneration == 0 &&
                    header.Body == 0);
        if (!valid) return false;
        var writer = new NetworkPacketWriter(destination);
        int maximumText = accepted
            ? MaximumGuestDisplayNameUtf8Bytes
            : MaximumGuestFeedbackUtf8Bytes;
        if (!RemoteGuestAdmissionResultHeaderCodec.TryWrite(ref writer, in header) ||
            !writer.TryWriteBoundedUtf8(text, maximumText) ||
            writer.WrittenCount > MaximumGuestAdmissionMessageBytes)
            return false;
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadGuestAdmissionResult(
        ReadOnlySpan<byte> source,
        out RemoteGuestAdmissionResultHeader header,
        out string text)
    {
        header = default;
        text = string.Empty;
        if (source.Length > MaximumGuestAdmissionMessageBytes) return false;
        var reader = new NetworkPacketReader(source);
        if (!RemoteGuestAdmissionResultHeaderCodec.TryRead(
                ref reader, out RemoteGuestAdmissionResultHeader staged))
            return false;
        bool accepted = staged.Status == RemoteGuestAdmissionStatus.Accepted;
        int maximumText = accepted
            ? MaximumGuestDisplayNameUtf8Bytes
            : MaximumGuestFeedbackUtf8Bytes;
        if (!reader.TryReadBoundedUtf8Bytes(maximumText, out ReadOnlySpan<byte> bytes) ||
            bytes.IsEmpty || !reader.IsComplete)
            return false;
        text = System.Text.Encoding.UTF8.GetString(bytes);
        if (!TryWriteGuestAdmissionResult(
                stackalloc byte[MaximumGuestAdmissionMessageBytes], in staged, text, out _))
        {
            text = string.Empty;
            return false;
        }
        header = staged;
        return true;
    }

    public static bool TryCreatePlayableCommand(
        in RemotePlayableCommandMessage remote,
        out GamePlayerCommand command,
        out RemotePublicEntityTarget target)
    {
        command = default;
        target = default;
        if (remote.ApplicationSessionEpoch == 0 || remote.ControlSessionEpoch == 0 ||
            remote.CommandSequence == 0 ||
            (remote.Flags & ~(RemotePlayableCommandFlags.FaceRequested |
                RemotePlayableCommandFlags.FacingRequiresAcceptedLocomotion |
                RemotePlayableCommandFlags.OpenInventoryRequested |
                RemotePlayableCommandFlags.OpenServerMonitorRequested)) != 0 ||
            (remote.Flags & RemotePlayableCommandFlags.OpenInventoryRequested) != 0 &&
                (remote.Flags & RemotePlayableCommandFlags.OpenServerMonitorRequested) != 0 ||
            (remote.Flags & RemotePlayableCommandFlags.FacingRequiresAcceptedLocomotion) != 0 &&
                (remote.Flags & RemotePlayableCommandFlags.FaceRequested) == 0 ||
            remote.TargetPublicEntityGeneration == 0 && remote.TargetPublicEntityIndex != 0 ||
            (remote.Flags & RemotePlayableCommandFlags.FaceRequested) == 0 &&
                (remote.FaceXMillimeters != 0 || remote.FaceZMillimeters != 0))
            return false;
        GameLocomotionUpdate locomotion = new(
            (GameLocomotionUpdateKind)remote.LocomotionKind,
            remote.DirectX,
            remote.DirectZ,
            remote.DestinationXMillimeters,
            remote.DestinationZMillimeters);
        command = new GamePlayerCommand(
            new GameControlSessionEpoch(remote.ControlSessionEpoch),
            remote.CommandSequence,
            locomotion,
            (GamePlayerActions)remote.HeldActions,
            (GamePlayerActions)remote.PressedActions);
        if (!command.IsValid)
        {
            command = default;
            return false;
        }
        if (remote.TargetPublicEntityGeneration != 0)
            target = new RemotePublicEntityTarget(
                remote.TargetPublicEntityIndex, remote.TargetPublicEntityGeneration);
        return true;
    }

    public static RemotePlayableCommandMessage CreatePlayableCommand(
        ulong applicationSessionEpoch,
        in GamePlayerCommand command,
        RemotePublicEntityTarget target = default,
        RemotePlayableCommandFlags flags = RemotePlayableCommandFlags.None,
        long faceXMillimeters = 0,
        long faceZMillimeters = 0) => new()
    {
        ApplicationSessionEpoch = applicationSessionEpoch,
        ControlSessionEpoch = command.ControlSession.Value,
        CommandSequence = command.Sequence,
        LocomotionKind = (byte)command.Locomotion.Kind,
        DirectX = command.Locomotion.DirectX,
        DirectZ = command.Locomotion.DirectZ,
        DestinationXMillimeters = command.Locomotion.DestinationXMillimeters,
        DestinationZMillimeters = command.Locomotion.DestinationZMillimeters,
        HeldActions = (ushort)command.HeldActions,
        PressedActions = (ushort)command.PressedActions,
        TargetPublicEntityIndex = target.Index,
        TargetPublicEntityGeneration = target.Generation,
        Flags = flags,
        FaceXMillimeters = faceXMillimeters,
        FaceZMillimeters = faceZMillimeters,
    };
}
