using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Local;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.World;

namespace UltraSim.Client.Godot;

public partial class LocalCabinClient
{
    public GodotActorPresenterSnapshot SetF112Reveal(bool held)
    {
        if (held && _camera is not null && TryFindPublicRecord(GameWorldPublicPrefabIds.WoodlandAnimal, out var animal))
        {
            Vector3 target = new((animal.Position.GlobalXMillimeters - _player.GlobalXMillimeters) * .001f,
                .7f, (animal.Position.GlobalZMillimeters - _player.GlobalZMillimeters) * .001f);
            _camera.Position = target + new Vector3(0, 4, 6);
            _camera.LookAt(target);
        }
        _Input(new InputEventKey { Keycode = Key.Ctrl, PhysicalKeycode = Key.Ctrl, Pressed = held });
        return _actorPresenter!.GetSnapshot();
    }
    /// <summary>Cold native product control: nominate through the actual frame mapping and camera ray.</summary>
    public async Task<object> RunF112PointerControl(bool checkInteraction = false)
    {
        if (_camera is null || _sessionUi is null || _actorPresenter is null)
            throw new InvalidOperationException("Native pointer consumer is unavailable.");
        SetPhysicsProcess(false); // one exact retained disclosure/pose, no synthetic entities
        LocalPlayableAuthorityView initialAuthority = ReadF112Authority();
        var results = new List<object>(4);
        foreach (uint prefab in new[] { GameWorldPublicPrefabIds.WoodlandAnimal,
            GameWorldPublicPrefabIds.IronSword, GameWorldPublicPrefabIds.InformationalSign })
        {
            if (!TryFindPublicRecord(prefab, out WorldPublicRecord record))
                throw new InvalidOperationException($"Pointer target {prefab} was not disclosed.");
            ClientPrimitivePresentation bounds = ClientPrimitivePresentationCatalog.ForPublic(prefab);
            Vector3 center = new(
                (record.Position.GlobalXMillimeters - _player.GlobalXMillimeters) * .001f,
                (record.Position.YMillimeters - _player.YMillimeters) * .001f +
                    (bounds.MinimumYMillimeters + bounds.MaximumYMillimeters) * .0005f,
                (record.Position.GlobalZMillimeters - _player.GlobalZMillimeters) * .001f);
            // A bounded diagnostic camera chooses the target, not the picking result.
            _camera.Position = center + new Vector3(0, 6, 6);
            _camera.LookAt(center);
            Vector2 output = _camera.UnprojectPosition(center);
            Vector2 application = ToApplication(output);
            GetTree().Root.WarpMouse(application);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            UpdatePointerWorldPoint(); // the shipping mouse->frame->output->camera nomination, not just math
            if (!_sessionUi.TryToOutput(application, out Vector2 mapped) ||
                !TryPickPresentedPublicObject(_camera.ProjectRayOrigin(mapped),
                    _camera.ProjectRayNormal(mapped), out var point, out var id, out uint picked) ||
                id != record.Id || picked != prefab || _pointerTarget != record.Id)
                throw new InvalidOperationException($"Native camera ray nominated wrong identity for {prefab}.");
            _pointerTarget = id; _pointerTargetPrefab = picked; _pointerPoint = point; _pointerPointValid = true;
            Select(point); // unchanged product semantic selection path; Game still authorizes Interact
            string? interaction = null;
            if (checkInteraction && prefab == GameWorldPublicPrefabIds.InformationalSign)
            {
                if (!_sequencer!.TryCreateSemanticCommand(new ClientWorldCommandIntent(
                        GameLocomotionUpdate.Stop, GamePlayerActions.Interact, true), out GamePlayerCommand use))
                    throw new InvalidOperationException("Mapped sign interaction could not be formed.");
                ApplyCommand(use, false, default, false);
                DriveLocalUntil(() => ReadF112Authority().LastCommandSequence == use.Sequence);
                interaction = ReadF112Authority().LastCommandResult.Interaction.ToString();
                // This disclosed fallback-lab sign is deliberately beyond reach at PlayerStart.
                if (ReadF112Authority().LastCommandResult.Interaction != GameWorldActionStatus.OutOfRange)
                    throw new InvalidOperationException($"Display mapping sign range result was {interaction}, selection={_selection}, player={ReadF112Authority().PlayerPosition}.");
                SubmitF112Stop();
            }
            results.Add(new { Kind = "Public", record.Id, record.EntityPublicVersion, prefab,
                Application = application.ToString(), ObservedMouse = GetTree().Root.GetMousePosition().ToString(),
                Output = mapped.ToString(), Selection = _selection.ToString(),
                Point = point, interaction,
                Tier = _actorPresenter.TryGetRepresentationTier(id, out var tier) ? tier.ToString() : null });
        }
        Vector3 ground = new(2, 0, -2);
        _camera.Position = ground + new Vector3(0, 6, 6); _camera.LookAt(ground);
        Vector2 groundApplication = ToApplication(_camera.UnprojectPosition(ground));
        GetTree().Root.WarpMouse(groundApplication);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        UpdatePointerWorldPoint();
        if (!_sessionUi.TryToOutput(groundApplication, out Vector2 groundOutput))
            throw new InvalidOperationException("Ground pointer escaped Gameplay Frame.");
        Vector3 origin = _camera.ProjectRayOrigin(groundOutput), direction = _camera.ProjectRayNormal(groundOutput);
        Vector3 hit = origin + direction * (-origin.Y / direction.Y);
        long x = _player.GlobalXMillimeters + (long)Math.Round(hit.X * 1000);
        long z = _player.GlobalZMillimeters + (long)Math.Round(hit.Z * 1000);
        if (Math.Abs(hit.X - ground.X) > .01 || Math.Abs(hit.Z - ground.Z) > .01 || !TrySetPointerWorldPoint(x, z))
            throw new InvalidOperationException("Mapped ground ray lost its authoritative support.");
        Select(_pointerPoint);
        results.Add(new { Kind = "Ground", Application = groundApplication.ToString(),
            Output = groundOutput.ToString(), Point = _pointerPoint, Selection = _selection.ToString() });
        LocalPlayableAuthorityView finalAuthority = ReadF112Authority();
        if (initialAuthority.PlayerPosition != finalAuthority.PlayerPosition ||
            initialAuthority.PublicActor != finalAuthority.PublicActor ||
            initialAuthority.GenerationId != finalAuthority.GenerationId ||
            initialAuthority.MainHandPrefabId != finalAuthority.MainHandPrefabId)
            throw new InvalidOperationException("Native pointer/display changed authoritative actor state.");
        return new { Results = results, InitialAuthority = initialAuthority.PlayerPosition,
            FinalAuthority = finalAuthority.PlayerPosition, Snapshot = GetPresentationSnapshot() };

        Vector2 ToApplication(Vector2 output)
        {
            GameplayFrameLayout layout = _sessionUi.GameplayFrame;
            return new(layout.Frame.X + output.X * layout.Frame.Width / layout.SelectedOutput.Width,
                layout.Frame.Y + output.Y * layout.Frame.Height / layout.SelectedOutput.Height);
        }
    }

