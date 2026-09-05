using UltraSim.Client.Core;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Client.Local;

public enum LocalPlayableSelection : byte
{
    None = 0,
    CabinDoor = 1,
    CabinChest = 2,
    SkyTelepad = 3,
    FarGate = 4,
    InformationalSign = 5,
    FallbackLabSign = 6,
    Unsupported = 7,
}

public readonly record struct LocalPlayableCommand(
    GamePlayerCommand Command,
    LocalPlayableSelection Selection,
    bool FaceRequested,
    ClientControlWorldPoint FacePoint,
    bool FacingRequiresAcceptedLocomotion)
{
    public bool IsValid => Command.IsValid &&
        Selection is >= LocalPlayableSelection.None and <= LocalPlayableSelection.Unsupported;
}

public enum LocalAdministrativeOperationKind : byte
{
    None = 0,
    Save = 1,
    Load = 2,
    Relocate = 3,
    OpenInventory = 4,
}

public readonly record struct LocalAdministrativeRequest(
    ulong Sequence,
    LocalAdministrativeOperationKind Kind,
    WorldPosition Position);

public readonly record struct LocalPlayableAuthorityView(
    ulong GenerationId,
    ulong SimulationTick,
    WorldPosition PlayerPosition,
    WorldYaw PlayerOrientation,
    bool DoorIsOpen,
    bool BreadIsInBackpack,
    ulong LastCommandSequence,
    LocalPlayableSelection LastSelection,
    GamePlayerCommandResult LastCommandResult,
    GameWorldActionStatus LastChestAction,
    ulong LastAdministrativeSequence,
    LocalAdministrativeOperationKind LastAdministrativeKind,
    bool LastAdministrativeSucceeded,
    string? LastAdministrativeMessage,
    WorldPreparedView PreparedWorld)
{
    public bool IsReady => GenerationId != 0 && PreparedWorld is not null;
}

public readonly record struct LocalPlayableSessionLimits(
    int CommandCapacity,
    int AdministrativeCapacity,
    int ChatSubmissionCapacity,
    int WorldDeliveryCapacity,
    int WorldProofCapacity,
    int ChatDeliveryCapacity,
    int ChatProofCapacity,
    int UiOperationCapacity,
    int UiActionCapacity,
    int UiProofCapacity)
{
    public static LocalPlayableSessionLimits Default => new(
        64, 8, 16, 64, 64, 32, 32, 32, 32, 32);

    internal void Validate()
    {
        if (CommandCapacity <= 0 || AdministrativeCapacity <= 0 ||
            ChatSubmissionCapacity <= 0 || WorldDeliveryCapacity <= 0 ||
            WorldProofCapacity <= 0 || ChatDeliveryCapacity <= 0 ||
            ChatProofCapacity <= 0 || UiOperationCapacity <= 0 ||
            UiActionCapacity <= 0 || UiProofCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(LocalPlayableSessionLimits));
    }
}

public readonly record struct LocalPlayableSessionSnapshot(
    bool IsReady,
    bool IsRunning,
    ulong AuthorityTicks,
    int PendingCommands,
    int PendingWorldDeliveries,
    int PendingChatDeliveries,
    int CommandHighWater,
    int WorldDeliveryHighWater,
    int ChatDeliveryHighWater,
    ulong WorldDeliveriesApplied,
    ulong WorldDeliveriesRejected,
    ulong WorldDeliveriesInvalidated,
    ulong ChatDeliveriesApplied,
    ulong ChatDeliveriesRejected,
    ulong UiOperationsApplied,
    ulong UiOperationsRejected,
    long KnownReservedBytes,
    string? Fault);

public enum LocalSemanticProofStatus : byte
{
    Applied = 1,
    Rejected = 2,
}

public readonly record struct LocalChatSubmission(
    ulong Sequence,
    ClientChatSpeechMode Mode,
    string Text);
