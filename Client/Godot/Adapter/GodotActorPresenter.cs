using Godot;
using UltraSim.Client.Core;
using UltraSim.Game.World;

namespace UltraSim.Client.Godot;

public interface IGodotActorVisualResolver
{
    Node3D? Instantiate(in ClientActorDefinition definition);
}

/// <summary>Optional exclusion for public identities realized as individual actor roots.</summary>
public interface IGodotWorldPublicBatchFilter
{
    bool ShouldBatch(in WorldPublicRecord record);
}

public readonly record struct GodotActorPresenterSnapshot(
    int ActiveActors,
    int PrimitiveFallbackActors,
    int AnimatedActors,
    int EquippedMainHands,
    int VisibleNameplates,
    int VisibleOverheadLabels,
    int VisiblePrimaryCues,
    int VisibleInteractCues,
    bool RevealHeld,
    ulong Added,
    ulong Updated,
    ulong Removed,
    ulong PrimaryCuesPresented,
    ulong InteractCuesPresented,
    ulong LandingCuesPresented,
    ulong VisualReplacements)
{
    public int DetailedActors { get; init; }
    public int BatchedPrimitiveActors { get; init; }
    public int CheapBatchGroups { get; init; }
    public int CheapBatchRows { get; init; }
    public int NativeBodyNodes { get; init; }
    public int NativePresentationNodes { get; init; }
    public ulong TierPromotions { get; init; }
    public ulong TierDemotions { get; init; }
    public ulong CheapTransformWrites { get; init; }
    public ulong CheapBoundsWrites { get; init; }
    public int CheapOwnedResources { get; init; }
    public long CheapReservedPayloadBytes { get; init; }
    public long RepresentationReservedPayloadBytes { get; init; }
    public string ToDiagnosticText() => $"Actors detailed/cheap={DetailedActors}/{BatchedPrimitiveActors} " +
        $"fallback={PrimitiveFallbackActors} batches/rows={CheapBatchGroups}/{CheapBatchRows} " +
        $"promote/demote={TierPromotions}/{TierDemotions}";
}

public enum GodotActorMotionPhase : byte
{
    Idle = 0,
    Walk = 1,
    Rising = 2,
    Falling = 3,
}

public readonly record struct GodotActorPresentationDiagnostics(
    GodotActorMotionPhase MotionPhase,
    bool HasAnimationPlayer,
    bool UsesPrimitiveFallback,
    bool MainHandVisible,
    bool PrimaryCueActive,
    bool InteractCueActive);

/// <summary>
/// Native realization of engine-neutral actor state. Semantic identity and presented pose survive
/// transitions between Detailed hierarchy and bounded primitive MultiMesh rows. Labels do not
/// mandate Detailed bodies; controlled/equipment/action/overhead features do.
/// </summary>
public sealed class GodotActorPresenter : IGodotWorldPublicBatchFilter, IDisposable
{
    public const ulong ControlledNameplateGraceMilliseconds = 2_500;
    public const ulong PrimaryCueDurationMilliseconds = 240;
    public const ulong InteractCueDurationMilliseconds = 220;
    public const ulong LandingCueDurationMilliseconds = 140;
    public const ulong MotionHoldMilliseconds = 180;
    private const int MotionThresholdMillimeters = 4;
    private const float MillimetersToMeters = 0.001f;
    private const float MainHandBladeWidthMeters = 0.055f;
    private const float MainHandBladeLengthMeters = 0.75f;
    private const float MainHandBladeThicknessMeters = 0.018f;
    private const float MainHandGuardWidthMeters = 0.20f;
    private const float MainHandGuardLengthMeters = 0.025f;
    private const float MainHandGuardThicknessMeters = 0.04f;
    private const float MainHandGripWidthMeters = 0.034f;
    private const float MainHandGripLengthMeters = 0.13f;
    private const float MainHandGripThicknessMeters = 0.032f;
    private const float MainHandGuardCenterMeters = 0.075f;
    private const float MainHandBladeBaseMeters = 0.095f;
    private readonly ClientActorDefinitionCatalog _catalog;
    private readonly IGodotActorVisualResolver? _visuals;
    private readonly ActorSlot[] _slots;
    private readonly GodotPrimitiveActorBatches _batches;
    private readonly ClientActorRepresentationSelector _selector;
    private readonly ClientActorRepresentationCandidate[] _tierCandidates;
    private readonly ClientActorRepresentationTier[] _tierResults;
    private readonly int[] _tierOwners;
    private ulong _promotions, _demotions;
    private readonly int _ownerThreadId = System.Environment.CurrentManagedThreadId;
    private bool _revealHeld;
    private WorldPublicEntityId _controlledActor;
    private WorldPosition _controlledPosition;
    private WorldYaw _controlledOrientation;
    private bool _controlledPoseAvailable;
    private ulong _controlledNameplateExpiresAtMilliseconds;
    private ulong _currentTimeMilliseconds;
    private WorldPosition _origin;
    private int _active;
    private int _primitive;
    private int _animated;
    private int _equippedMainHands;
    private ulong _added;
    private ulong _updated;
    private ulong _removed;
    private ulong _primaryCuesPresented;
    private ulong _interactCuesPresented;
    private ulong _landingCuesPresented;
    private ulong _visualReplacements;
    private bool _disposed;

