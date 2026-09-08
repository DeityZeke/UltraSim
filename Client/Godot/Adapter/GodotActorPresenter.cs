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
    bool ShouldBatch(uint prefabId);
}

public readonly record struct GodotActorPresenterSnapshot(
    int ActiveActors,
    int PrimitiveFallbackActors,
    int VisibleNameplates,
    int VisibleOverheadLabels,
    bool RevealHeld,
    ulong Added,
    ulong Updated,
    ulong Removed);

/// <summary>
/// Native realization of engine-neutral actor state. One actor root is earned by moving/nameplate/
/// overhead anchors; terrain cells and ordinary public objects remain batched by the world renderer.
/// </summary>
public sealed class GodotActorPresenter : IGodotWorldPublicBatchFilter, IDisposable
{
    private const float MillimetersToMeters = 0.001f;
    private readonly ClientActorDefinitionCatalog _catalog;
    private readonly IGodotActorVisualResolver? _visuals;
    private readonly ActorSlot[] _slots;
    private readonly int _ownerThreadId = System.Environment.CurrentManagedThreadId;
    private bool _revealHeld;
    private WorldPublicEntityId _controlledActor;
    private WorldPosition _controlledPosition;
    private WorldYaw _controlledOrientation;
    private bool _controlledPoseAvailable;
    private WorldPosition _origin;
    private int _active;
    private int _primitive;
    private ulong _added;
    private ulong _updated;
    private ulong _removed;
    private bool _disposed;

    public GodotActorPresenter(
        Node3D host,
        ClientActorDefinitionCatalog catalog,
        int maximumActors = 256,
        IGodotActorVisualResolver? visuals = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        if (maximumActors <= 0) throw new ArgumentOutOfRangeException(nameof(maximumActors));
        _visuals = visuals;
        _slots = new ActorSlot[maximumActors];
        Root = new Node3D { Name = "Actors" };
        host.AddChild(Root);
    }

    public Node3D Root { get; }

    public bool ShouldBatch(uint prefabId) => !_catalog.TryGet(prefabId, out _);

    /// <summary>
    /// Supplies the fresh owner-private pose for the locally controlled actor. Its independently
    /// delivered public record may legitimately trail that pose; using the public record for the
    /// controlled root would draw the actor behind the player-relative world origin and then snap
    /// it forward whenever public delivery catches up.
    /// </summary>
    public void SetControlledActorPose(
        WorldPublicEntityId id,
        in WorldPosition position,
        WorldYaw orientation)
    {
        EnsureOperational();
        if (!id.IsValid) throw new ArgumentOutOfRangeException(nameof(id));
        _controlledActor = id;
        _controlledPosition = position;
        _controlledOrientation = orientation;
        _controlledPoseAvailable = true;
        RefreshNameplates();
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
        UpdateTransformsAndLifetimes(nowMilliseconds);
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
        ref ActorSlot slot = ref _slots[Find(occurrence.PublicAnchor)];
        slot.Overhead!.Text = occurrence.Text;
        slot.Overhead.Visible = true;
        slot.OverheadExpiresAtMilliseconds = expiresAtMilliseconds;
        return true;
    }

    public bool TryGetActorRoot(WorldPublicEntityId id, out Node3D? root)
    {
        EnsureOperational();
        int slot = Find(id);
        root = slot >= 0 ? _slots[slot].Root : null;
        return root is not null;
    }

    public GodotActorPresenterSnapshot GetSnapshot()
    {
        EnsureOperational();
        int nameplates = 0;
        int overhead = 0;
        for (int index = 0; index < _slots.Length; index++)
        {
            if (!_slots[index].Used) continue;
            if (_slots[index].Nameplate?.Visible == true) nameplates++;
            if (_slots[index].Overhead?.Visible == true) overhead++;
        }
        return new GodotActorPresenterSnapshot(
            _active, _primitive, nameplates, overhead, _revealHeld,
            _added, _updated, _removed);
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        for (int index = 0; index < _slots.Length; index++)
            if (_slots[index].Used) Release(index);
        if (GodotObject.IsInstanceValid(Root))
        {
            Root.GetParent()?.RemoveChild(Root);
            Root.QueueFree();
        }
    }

    private void Add(in ClientActorPresentationRecord actor)
    {
        if (Find(actor.Id) >= 0)
            throw new InvalidOperationException("An actor presentation was added twice.");
        int index = FindFree();
        if (index < 0) throw new InvalidOperationException("The native actor presentation is full.");
        Node3D root = new() { Name = $"Actor_{actor.Id.Index}_{actor.Id.Generation}" };
        ClientActorDefinition definition = actor.Definition;
        Node3D? visual = _visuals?.Instantiate(in definition);
        bool primitive = visual is null;
        visual ??= CreatePrimitive(actor.Definition.VisualKind);
        visual.Name = "Visual";
        float scale = actor.Definition.VisualScalePermille / 1_000f;
        visual.Scale *= new Vector3(scale, scale, scale);
        root.AddChild(visual);
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
        root.AddChild(new Marker3D
        {
            Name = "EffectsAnchor",
            Position = new Vector3(
                0, actor.Definition.EffectsHeightMillimeters * MillimetersToMeters, 0),
        });
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
        Root.AddChild(root);
        _slots[index] = new ActorSlot
        {
            Used = true,
            Record = actor,
            Root = root,
            Nameplate = nameplate,
            Overhead = overhead,
            PrimitiveFallback = primitive,
        };
        _active++;
        if (primitive) _primitive++;
        SaturatingIncrement(ref _added);
        ApplySlot(index, nowMilliseconds: 0);
    }

