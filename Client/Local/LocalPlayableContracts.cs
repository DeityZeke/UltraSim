using UltraSim.Client.Core;
using UltraSim.Game.Admission;
using UltraSim.Game.Chat;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.World;
using UltraSim.Server.Runtime;
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
    IronSword = 8,
}

public readonly record struct LocalPlayableCommand(
    GamePlayerCommand Command,
    LocalPlayableSelection Selection,
    bool FaceRequested,
    ClientControlWorldPoint FacePoint,
    bool FacingRequiresAcceptedLocomotion,
    WorldPublicEntityId Target = default)
{
    public bool IsValid => Command.IsValid &&
        Selection is >= LocalPlayableSelection.None and <= LocalPlayableSelection.IronSword;
}

public enum LocalAdministrativeOperationKind : byte
{
    None = 0,
    Save = 1,
    Load = 2,
    Relocate = 3,
    OpenInventory = 4,
    EnableHosting = 5,
    DisableHosting = 6,
}

public readonly record struct LocalAdministrativeRequest(
    ulong Sequence,
    LocalAdministrativeOperationKind Kind,
    WorldPosition Position);

public readonly record struct LocalPlayableAuthorityView(
    ulong GenerationId,
    ulong SimulationTick,
    GameGuestIdentity GuestIdentity,
    WorldPublicEntityId PublicActor,
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
    LocalPlayableServerExtensionSnapshot ServerExtension,
    WorldPreparedView PreparedWorld)
{
    public bool IsReady => GenerationId != 0 && PreparedWorld is not null;
}

/// <summary>Thread-safe packet-free Chat destination exposed to an optional internal-server extension.</summary>
public interface ILocalPlayableChatSink
{
    bool CanPublish { get; }
    bool TryPublish(in GameSpeechFact fact);
}

/// <summary>Cold authority-thread inputs for an optional deployment adapter.</summary>
public sealed class LocalPlayableServerContext
{
    internal LocalPlayableServerContext(
        string identityRoot,
        WorldPack worldPack,
        UltraSimGameComposition composition,
        ServerRuntime runtime,
        AdmissionService admission,
        GameChatService chat,
        GamePlayableWorldScene scene,
        ParticipantId localParticipant,
        GameGuestIdentity localGuestIdentity,
        GameControlSessionEpoch localControlSession,
        GamePlayableParticipantEntities localEntities,
        WorldPublicEntityId localPublicEntity,
        ILocalPlayableChatSink localChat)
    {
        IdentityRoot = identityRoot;
        WorldPack = worldPack;
        Composition = composition;
        Runtime = runtime;
        Admission = admission;
        Chat = chat;
        Scene = scene;
        LocalParticipant = localParticipant;
        LocalGuestIdentity = localGuestIdentity;
        LocalControlSession = localControlSession;
        LocalEntities = localEntities;
        LocalPublicEntity = localPublicEntity;
        LocalChat = localChat;
    }

    public string IdentityRoot { get; }
    public WorldPack WorldPack { get; }
    public UltraSimGameComposition Composition { get; }
    public ServerRuntime Runtime { get; }
    public AdmissionService Admission { get; }
    public GameChatService Chat { get; }
    public GamePlayableWorldScene Scene { get; }
    public ParticipantId LocalParticipant { get; }
    public GameGuestIdentity LocalGuestIdentity { get; }
    public GameControlSessionEpoch LocalControlSession { get; }
    public GamePlayableParticipantEntities LocalEntities { get; }
    public WorldPublicEntityId LocalPublicEntity { get; }
    public ILocalPlayableChatSink LocalChat { get; }
}

public readonly record struct LocalPlayableServerExtensionSnapshot(
    bool Available,
    bool HostingEnabled,
    string? Endpoint,
    string? ApplicationRealm,
    string? DeploymentFingerprint,
    int RemoteParticipants,
    ulong ServerGenerationId,
    string? Detail);

/// <summary>
/// Optional deployment adapter progressed on the existing local authority owner thread. It may
/// add recipients, but it never owns or replaces the supplied internal server.
/// </summary>
public interface ILocalPlayableServerExtension : IDisposable
{
    bool OwnsSharedChatHandoff { get; }
    void Pump();
    bool TrySetHostingEnabled(bool enabled, out string? detail);
    LocalPlayableServerExtensionSnapshot GetSnapshot();
    int CopyPublicPresentationMetadata(Span<ClientPublicPresentationMetadata> destination);
}

public interface ILocalPlayableServerExtensionFactory
{
    ILocalPlayableServerExtension Create(LocalPlayableServerContext context);
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
    ulong UiColdOpensApplied,
    ulong UiHotOpensApplied,
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