    public object RunF112TierControl(bool far)
    {
        SetPhysicsProcess(true);
        SetF112StableRadius(3); // diagnostic visibility, restored at retirement; no production policy change
        WorldPosition position = far
            ? new WorldPosition(2, 0, 7000, 4000, 0) : GamePlayableWorld.PlayerStart;
        RelocateControlledActor(position);
        SubmitF112Stop();
        SetPhysicsProcess(false);
        if (!TryFindPublicRecord(GameWorldPublicPrefabIds.WoodlandAnimal, out WorldPublicRecord animal) ||
            !_actorPresenter!.TryGetRepresentationTier(animal.Id, out var tier) ||
            tier != (far ? ClientActorRepresentationTier.BatchedPrimitive : ClientActorRepresentationTier.Detailed))
            throw new InvalidOperationException("Actual disclosed animal did not select the expected distance tier.");
        if (!_actorPresenter.TryGetActorRecord(animal.Id, out var semantic) ||
            !_actorPresenter.TryGetPresentedPose(animal.Id, out var pose))
            throw new InvalidOperationException("Tier lost exact retained identity/pose.");
        bool root = _actorPresenter.TryGetActorRoot(animal.Id, out _);
        var before = _actorPresenter.GetSnapshot();
        _Input(new InputEventKey { Keycode = Key.Ctrl, PhysicalKeycode = Key.Ctrl, Pressed = true });
        var revealed = _actorPresenter.GetSnapshot();
        if (revealed.BatchedPrimitiveActors != before.BatchedPrimitiveActors ||
            revealed.VisibleNameplates <= before.VisibleNameplates ||
            !_actorPresenter.TryGetActorRecord(animal.Id, out var after) || after != semantic ||
            !_actorPresenter.TryGetPresentedPose(animal.Id, out var afterPose) || afterPose != pose ||
            far && (root || _actorPresenter.TryGetActorRoot(animal.Id, out _)))
            throw new InvalidOperationException($"Ctrl reveal mismatch far={far} before={before} revealed={revealed} input={_gameplayInputAllowed} root={root}.");
        _Input(new InputEventKey { Keycode = Key.Ctrl, PhysicalKeycode = Key.Ctrl, Pressed = false });
        var released = _actorPresenter.GetSnapshot();
        if (released.VisibleNameplates != before.VisibleNameplates)
            throw new InvalidOperationException("Ctrl nameplate overlay did not retire.");
        LocalPlayableAuthorityView authority = ReadF112Authority();
        if (!_actorPresenter.TryGetRepresentationTier(authority.PublicActor, out var controlledTier) ||
            controlledTier != ClientActorRepresentationTier.Detailed)
            throw new InvalidOperationException("Controlled actor lost mandatory Detailed representation.");
        return new { far, animal.Id, animal.EntityPublicVersion, semantic, pose, tier, root, before, revealed, released,
            ControlledId = authority.PublicActor, controlledTier, Snapshot = GetPresentationSnapshot() };
    }
}