    private void Update(in ClientActorPresentationRecord actor)
    {
        int index = Find(actor.Id);
        if (index < 0)
            throw new InvalidOperationException("An unknown actor presentation was updated.");
        ref ActorSlot slot = ref _slots[index];
        if (slot.Record.Definition.VisualKind != actor.Definition.VisualKind ||
            slot.Record.Definition.VisualScalePermille != actor.Definition.VisualScalePermille)
        {
            bool overheadVisible = slot.Overhead?.Visible == true;
            string overheadText = slot.Overhead?.Text ?? string.Empty;
            ulong overheadExpiry = slot.OverheadExpiresAtMilliseconds;
            Release(index);
            Add(in actor);
            index = Find(actor.Id);
            if (overheadVisible && index >= 0)
            {
                _slots[index].Overhead!.Text = overheadText;
                _slots[index].Overhead!.Visible = true;
                _slots[index].OverheadExpiresAtMilliseconds = overheadExpiry;
            }
            return;
        }
        slot.Record = actor;
        slot.Nameplate!.Text = actor.DisplayLabel;
        slot.Nameplate.Position = new Vector3(
            0, actor.Definition.NameplateHeightMillimeters * MillimetersToMeters, 0);
        slot.Overhead!.Position = new Vector3(
            0, (actor.Definition.NameplateHeightMillimeters + 400) * MillimetersToMeters, 0);
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
        if (slot.Root is not null && GodotObject.IsInstanceValid(slot.Root))
        {
            slot.Root.GetParent()?.RemoveChild(slot.Root);
            slot.Root.QueueFree();
        }
        if (slot.PrimitiveFallback) _primitive--;
        _active--;
        slot = default;
    }

    private void UpdateTransformsAndLifetimes(ulong nowMilliseconds)
    {
        for (int index = 0; index < _slots.Length; index++)
            if (_slots[index].Used) ApplySlot(index, nowMilliseconds);
    }

    private void ApplySlot(int index, ulong nowMilliseconds)
    {
        ref ActorSlot slot = ref _slots[index];
        ClientActorPresentationRecord actor = slot.Record;
        bool controlled = _controlledPoseAvailable && actor.Id == _controlledActor;
        WorldPosition position = controlled ? _controlledPosition : actor.Position;
        WorldYaw orientation = controlled ? _controlledOrientation : actor.Orientation;
        slot.Root!.Position = new Vector3(
            checked(position.GlobalXMillimeters - _origin.GlobalXMillimeters) *
                MillimetersToMeters,
            checked(position.YMillimeters - _origin.YMillimeters) * MillimetersToMeters,
            checked(position.GlobalZMillimeters - _origin.GlobalZMillimeters) *
                MillimetersToMeters);
        slot.Root.Rotation = new Vector3(
            0, orientation.Units * (MathF.Tau / WorldYaw.UnitsPerTurn), 0);
        slot.Nameplate!.Visible = actor.NameplateEligible &&
            (actor.Id == _controlledActor || _revealHeld);
        if (nowMilliseconds != 0 && slot.OverheadExpiresAtMilliseconds != 0 &&
            nowMilliseconds >= slot.OverheadExpiresAtMilliseconds)
        {
            slot.Overhead!.Visible = false;
            slot.Overhead.Text = string.Empty;
            slot.OverheadExpiresAtMilliseconds = 0;
        }
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

    private static Node3D CreatePrimitive(ClientActorVisualKind kind)
    {
        Vector3 size = kind == ClientActorVisualKind.WoodlandAnimal
            ? new Vector3(1.1f, 1.15f, 0.55f)
            : new Vector3(0.6f, 1.8f, 0.6f);
        Color color = kind switch
        {
            ClientActorVisualKind.HumanX => new Color(0.25f, 0.72f, 0.92f),
            ClientActorVisualKind.HumanY => new Color(0.86f, 0.52f, 0.24f),
            _ => new Color(0.48f, 0.35f, 0.23f),
        };
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 0.9f,
        };
        return new MeshInstance3D
        {
            Mesh = GodotMeshGeometry.CreateGroundedBox(size, material),
        };
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
        internal ClientActorPresentationRecord Record;
        internal Node3D? Root;
        internal Label3D? Nameplate;
        internal Label3D? Overhead;
        internal ulong OverheadExpiresAtMilliseconds;
        internal bool PrimitiveFallback;
    }
}