    public GodotActorPresenter(
        Node3D host,
        ClientActorDefinitionCatalog catalog,
        int maximumActors = 256,
        IGodotActorVisualResolver? visuals = null,
        ClientActorRepresentationPolicy? representationPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        if (maximumActors is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maximumActors));
        _visuals = visuals;
        _slots = new ActorSlot[maximumActors];
        _selector = new(maximumActors, representationPolicy ?? ClientActorRepresentationPolicy.ConceptDemo);
        _tierCandidates = new ClientActorRepresentationCandidate[maximumActors];
        _tierResults = new ClientActorRepresentationTier[maximumActors];
        _tierOwners = new int[maximumActors];
        Root = new Node3D { Name = "Actors" };
        host.AddChild(Root);
        _batches = new(Root, maximumActors);
    }

    public Node3D Root { get; }
    public ClientActorRepresentationPolicy RepresentationPolicy => _selector.Policy;

    public bool ShouldBatch(in WorldPublicRecord record) =>
        !_catalog.TryGet(record.PrefabId, out _) &&
        (record.PrefabId != UltraSim.Game.Composition.GameWorldPublicPrefabIds.IronSword ||
         (record.StateFlags & WorldPublicStateFlags.GroundItem) != 0);

    /// <summary>
    /// Supplies the fresh owner-private pose for the locally controlled actor. Its independently
    /// delivered public record may legitimately trail that pose; using the public record for the
    /// controlled root would draw the actor behind the player-relative world origin and then snap
    /// it forward whenever public delivery catches up.
    /// </summary>
    public void SetControlledActorPose(
        WorldPublicEntityId id,
        in WorldPosition position,
        WorldYaw orientation,
        ulong nowMilliseconds)
    {
        EnsureOperational();
        if (!id.IsValid) throw new ArgumentOutOfRangeException(nameof(id));
        int slot = Find(id);
        if (slot >= 0) EnsureDetailed(slot); // prepare before changing the controlled lifetime
        bool newControlLifetime = !_controlledPoseAvailable || id != _controlledActor;
        _controlledActor = id;
        _controlledPosition = position;
        _controlledOrientation = orientation;
        _controlledPoseAvailable = true;
        _currentTimeMilliseconds = nowMilliseconds;
        if (newControlLifetime)
            _controlledNameplateExpiresAtMilliseconds = nowMilliseconds <=
                ulong.MaxValue - ControlledNameplateGraceMilliseconds
                    ? nowMilliseconds + ControlledNameplateGraceMilliseconds
                    : ulong.MaxValue;
        if (slot >= 0)
        {
            ObserveMotion(
                ref _slots[slot], position, _slots[slot].Record.VerticalPhase, nowMilliseconds);
        }
        RefreshNameplates();
    }

    /// <summary>Temporal pose only; the slot's semantic record remains authoritative.</summary>
    public bool SetPresentedPose(WorldPublicEntityId id, in ClientMotionPose pose)
    {
        EnsureOperational();
        if (!pose.TryGetWorldPosition(out WorldPosition position)) return false;
        int slot = Find(id);
        if (slot < 0 || _controlledPoseAvailable && id == _controlledActor) return false;
        _slots[slot].PresentedPosition = position;
        _slots[slot].PresentedOrientation = pose.Orientation;
        _slots[slot].HasPresentedPose = true;
        return true;
    }

    public void SetRevealHeld(bool held)
    {
        EnsureOperational();
        if (_revealHeld == held) return;
        _revealHeld = held;
        RefreshNameplates();
    }

    public void Apply(
        ReadOnlySpan<ClientActorPresentationChange> changes,
        in WorldPosition origin,
        ulong nowMilliseconds)
    {
        EnsureOperational();
        _origin = origin;
        _currentTimeMilliseconds = nowMilliseconds;
        foreach (ref readonly ClientActorPresentationChange change in changes)
        {
            switch (change.Kind)
            {
                case ClientActorPresentationChangeKind.Added:
                    ClientActorPresentationRecord added = change.Current;
                    Add(in added);
                    break;
                case ClientActorPresentationChangeKind.Updated:
                    ClientActorPresentationRecord updated = change.Current;
                    Update(in updated);
                    break;
                case ClientActorPresentationChangeKind.Removed:
                    Remove(change.Id);
                    break;
                default:
                    throw new InvalidOperationException("The actor presentation change is invalid.");
            }
        }
        UpdateTransformsAndLifetimes(nowMilliseconds);
    }

    public void UpdateOriginAndLifetimes(in WorldPosition origin, ulong nowMilliseconds)
    {
        EnsureOperational();
        _origin = origin;
        _currentTimeMilliseconds = nowMilliseconds;
        UpdateTransformsAndLifetimes(nowMilliseconds);
    }

    /// <summary>
    /// Re-resolves visual children without replacing actor roots or presentation lifetimes. This
    /// is the only path by which a primitive/live visual can be upgraded after catalog realization.
    /// </summary>
    public int RefreshResolvedVisuals()
    {
        EnsureOperational();
        if (_visuals is null) return 0;
        int replaced = 0;
        for (int index = 0; index < _slots.Length; index++)
        {
            ref ActorSlot slot = ref _slots[index];
            if (!slot.Used || slot.Tier != ClientActorRepresentationTier.Detailed) continue;
            ClientActorDefinition definition = slot.Record.Definition;
            Node3D? visual = _visuals.Instantiate(in definition);
            bool primitive = visual is null;
            if (primitive && slot.PrimitiveFallback) continue;
            visual ??= CreatePrimitive(definition.VisualKind);
            ReplaceVisual(index, ref slot, visual, primitive);
            replaced++;
            SaturatingIncrement(ref _visualReplacements);
        }
        return replaced;
    }

    public bool TryPresentOverhead(
        in ClientChatOccurrence occurrence,
        ulong currentServerGenerationId,
        ulong expiresAtMilliseconds)
    {
        EnsureOperational();
        if (!occurrence.CanPresentOverhead(currentServerGenerationId) ||
            expiresAtMilliseconds == 0 || Find(occurrence.PublicAnchor) < 0)
            return false;
        int index = Find(occurrence.PublicAnchor);
        EnsureDetailed(index);
        ref ActorSlot slot = ref _slots[index];
        slot.Overhead!.Text = occurrence.Text;
        slot.Overhead.Visible = true;
        slot.OverheadExpiresAtMilliseconds = expiresAtMilliseconds;
        return true;
    }

    /// <summary>
    /// Presents one bounded primitive swing cue only after an authoritative Primary result. This
    /// is minimum feedback, not weapon authority or a substitute for future content animation.
    /// </summary>
    public bool TryPresentPrimaryAction(
        WorldPublicEntityId actor,
        ulong commandSequence,
        ulong nowMilliseconds)
    {
        EnsureOperational();
        if (!actor.IsValid || commandSequence == 0 || nowMilliseconds == 0) return false;
        int index = Find(actor);
        if (index < 0) return false;
        ref ActorSlot slot = ref _slots[index];
        if (commandSequence <= slot.LastActionSequence) return false;
        EnsureDetailed(index);
        slot.LastActionSequence = commandSequence;
        return PresentAction(
            ref slot, ClientPublicActorActionKind.Primary, commandSequence, nowMilliseconds);
    }

    /// <summary>Detailed-only native feature/diagnostic access. Never required for identity or pose.</summary>
    public bool TryGetActorRoot(WorldPublicEntityId id, out Node3D? root)
    {
        EnsureOperational();
        int slot = Find(id);
        root = slot >= 0 ? _slots[slot].Root : null;
        return root is not null;
    }

    public bool TryGetRepresentationTier(WorldPublicEntityId id, out ClientActorRepresentationTier tier)
    {
        EnsureOperational(); int index = Find(id);
        tier = index < 0 ? default : _slots[index].Tier;
        return index >= 0;
    }

    public bool TryGetCheapActorDiagnostics(WorldPublicEntityId id, out GodotCheapActorDiagnostics diagnostics)
    {
        EnsureOperational(); int index = Find(id);
        diagnostics = default;
        return index >= 0 && _batches.TryGet(index, out diagnostics);
    }

    /// <summary>Diagnostic copy of the retained semantic input, never changed by tier/temporal selection.</summary>
    public bool TryGetActorRecord(WorldPublicEntityId id, out ClientActorPresentationRecord record)
    {
        EnsureOperational(); int index = Find(id);
        record = index < 0 ? default : _slots[index].Record;
        return index >= 0;
    }

    /// <summary>
    /// The pose currently assigned to this native actor, separate from its exact semantic row.
    /// Pointer nomination may use this geometry, but Game still authorizes the selected identity.
    /// </summary>
    public bool TryGetPresentedPose(WorldPublicEntityId id, out ClientMotionPose pose)
    {
        EnsureOperational();
        int index = Find(id);
        if (index < 0)
        {
            pose = default;
            return false;
        }
        ref readonly ActorSlot slot = ref _slots[index];
        bool controlled = _controlledPoseAvailable && id == _controlledActor;
        WorldPosition position = controlled ? _controlledPosition :
            slot.HasPresentedPose ? slot.PresentedPosition : slot.Record.Position;
        WorldYaw orientation = controlled ? _controlledOrientation :
            slot.HasPresentedPose ? slot.PresentedOrientation : slot.Record.Orientation;
        pose = ClientMotionPose.FromAuthority(position, orientation);
        return true;
    }

    public bool TryGetActorDiagnostics(
        WorldPublicEntityId id,
        out GodotActorPresentationDiagnostics diagnostics)
    {
        EnsureOperational();
        int index = Find(id);
        if (index < 0)
        {
            diagnostics = default;
            return false;
        }
        ref readonly ActorSlot slot = ref _slots[index];
        diagnostics = new GodotActorPresentationDiagnostics(
            slot.MotionPhase,
            slot.AnimationPlayer is not null,
            slot.PrimitiveFallback,
            slot.MainHandPrefabId != 0 && slot.PrimaryCue?.Visible == true,
            slot.PrimaryCueStartedAtMilliseconds != 0,
            slot.InteractCue?.Visible == true);
        return true;
    }

    public GodotActorPresenterSnapshot GetSnapshot()
    {
        EnsureOperational();
        int nameplates = 0;
        int overhead = 0;
        int primaryCues = 0;
        int interactCues = 0, nodes = 1 + _batches.GroupCount, bodyNodes = _batches.GroupCount;
        for (int index = 0; index < _slots.Length; index++)
        {
            if (!_slots[index].Used) continue;
            if (_slots[index].Nameplate?.Visible == true) nameplates++;
            if (_slots[index].Overhead?.Visible == true) overhead++;
            if (_slots[index].PrimaryCueStartedAtMilliseconds != 0) primaryCues++;
            if (_slots[index].InteractCue?.Visible == true) interactCues++;
            nodes += _slots[index].NativeNodes;
            bodyNodes += _slots[index].NativeBodyNodes;
            if (_slots[index].Tier == ClientActorRepresentationTier.BatchedPrimitive &&
                _slots[index].Nameplate is not null) nodes++;
        }
        return new GodotActorPresenterSnapshot(
            _active, _primitive, _animated, _equippedMainHands,
            nameplates, overhead, primaryCues, interactCues, _revealHeld,
            _added, _updated, _removed, _primaryCuesPresented,
            _interactCuesPresented, _landingCuesPresented, _visualReplacements)
        {
            DetailedActors = _active - _batches.Count, BatchedPrimitiveActors = _batches.Count,
            CheapBatchGroups = _batches.GroupCount, CheapBatchRows = _batches.Count,
            NativeBodyNodes = bodyNodes, NativePresentationNodes = nodes,
            TierPromotions = _promotions, TierDemotions = _demotions,
            CheapTransformWrites = _batches.TransformWrites, CheapBoundsWrites = _batches.BoundsWrites,
            CheapOwnedResources = _batches.GroupCount * 3,
            CheapReservedPayloadBytes = _batches.ReservedPayloadBytes,
            RepresentationReservedPayloadBytes = (long)_slots.Length *
                (System.Runtime.CompilerServices.Unsafe.SizeOf<ClientActorRepresentationCandidate>() +
                 sizeof(byte) + sizeof(int) * 2 + sizeof(double)),
        };
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        for (int index = 0; index < _slots.Length; index++)
            if (_slots[index].Used) Release(index);
        _batches.Dispose();
        if (GodotObject.IsInstanceValid(Root))
        {
            Root.GetParent()?.RemoveChild(Root);
            Root.QueueFree();
        }
    }

    private void Add(in ClientActorPresentationRecord actor)
    {
        if (Find(actor.Id) >= 0) throw new InvalidOperationException("An actor presentation was added twice.");
        int index = FindFree();
        if (index < 0) throw new InvalidOperationException("The native actor presentation is full.");
        var retained = new ActorSlot
        {
            Used = true, Record = actor, Tier = ClientActorRepresentationTier.Detailed,
            LastActionSequence = actor.ActionSequence, LastVerticalPhase = actor.VerticalPhase,
            MotionPhase = ToMotionPhase(actor.VerticalPhase),
        };
        bool mandatory = _controlledPoseAvailable && actor.Id == _controlledActor || actor.MainHandPrefabId != 0;
        ClientMotionPose pose = ClientMotionPose.FromAuthority(actor.Position, actor.Orientation);
        ClientMotionPose viewer = ClientMotionPose.FromAuthority(
            _controlledPoseAvailable ? _controlledPosition : _origin, WorldYaw.Zero);
        if (_selector.Policy.Enabled && !mandatory &&
            ClientMotionPose.SpatialDistanceMillimeters(pose, viewer) > _selector.Policy.DemotionDistanceMillimeters)
        {
            _batches.Add(index, actor.Id, actor.Definition, ActorTransform(pose));
            retained.Tier = ClientActorRepresentationTier.BatchedPrimitive;
            _slots[index] = retained;
        }
        else
        {
            _slots[index] = PrepareDetailed(in retained);
            CountDetailed(in _slots[index], 1);
            _slots[index].Root!.Visible = true;
        }
        _active++; SaturatingIncrement(ref _added);
        ApplySlot(index, 0);
    }

    // Hidden candidate construction cannot expose duplicate bodies at a native boundary.
    // Only successful construction is committed; a failure leaves the old cheap identity intact.
    private ActorSlot PrepareDetailed(in ActorSlot retained)
    {
        ClientActorPresentationRecord actor = retained.Record;
        Node3D root = new() { Name = $"Actor_{actor.Id.Index}_{actor.Id.Generation}", Visible = false };
        try
        {
            ClientActorDefinition definition = actor.Definition;
            Node3D? visual = _visuals?.Instantiate(in definition);
            bool primitive = visual is null;
            visual ??= CreatePrimitive(actor.Definition.VisualKind);
            visual.Name = "Visual";
            float scale = actor.Definition.VisualScalePermille / 1_000f;
            visual.Scale *= new Vector3(scale, scale, scale);
            root.AddChild(visual);
            AnimationPlayer? animations = FindFirst<AnimationPlayer>(visual);
            if (animations is not null)
            {
                ConfigureAuthoritativeRootMotion(visual, animations);
                ConfigureAnimationLoops(animations);
            }
            var nameplate = new Label3D
            {
                Name = "Nameplate",
                Text = actor.DisplayLabel,
                Position = new Vector3(
                    0, actor.Definition.NameplateHeightMillimeters * MillimetersToMeters, 0),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FontSize = 32,
            };
            root.AddChild(nameplate);
            var effectsAnchor = new Marker3D
            {
                Name = "EffectsAnchor",
                Position = new Vector3(
                    0, actor.Definition.EffectsHeightMillimeters * MillimetersToMeters, 0),
            };
            root.AddChild(effectsAnchor);
            var overhead = new Label3D
            {
                Name = "OverheadSpeech",
                Position = new Vector3(
                    0, (actor.Definition.NameplateHeightMillimeters + 400) * MillimetersToMeters, 0),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FontSize = 28,
                Visible = false,
                Modulate = new Color(1f, 1f, 0.86f),
            };
            root.AddChild(overhead);
            Node3D mainHandSocket = CreateMainHandSocket(
                root, visual, out Skeleton3D? mainHandSkeleton, out int mainHandBone);
            Node3D primaryCue = CreateMainHandPresentation(out Node3D primaryActionPivot);
            mainHandSocket.AddChild(primaryCue);
            Node3D interactCue = CreateInteractCue();
            effectsAnchor.AddChild(interactCue);
            Root.AddChild(root);
            SynchronizeMainHandSocketPosition(
                root, mainHandSocket, mainHandSkeleton, mainHandBone);
            NormalizeMainHandPresentationScale(mainHandSocket, primaryCue);
            ActorSlot slot = retained;
            slot.Tier = ClientActorRepresentationTier.Detailed;
            slot.Root = root; slot.Visual = visual; slot.BaseVisualScale = visual.Scale;
            slot.Nameplate = nameplate; slot.Overhead = overhead;
            slot.PrimaryCue = primaryCue; slot.PrimaryActionPivot = primaryActionPivot;
            slot.MainHandSocket = mainHandSocket; slot.MainHandSkeleton = mainHandSkeleton;
            slot.MainHandBone = mainHandBone; slot.InteractCue = interactCue;
            slot.AnimationPlayer = animations; slot.PrimitiveFallback = primitive;
            slot.PlayedMotionPhase = (GodotActorMotionPhase)byte.MaxValue;
            slot.MainHandPrefabId = actor.MainHandPrefabId;
            primaryCue.Visible = actor.MainHandPrefabId != 0 || slot.PrimaryCueStartedAtMilliseconds != 0;
            PlayMotionAnimation(ref slot, slot.MotionPhase, force: true);
            slot.NativeNodes = CountNative(root, includeLabels: true);
            slot.NativeBodyNodes = CountNative(root, includeLabels: false);
            return slot;
        }
        catch { if (GodotObject.IsInstanceValid(root)) root.Free(); throw; }
    }

    private void ReplaceVisual(int index, ref ActorSlot slot, Node3D visual, bool primitive)
    {
        Node3D root = slot.Root ?? throw new InvalidOperationException("The actor root is absent.");
        if (slot.PrimitiveFallback) _primitive--;
        if (slot.AnimationPlayer is not null) _animated--;

        Node3D? oldVisual = slot.Visual;
        Node3D? oldSocket = slot.MainHandSocket;
        if (oldSocket is not null && oldSocket.GetParent() == root)
        {
            root.RemoveChild(oldSocket);
            oldSocket.Free();
        }
        if (oldVisual is not null && GodotObject.IsInstanceValid(oldVisual))
        {
            oldVisual.GetParent()?.RemoveChild(oldVisual);
            oldVisual.Free();
        }

        visual.Name = "Visual";
        float scale = slot.Record.Definition.VisualScalePermille / 1_000f;
        visual.Scale *= new Vector3(scale, scale, scale);
        root.AddChild(visual);
        AnimationPlayer? animations = FindFirst<AnimationPlayer>(visual);
        if (animations is not null)
        {
            ConfigureAuthoritativeRootMotion(visual, animations);
            ConfigureAnimationLoops(animations);
        }
        Node3D socket = CreateMainHandSocket(
            root, visual, out Skeleton3D? mainHandSkeleton, out int mainHandBone);
        Node3D primaryCue = CreateMainHandPresentation(out Node3D primaryActionPivot);
        socket.AddChild(primaryCue);
        SynchronizeMainHandSocketPosition(root, socket, mainHandSkeleton, mainHandBone);
        NormalizeMainHandPresentationScale(socket, primaryCue);

        slot.Visual = visual;
        slot.BaseVisualScale = visual.Scale;
        slot.AnimationPlayer = animations;
        slot.MainHandSocket = socket;
        slot.PrimaryCue = primaryCue;
        slot.PrimaryActionPivot = primaryActionPivot;
        slot.MainHandSkeleton = mainHandSkeleton;
        slot.MainHandBone = mainHandBone;
        slot.PrimitiveFallback = primitive;
        if (primitive) _primitive++;
        slot.PlayedMotionPhase = (GodotActorMotionPhase)byte.MaxValue;
        if (animations is not null) _animated++;
        primaryCue.Visible = slot.MainHandPrefabId != 0 ||
            slot.PrimaryCueStartedAtMilliseconds != 0;
        PlayMotionAnimation(ref slot, slot.MotionPhase, force: true);
        slot.NativeNodes = CountNative(root, includeLabels: true);
        slot.NativeBodyNodes = CountNative(root, includeLabels: false);
        ApplySlot(index, _currentTimeMilliseconds);
    }

    private void Update(in ClientActorPresentationRecord actor)
    {
        int index = Find(actor.Id);
        if (index < 0)
            throw new InvalidOperationException("An unknown actor presentation was updated.");
        ref ActorSlot slot = ref _slots[index];
        ClientActorPresentationRecord previous = slot.Record;
        if (slot.Record.Definition.VisualKind != actor.Definition.VisualKind ||
            slot.Record.Definition.VisualScalePermille != actor.Definition.VisualScalePermille)
        {
            // Keep identity, temporal pose, sequence/expiry state even when the visual family changes.
            if (slot.Tier == ClientActorRepresentationTier.BatchedPrimitive) _batches.Remove(index);
            slot.Record = actor;
            if (slot.Tier == ClientActorRepresentationTier.BatchedPrimitive)
                _batches.Add(index, actor.Id, actor.Definition, ActorTransform(PresentedPose(in slot)));
            else
            {
                Node3D? visual = _visuals?.Instantiate(actor.Definition);
                bool primitive = visual is null;
                ReplaceVisual(index, ref slot, visual ?? CreatePrimitive(actor.Definition.VisualKind), primitive);
            }
        }
        slot.Record = actor;
        if (slot.Nameplate is not null) slot.Nameplate.Text = actor.DisplayLabel;
        if (slot.Tier == ClientActorRepresentationTier.BatchedPrimitive &&
            (actor.MainHandPrefabId != 0 || actor.ActionSequence > slot.LastActionSequence))
            EnsureDetailed(index);
        if (slot.Nameplate is not null) slot.Nameplate.Position = new Vector3(
            0, actor.Definition.NameplateHeightMillimeters * MillimetersToMeters, 0);
        if (slot.Overhead is not null) slot.Overhead.Position = new Vector3(
            0, (actor.Definition.NameplateHeightMillimeters + 400) * MillimetersToMeters, 0);
        if (previous.MainHandPrefabId != actor.MainHandPrefabId)
            SynchronizeMainHand(ref slot, actor.MainHandPrefabId);
        if (actor.ActionSequence > slot.LastActionSequence)
        {
            slot.LastActionSequence = actor.ActionSequence;
            _ = PresentAction(ref slot, actor.Action, actor.ActionSequence, _currentTimeMilliseconds);
        }
        SaturatingIncrement(ref _updated);
        ApplySlot(index, nowMilliseconds: 0);
    }

    private void Remove(WorldPublicEntityId id)
    {
        int index = Find(id);
        if (index < 0) return;
        Release(index);
        SaturatingIncrement(ref _removed);
    }

    private void Release(int index)
    {
        ref ActorSlot slot = ref _slots[index];
        if (!slot.Used) return;
        if (slot.Tier == ClientActorRepresentationTier.BatchedPrimitive)
        {
            _batches.Remove(index); slot.Nameplate?.Free();
        }
        else RetireDetailed(ref slot);
        _active--;
        slot = default;
    }

    private void UpdateTransformsAndLifetimes(ulong nowMilliseconds)
    {
        // F1.8A has already selected poses. Motion/cue eligibility is observed before tier selection.
        int count = 0;
        for (int index = 0; index < _slots.Length; index++)
        {
            ref ActorSlot slot = ref _slots[index];
            if (!slot.Used) continue;
            ClientMotionPose pose = PresentedPose(in slot);
            if (nowMilliseconds != 0 && pose.TryGetWorldPosition(out WorldPosition position))
                ObserveMotion(ref slot, position, slot.Record.VerticalPhase, nowMilliseconds);
            bool mandatory = _controlledPoseAvailable && slot.Record.Id == _controlledActor ||
                slot.Record.MainHandPrefabId != 0 ||
                IsCueActive(slot.PrimaryCueStartedAtMilliseconds, PrimaryCueDurationMilliseconds) ||
                IsCueActive(slot.InteractCueStartedAtMilliseconds, InteractCueDurationMilliseconds) ||
                IsCueActive(slot.LandingCueStartedAtMilliseconds, LandingCueDurationMilliseconds) ||
                slot.OverheadExpiresAtMilliseconds > _currentTimeMilliseconds;
            _tierOwners[count] = index;
            _tierCandidates[count++] = new(slot.Record.Id, pose, slot.Tier, mandatory);
        }
        ClientMotionPose viewer = ClientMotionPose.FromAuthority(
            _controlledPoseAvailable ? _controlledPosition : _origin, WorldYaw.Zero);
        _selector.Select(_tierCandidates.AsSpan(0, count), viewer, _tierResults);
        for (int i = 0; i < count; i++)
        {
            int index = _tierOwners[i];
            if (_tierResults[i] == ClientActorRepresentationTier.Detailed) EnsureDetailed(index);
            else EnsureCheap(index);
        }
        for (int index = 0; index < _slots.Length; index++)
            if (_slots[index].Used) ApplySlot(index, nowMilliseconds);
        _batches.UpdateBounds();
    }

    private bool IsCueActive(ulong started, ulong duration) => started != 0 &&
        _currentTimeMilliseconds >= started && _currentTimeMilliseconds - started < duration;

    private ClientMotionPose PresentedPose(in ActorSlot slot) =>
        ClientMotionPose.FromAuthority(_controlledPoseAvailable && slot.Record.Id == _controlledActor
                ? _controlledPosition : slot.HasPresentedPose ? slot.PresentedPosition : slot.Record.Position,
            _controlledPoseAvailable && slot.Record.Id == _controlledActor ? _controlledOrientation :
                slot.HasPresentedPose ? slot.PresentedOrientation : slot.Record.Orientation);

    private Transform3D ActorTransform(in ClientMotionPose pose) => new(
        new Basis(Vector3.Up, pose.Orientation.Units * (MathF.Tau / WorldYaw.UnitsPerTurn)),
        new Vector3(checked(pose.GlobalXMillimeters - _origin.GlobalXMillimeters) * MillimetersToMeters,
            checked(pose.YMillimeters - _origin.YMillimeters) * MillimetersToMeters,
            checked(pose.GlobalZMillimeters - _origin.GlobalZMillimeters) * MillimetersToMeters));

    private void CountDetailed(in ActorSlot slot, int direction)
    {
        if (slot.PrimitiveFallback) _primitive += direction;
        if (slot.AnimationPlayer is not null) _animated += direction;
        if (slot.MainHandPrefabId != 0) _equippedMainHands += direction;
    }

    private void RetireDetailed(ref ActorSlot slot)
    {
        CountDetailed(in slot, -1);
        slot.Root?.Free();
        slot.Root = slot.Visual = slot.PrimaryCue = slot.PrimaryActionPivot = slot.MainHandSocket =
            slot.InteractCue = null;
        slot.Nameplate = slot.Overhead = null; slot.AnimationPlayer = null; slot.MainHandSkeleton = null;
        slot.NativeNodes = slot.NativeBodyNodes = 0; slot.PrimitiveFallback = false;
    }

    private void EnsureDetailed(int index)
    {
        ref ActorSlot slot = ref _slots[index];
        if (slot.Tier == ClientActorRepresentationTier.Detailed) return;
        ActorSlot candidate = PrepareDetailed(in slot);
        Label3D? oldNameplate = slot.Nameplate;
        _batches.Remove(index); oldNameplate?.Free();
        slot = candidate; CountDetailed(in slot, 1);
        slot.Root!.Transform = ActorTransform(PresentedPose(in slot));
        slot.Root.Visible = true;
        SaturatingIncrement(ref _promotions);
    }

    private void EnsureCheap(int index)
    {
        ref ActorSlot slot = ref _slots[index];
        if (slot.Tier == ClientActorRepresentationTier.BatchedPrimitive) return;
        _batches.Add(index, slot.Record.Id, slot.Record.Definition, ActorTransform(PresentedPose(in slot)));
        RetireDetailed(ref slot); slot.Tier = ClientActorRepresentationTier.BatchedPrimitive;
        SaturatingIncrement(ref _demotions);
    }

    private static int CountNative(Node node, bool includeLabels)
    {
        int count = includeLabels || node is not Label3D ? 1 : 0;
        foreach (Node child in node.GetChildren()) count += CountNative(child, includeLabels);
        return count; // cold realization/visual replacement only, never a frame tree census
    }

    private void ApplySlot(int index, ulong nowMilliseconds)
    {
        ref ActorSlot slot = ref _slots[index];
        ClientActorPresentationRecord actor = slot.Record;
        bool controlled = _controlledPoseAvailable && actor.Id == _controlledActor;
        WorldPosition position = controlled ? _controlledPosition :
            slot.HasPresentedPose ? slot.PresentedPosition : actor.Position;
        WorldYaw orientation = controlled ? _controlledOrientation :
            slot.HasPresentedPose ? slot.PresentedOrientation : actor.Orientation;
        bool controlledNameGrace = controlled &&
            _currentTimeMilliseconds < _controlledNameplateExpiresAtMilliseconds;
        bool showName = actor.NameplateEligible && (_revealHeld || controlledNameGrace);
        if (slot.Tier == ClientActorRepresentationTier.BatchedPrimitive)
        {
            _batches.SetTransform(index, actor.Definition, ActorTransform(PresentedPose(in slot)));
            if (showName && slot.Nameplate is null)
            {
                slot.Nameplate = new Label3D { Name = "CheapActorNameplate", Text = actor.DisplayLabel,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 32 };
                Root.AddChild(slot.Nameplate);
            }
            if (!showName && slot.Nameplate is not null) { slot.Nameplate.Free(); slot.Nameplate = null; }
            if (slot.Nameplate is not null)
                slot.Nameplate.Position = ActorTransform(PresentedPose(in slot)).Origin +
                    Vector3.Up * actor.Definition.NameplateHeightMillimeters * MillimetersToMeters;
            // Expired feature clocks do not become new obligations after a later promotion.
            if (!IsCueActive(slot.PrimaryCueStartedAtMilliseconds, PrimaryCueDurationMilliseconds)) slot.PrimaryCueStartedAtMilliseconds = 0;
            if (!IsCueActive(slot.InteractCueStartedAtMilliseconds, InteractCueDurationMilliseconds)) slot.InteractCueStartedAtMilliseconds = 0;
            if (!IsCueActive(slot.LandingCueStartedAtMilliseconds, LandingCueDurationMilliseconds)) slot.LandingCueStartedAtMilliseconds = 0;
            if (slot.OverheadExpiresAtMilliseconds <= _currentTimeMilliseconds) slot.OverheadExpiresAtMilliseconds = 0;
            return;
        }
        slot.Root!.Position = new Vector3(
            checked(position.GlobalXMillimeters - _origin.GlobalXMillimeters) *
                MillimetersToMeters,
            checked(position.YMillimeters - _origin.YMillimeters) * MillimetersToMeters,
            checked(position.GlobalZMillimeters - _origin.GlobalZMillimeters) *
                MillimetersToMeters);
        slot.Root.Rotation = new Vector3(
            0, orientation.Units * (MathF.Tau / WorldYaw.UnitsPerTurn), 0);
        SynchronizeMainHandSocketPosition(ref slot);
        slot.Nameplate!.Visible = showName;
        if (nowMilliseconds != 0 && slot.OverheadExpiresAtMilliseconds != 0 &&
            nowMilliseconds >= slot.OverheadExpiresAtMilliseconds)
        {
            slot.Overhead!.Visible = false;
            slot.Overhead.Text = string.Empty;
            slot.OverheadExpiresAtMilliseconds = 0;
        }
        if (nowMilliseconds != 0)
        {
            ApplyPrimaryCue(ref slot, nowMilliseconds);
            ApplyInteractCue(ref slot, nowMilliseconds);
            ApplyLandingCue(ref slot, nowMilliseconds);
            ApplyPrimitiveMotion(ref slot, nowMilliseconds);
        }
    }

    private static void ApplyPrimaryCue(ref ActorSlot slot, ulong nowMilliseconds)
    {
        if (slot.PrimaryCue is null || slot.PrimaryActionPivot is null ||
            slot.PrimaryCueStartedAtMilliseconds == 0) return;
        ulong elapsed = nowMilliseconds >= slot.PrimaryCueStartedAtMilliseconds
            ? nowMilliseconds - slot.PrimaryCueStartedAtMilliseconds
            : ulong.MaxValue;
        if (elapsed >= PrimaryCueDurationMilliseconds)
        {
            slot.PrimaryCueStartedAtMilliseconds = 0;
            slot.PrimaryActionPivot.Rotation = Vector3.Zero;
            slot.PrimaryCue.Visible = slot.MainHandPrefabId != 0;
            return;
        }
        slot.PrimaryCue.Visible = true;
        float progress = elapsed / (float)PrimaryCueDurationMilliseconds;
        slot.PrimaryActionPivot.Rotation = new Vector3(
            -0.35f, Mathf.Lerp(-1.2f, 1.2f, progress), 0.25f);
    }

    private bool PresentAction(
        ref ActorSlot slot,
        ClientPublicActorActionKind action,
        ulong actionSequence,
        ulong nowMilliseconds)
    {
        if (actionSequence == 0 || nowMilliseconds == 0) return false;
        switch (action)
        {
            case ClientPublicActorActionKind.Primary:
                slot.PrimaryCueStartedAtMilliseconds = nowMilliseconds;
                ApplyPrimaryCue(ref slot, nowMilliseconds);
                SaturatingIncrement(ref _primaryCuesPresented);
                return true;
            case ClientPublicActorActionKind.Interact:
                slot.InteractCueStartedAtMilliseconds = nowMilliseconds;
                slot.InteractCue!.Visible = true;
                ApplyInteractCue(ref slot, nowMilliseconds);
                SaturatingIncrement(ref _interactCuesPresented);
                return true;
            default:
                return false;
        }
    }

    private static void ApplyInteractCue(ref ActorSlot slot, ulong nowMilliseconds)
    {
        if (slot.InteractCue?.Visible != true || slot.InteractCueStartedAtMilliseconds == 0) return;
        ulong elapsed = nowMilliseconds >= slot.InteractCueStartedAtMilliseconds
            ? nowMilliseconds - slot.InteractCueStartedAtMilliseconds
            : ulong.MaxValue;
        if (elapsed >= InteractCueDurationMilliseconds)
        {
            slot.InteractCue.Visible = false;
            slot.InteractCue.Scale = Vector3.One;
            slot.InteractCueStartedAtMilliseconds = 0;
            return;
        }
        float pulse = 0.75f + 0.45f * MathF.Sin(
            elapsed / (float)InteractCueDurationMilliseconds * MathF.PI);
        slot.InteractCue.Scale = new Vector3(pulse, pulse, pulse);
    }

    private static void ApplyLandingCue(ref ActorSlot slot, ulong nowMilliseconds)
    {
        if (slot.Visual is null || slot.LandingCueStartedAtMilliseconds == 0) return;
        ulong elapsed = nowMilliseconds >= slot.LandingCueStartedAtMilliseconds
            ? nowMilliseconds - slot.LandingCueStartedAtMilliseconds
            : ulong.MaxValue;
        if (elapsed >= LandingCueDurationMilliseconds)
        {
            slot.Visual.Scale = slot.BaseVisualScale;
            slot.LandingCueStartedAtMilliseconds = 0;
            return;
        }
        float progress = elapsed / (float)LandingCueDurationMilliseconds;
        float squash = 0.92f + 0.08f * progress;
        slot.Visual.Scale = new Vector3(
            slot.BaseVisualScale.X / MathF.Sqrt(squash),
            slot.BaseVisualScale.Y * squash,
            slot.BaseVisualScale.Z / MathF.Sqrt(squash));
    }

    private void ObserveMotion(
        ref ActorSlot slot,
        in WorldPosition position,
        ClientPublicActorVerticalPhase verticalPhase,
        ulong nowMilliseconds)
    {
        if (!slot.HasObservedPosition)
        {
            slot.HasObservedPosition = true;
            slot.ObservedPosition = position;
            slot.MotionExpiresAtMilliseconds = nowMilliseconds;
            slot.LastVerticalPhase = verticalPhase;
            SetMotionPhase(ref slot, ToMotionPhase(verticalPhase));
            return;
        }

        long deltaX = position.GlobalXMillimeters - slot.ObservedPosition.GlobalXMillimeters;
        long deltaZ = position.GlobalZMillimeters - slot.ObservedPosition.GlobalZMillimeters;
        slot.ObservedPosition = position;
        bool horizontal = Math.Abs(deltaX) >= MotionThresholdMillimeters ||
            Math.Abs(deltaZ) >= MotionThresholdMillimeters;
        if (horizontal)
            slot.MotionExpiresAtMilliseconds = AddSaturated(
                nowMilliseconds, MotionHoldMilliseconds);

        if (slot.LastVerticalPhase != verticalPhase)
        {
            if (IsAirborne(slot.LastVerticalPhase) &&
                verticalPhase == ClientPublicActorVerticalPhase.Grounded)
                StartLanding(ref slot, nowMilliseconds);
            slot.LastVerticalPhase = verticalPhase;
        }

        GodotActorMotionPhase verticalMotion = ToMotionPhase(verticalPhase);
        if (IsAirborne(verticalMotion))
        {
            SetMotionPhase(ref slot, verticalMotion);
            return;
        }
        SetMotionPhase(ref slot,
            horizontal || nowMilliseconds < slot.MotionExpiresAtMilliseconds
                ? GodotActorMotionPhase.Walk
                : GodotActorMotionPhase.Idle);
    }

    private void StartLanding(ref ActorSlot slot, ulong nowMilliseconds)
    {
        slot.LandingCueStartedAtMilliseconds = nowMilliseconds;
        SaturatingIncrement(ref _landingCuesPresented);
    }

    private static bool IsAirborne(ClientPublicActorVerticalPhase phase) =>
        phase is ClientPublicActorVerticalPhase.Rising or
            ClientPublicActorVerticalPhase.Falling;

    private static bool IsAirborne(GodotActorMotionPhase phase) =>
        phase is GodotActorMotionPhase.Rising or GodotActorMotionPhase.Falling;

    private static GodotActorMotionPhase ToMotionPhase(
        ClientPublicActorVerticalPhase phase) => phase switch
        {
            ClientPublicActorVerticalPhase.Rising => GodotActorMotionPhase.Rising,
            ClientPublicActorVerticalPhase.Falling => GodotActorMotionPhase.Falling,
            _ => GodotActorMotionPhase.Idle,
        };

    private static ulong AddSaturated(ulong value, ulong addition) =>
        value <= ulong.MaxValue - addition ? value + addition : ulong.MaxValue;

    private static void SetMotionPhase(ref ActorSlot slot, GodotActorMotionPhase phase)
    {
        if (slot.MotionPhase == phase) return;
        bool remainedAirborne = IsAirborne(slot.MotionPhase) && IsAirborne(phase);
        slot.MotionPhase = phase;
        if (!remainedAirborne) PlayMotionAnimation(ref slot, phase);
    }

    private static void PlayMotionAnimation(
        ref ActorSlot slot,
        GodotActorMotionPhase phase,
        bool force = false)
    {
        if (slot.AnimationPlayer is null || !force && slot.PlayedMotionPhase == phase) return;
        string requested = phase switch
        {
            GodotActorMotionPhase.Walk => "Walk",
            GodotActorMotionPhase.Rising or GodotActorMotionPhase.Falling => "Jump",
            _ => "Idle",
        };
        if (!TryFindAnimation(slot.AnimationPlayer, requested, out StringName animation)) return;
        slot.AnimationPlayer.Play(animation, 0.12);
        slot.PlayedMotionPhase = phase;
    }

    private static void ApplyPrimitiveMotion(ref ActorSlot slot, ulong nowMilliseconds)
    {
        if (!slot.PrimitiveFallback || slot.Visual is null) return;
        float groundedCapsuleCentre = slot.Visual is MeshInstance3D
            { Mesh: CapsuleMesh capsule } ? capsule.Height * 0.5f : 0f;
        slot.Visual.Position = slot.MotionPhase == GodotActorMotionPhase.Walk
            ? new Vector3(0, groundedCapsuleCentre +
                0.035f * MathF.Sin(nowMilliseconds * 0.024f), 0)
            : new Vector3(0, groundedCapsuleCentre, 0);
        slot.Visual.Rotation = slot.MotionPhase switch
        {
            GodotActorMotionPhase.Rising => new Vector3(-0.08f, 0, 0),
            GodotActorMotionPhase.Falling => new Vector3(0.08f, 0, 0),
            _ => Vector3.Zero,
        };
    }

    private void SynchronizeMainHand(ref ActorSlot slot, uint prefabId)
    {
        if (slot.MainHandPrefabId == prefabId) return;
        if (slot.MainHandPrefabId != 0) _equippedMainHands--;
        slot.MainHandPrefabId = prefabId;
        if (prefabId != 0) _equippedMainHands++;
        if (slot.PrimaryCue is not null)
            slot.PrimaryCue.Visible = prefabId != 0 || slot.PrimaryCueStartedAtMilliseconds != 0;
    }

    private static void ConfigureAnimationLoops(AnimationPlayer player)
    {
        foreach (string requested in new[] { "Idle", "Walk" })
        {
            if (!TryFindAnimation(player, requested, out StringName name)) continue;
            Animation? animation = player.GetAnimation(name);
            if (animation is not null) animation.LoopMode = Animation.LoopModeEnum.Linear;
        }
    }

    private static void ConfigureAuthoritativeRootMotion(
        Node3D visual,
        AnimationPlayer player)
    {
        Skeleton3D? skeleton = FindFirst<Skeleton3D>(visual);
        if (skeleton is null) return;
        int rootBone = -1;
        for (int bone = 0; bone < skeleton.GetBoneCount(); bone++)
        {
            if (skeleton.GetBoneParent(bone) != -1) continue;
            rootBone = bone;
            break;
        }
        if (rootBone < 0) return;
        string rootName = skeleton.GetBoneName(rootBone).ToString();
        foreach (StringName animationName in player.GetAnimationList())
        {
            Animation? animation = player.GetAnimation(animationName);
            if (animation is null) continue;
            for (int track = 0; track < animation.GetTrackCount(); track++)
            {
                if (animation.TrackGetType(track) != Animation.TrackType.Position3D)
                    continue;
                NodePath path = animation.TrackGetPath(track);
                if (!string.Equals(
                        path.GetConcatenatedSubNames(), rootName,
                        StringComparison.Ordinal))
                    continue;

                // Game owns the actor's complete authoritative position, including vertical
                // motion. Extracting this imported track makes Godot cancel its visual motion
                // instead of applying it on top of the authoritative actor root.
                player.RootMotionTrack = path;
                return;
            }
        }
    }

    private static bool TryFindAnimation(
        AnimationPlayer player,
        string requested,
        out StringName result)
    {
        foreach (StringName candidate in player.GetAnimationList())
        {
            string text = candidate.ToString();
            int separator = Math.Max(text.LastIndexOf('/'), text.LastIndexOf(':'));
            string leaf = separator >= 0 ? text[(separator + 1)..] : text;
            if (leaf.EndsWith("-loop", StringComparison.OrdinalIgnoreCase))
                leaf = leaf[..^5];
            if (!string.Equals(leaf, requested, StringComparison.OrdinalIgnoreCase)) continue;
            result = candidate;
            return true;
        }
        result = new StringName();
        return false;
    }

    private void RefreshNameplates()
    {
        for (int index = 0; index < _slots.Length; index++)
            if (_slots[index].Used) ApplySlot(index, nowMilliseconds: 0);
    }

    private int Find(WorldPublicEntityId id)
    {
        for (int index = 0; index < _slots.Length; index++)
            if (_slots[index].Used && _slots[index].Record.Id == id) return index;
        return -1;
    }

    private int FindFree()
    {
        for (int index = 0; index < _slots.Length; index++)
            if (!_slots[index].Used) return index;
        return -1;
    }

    private static Node3D CreatePrimitive(ClientActorVisualKind kind) =>
        GodotPrimitiveSubstrate.CreateActor(kind);

    private static Node3D CreateMainHandSocket(
        Node3D root,
        Node3D visual,
        out Skeleton3D? mainHandSkeleton,
        out int mainHandBone)
    {
        mainHandSkeleton = FindFirst<Skeleton3D>(visual);
        if (mainHandSkeleton is not null)
        {
            for (int bone = 0; bone < mainHandSkeleton.GetBoneCount(); bone++)
            {
                string candidate = mainHandSkeleton.GetBoneName(bone);
                if (!candidate.EndsWith("RightHand", StringComparison.OrdinalIgnoreCase)) continue;
                mainHandBone = bone;
                var socket = new Marker3D
                {
                    Name = "MainHandSocket",
                };
                // The scale-free hand transform is sampled explicitly below. Keeping the socket
                // under the actor root avoids inheriting the imported skeleton's 0.01 scene scale
                // while retaining the animated hand's complete position and orientation.
                root.AddChild(socket);
                return socket;
            }
        }

        mainHandSkeleton = null;
        mainHandBone = -1;
        var fallback = new Marker3D
        {
            Name = "MainHandSocket",
            Position = new Vector3(-0.34f, 1.08f, 0),
            // Match the imported hand's canonical idle frame so the same grip correction works
            // for primitive fallback actors without a skeleton.
            Basis = new Basis(Vector3.Back, Vector3.Left, Vector3.Down),
        };
        root.AddChild(fallback);
        return fallback;
    }

    private static void SynchronizeMainHandSocketPosition(ref ActorSlot slot)
    {
        if (slot.Root is null || slot.MainHandSocket is null) return;
        SynchronizeMainHandSocketPosition(
            slot.Root, slot.MainHandSocket, slot.MainHandSkeleton, slot.MainHandBone);
    }

    private static void SynchronizeMainHandSocketPosition(
        Node3D root,
        Node3D socket,
        Skeleton3D? skeleton,
        int bone)
    {
        if (skeleton is null || bone < 0) return;
        Transform3D handInRoot = root.GlobalTransform.AffineInverse() *
            skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone);
        socket.Transform = new Transform3D(
            handInRoot.Basis.Orthonormalized(),
            handInRoot.Origin);
    }

    private static Node3D CreateMainHandPresentation(out Node3D actionPivot)
    {
        var bladeMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.83f, 0.90f, 1.0f),
            Metallic = 0.75f,
            Roughness = 0.18f,
            EmissionEnabled = true,
            Emission = new Color(0.14f, 0.19f, 0.30f),
            EmissionEnergyMultiplier = 0.65f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var guardMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.48f, 0.12f),
            Metallic = 0.65f,
            Roughness = 0.28f,
        };
        var gripMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.07f, 0.035f),
            Roughness = 0.82f,
        };
        var pivot = new Node3D
        {
            Name = "MainHandPresentation",
            Visible = false,
            // Local +Y is the weapon's hilt-to-tip axis. This grip correction places the blade
            // diagonally down/back from the Mixamo RightHand frame; locomotion then carries the
            // whole correction with the animated hand instead of leaving a vertical pendulum.
            Basis = CreateMainHandGripBasis(),
        };
        actionPivot = new Node3D { Name = "MainHandActionPivot" };
        pivot.AddChild(actionPivot);
        actionPivot.AddChild(new MeshInstance3D
        {
            Name = "PrimitiveBlade",
            Mesh = GodotMeshGeometry.CreateGroundedBox(
                new Vector3(
                    MainHandBladeWidthMeters,
                    MainHandBladeLengthMeters,
                    MainHandBladeThicknessMeters), bladeMaterial),
            Position = new Vector3(0, MainHandBladeBaseMeters, 0),
        });
        actionPivot.AddChild(new MeshInstance3D
        {
            Name = "PrimitiveGuard",
            Mesh = GodotMeshGeometry.CreateGroundedBox(
                new Vector3(
                    MainHandGuardWidthMeters,
                    MainHandGuardLengthMeters,
                    MainHandGuardThicknessMeters), guardMaterial),
            Position = new Vector3(
                0, MainHandGuardCenterMeters - MainHandGuardLengthMeters * 0.5f, 0),
        });
        actionPivot.AddChild(new MeshInstance3D
        {
            Name = "PrimitiveGrip",
            Mesh = GodotMeshGeometry.CreateGroundedBox(
                new Vector3(
                    MainHandGripWidthMeters,
                    MainHandGripLengthMeters,
                    MainHandGripThicknessMeters), gripMaterial),
            // The bone socket now lands at the middle of the grip, not on the guard.
            Position = new Vector3(0, -MainHandGripLengthMeters * 0.5f, 0),
        });
        return pivot;
    }

    private static Basis CreateMainHandGripBasis()
    {
        Vector3 blade = new Vector3(-0.20f, 0.34f, 0.92f).Normalized();
        Vector3 guard = (Vector3.Up - blade * blade.Dot(Vector3.Up)).Normalized();
        return new Basis(guard, blade, guard.Cross(blade).Normalized());
    }

    private static void NormalizeMainHandPresentationScale(Node3D socket, Node3D presentation)
    {
        Vector3 inherited = socket.GlobalTransform.Basis.Scale.Abs();
        const float minimum = 0.0001f;
        if (inherited.X < minimum || inherited.Y < minimum || inherited.Z < minimum)
            throw new InvalidDataException("The MainHand socket has a degenerate inherited scale.");
        presentation.Scale = new Vector3(
            1f / inherited.X,
            1f / inherited.Y,
            1f / inherited.Z);
    }

    private static Node3D CreateInteractCue()
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.82f, 0.2f),
            EmissionEnabled = true,
            Emission = new Color(0.8f, 0.45f, 0.05f),
            EmissionEnergyMultiplier = 1.8f,
        };
        return new MeshInstance3D
        {
            Name = "AcceptedInteractCue",
            Mesh = GodotMeshGeometry.CreateGroundedBox(new Vector3(0.18f, 0.18f, 0.18f), material),
            Position = new Vector3(0, 0.12f, 0),
            Visible = false,
        };
    }

    private static T? FindFirst<T>(Node root) where T : Node
    {
        if (root is T match) return match;
        foreach (Node child in root.GetChildren())
        {
            T? nested = FindFirst<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOperational()
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwner()
    {
        if (System.Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Godot actor presentation is engine-thread-bound.");
    }

    private struct ActorSlot
    {
        internal bool Used;
        internal ClientActorRepresentationTier Tier;
        internal int NativeNodes, NativeBodyNodes;
        internal ClientActorPresentationRecord Record;
        internal bool HasPresentedPose;
        internal WorldPosition PresentedPosition;
        internal WorldYaw PresentedOrientation;
        internal Node3D? Root;
        internal Node3D? Visual;
        internal Vector3 BaseVisualScale;
        internal Label3D? Nameplate;
        internal Label3D? Overhead;
        internal Node3D? PrimaryCue;
        internal Node3D? PrimaryActionPivot;
        internal Node3D? MainHandSocket;
        internal Skeleton3D? MainHandSkeleton;
        internal int MainHandBone;
        internal Node3D? InteractCue;
        internal AnimationPlayer? AnimationPlayer;
        internal WorldPosition ObservedPosition;
        internal GodotActorMotionPhase MotionPhase;
        internal GodotActorMotionPhase PlayedMotionPhase;
        internal ClientPublicActorVerticalPhase LastVerticalPhase;
        internal ulong MotionExpiresAtMilliseconds;
        internal ulong OverheadExpiresAtMilliseconds;
        internal ulong PrimaryCueStartedAtMilliseconds;
        internal ulong InteractCueStartedAtMilliseconds;
        internal ulong LandingCueStartedAtMilliseconds;
        internal ulong LastActionSequence;
        internal uint MainHandPrefabId;
        internal bool HasObservedPosition;
        internal bool PrimitiveFallback;
    }
}
