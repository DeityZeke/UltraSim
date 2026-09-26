using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Local;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.World;

namespace UltraSim.Client.Godot;

public partial class LocalCabinClient
{
    public object SetF112CabinView(string view)
    {
        if (_camera is null || _cache is null) throw new InvalidOperationException("Cabin is not ready.");
        (Vector3 from, Vector3 target) = view switch
        {
            "Exterior" => (new Vector3(8, 6, 1), new Vector3(16, 1, 12)),
            "Doorway" => (new Vector3(16, 1.6f, 6), new Vector3(16, 1.4f, 12)),
            "Interior" => (new Vector3(16, 1.6f, 10), new Vector3(16, 1.4f, 14)),
            "Shelf" => (new Vector3(16.5f, 1.6f, 12.5f), new Vector3(18.5f, 1.2f, 14)),
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        };
        Vector3 origin = new(_player.GlobalXMillimeters * .001f, _player.YMillimeters * .001f,
            _player.GlobalZMillimeters * .001f);
        _camera.Position = from - origin;
        _camera.LookAt(target - origin);
        LocalPlayableAuthorityView authority = ReadF112Authority();
        return new { ApplicationEpoch = _localSession!.ApplicationSessionEpoch,
            ControlEpoch = _localSession.ControlSession.Value, authority.GenerationId,
            Manifest = authority.PreparedComposition?.Manifest.Fingerprint.ToString(),
            Catalog = _resolvedContent!.Catalog.Fingerprint.ToString(),
            Presentation = GetPresentationSnapshot().PresentationGeneration,
            SemanticHash = _cache.GetSnapshot().WorldSemanticHash,
            Camera = from.ToString(), Target = target.ToString() };
    }
    public GodotPresentationEnvironmentSnapshot ApplyF112EnvironmentPreview(
        GodotDiagnosticTimeOfDay time, GodotDiagnosticAtmosphere atmosphere)
    {
        if (_diagnosticEnvironment is null)
            throw new InvalidOperationException("An explicit diagnostic environment preview is required.");
        _diagnosticEnvironment.Apply(time, atmosphere, shadows: true);
        return _diagnosticEnvironment.GetSnapshot();
    }
    /// <summary>Cold, opt-in product acceptance; commands use the existing authority bridge.</summary>
    public object RunF112SaveLoadAcceptance(bool far)
    {
        if (_localSession is null || _cache?.PreparedComposition is null)
            throw new InvalidOperationException("F1.12 requires canonical multi-Pack Local play.");
        RelocateControlledActor(far ? GamePlayableWorld.FarGateDestination : GamePlayableWorld.PlayerStart);
        SubmitF112Stop();
        LocalPlayableAuthorityView saved = ReadF112Authority();
        var manifest = saved.PreparedComposition!.Manifest.Fingerprint;
        ulong application = _localSession.ApplicationSessionEpoch;
        ulong saveRequest = _nextAdministrativeRequest;
        HandleKey(new InputEventKey { Keycode = Key.F5, Pressed = true });
        DriveLocalUntil(() => ReadF112Authority().LastAdministrativeSequence == saveRequest);
        if (!ReadF112Authority().LastAdministrativeSucceeded)
            throw new InvalidOperationException(ReadF112Authority().LastAdministrativeMessage);
        RelocateControlledActor(far ? GamePlayableWorld.PlayerStart : GamePlayableWorld.FarGateDestination);
        ulong loadRequest = _nextAdministrativeRequest;
        HandleKey(new InputEventKey { Keycode = Key.F9, Pressed = true });
        DriveLocalUntil(() => ReadF112Authority().LastAdministrativeSequence == loadRequest);
        LocalPlayableAuthorityView loaded = ReadF112Authority();
        if (!loaded.LastAdministrativeSucceeded)
            throw new InvalidOperationException(loaded.LastAdministrativeMessage);
        DriveLocalUntil(() => _player == saved.PlayerPosition &&
            _residentGenerationId == loaded.GenerationId && CountPresentedCharacters() == 1);
        if (loaded.GenerationId <= saved.GenerationId || loaded.PlayerPosition != saved.PlayerPosition ||
            loaded.DoorIsOpen != saved.DoorIsOpen || loaded.BreadIsInBackpack != saved.BreadIsInBackpack ||
            loaded.MainHandPrefabId != saved.MainHandPrefabId ||
            loaded.GuestIdentity != saved.GuestIdentity ||
            loaded.PreparedComposition!.Manifest.Fingerprint != manifest ||
            _localSession.ApplicationSessionEpoch != application)
            throw new InvalidOperationException("F1.12 save/load lost persisted truth or session/manifest ownership.");
        return new { far, saved.PlayerPosition, saved.DoorIsOpen, saved.BreadIsInBackpack,
            saved.MainHandPrefabId, OldGeneration = saved.GenerationId, NewGeneration = loaded.GenerationId,
            ApplicationEpoch = application, Manifest = manifest.ToString() };
    }

    public void RunF112ItemAcceptance()
    {
        ProbeF13PrimitiveDropAndPickup();
        SubmitF112Stop(); // release the pickup Interact edge before the next product action
    }

    public void RunF112CabinAcceptance() => ProbeDoorAndChestInteraction();

    public void PrepareF112CentralControl()
    {
        RelocateControlledActor(GamePlayableWorld.PlayerStart);
        SubmitF112Stop();
    }

    public void SetF112StableRadius(int radius)
    {
        if (radius is < 1 or > 3 || _settings is null)
            throw new InvalidOperationException("F1.12 stable radius must be 1/2/3.");
        ApplySettings(WithRenderDistance(_settings, radius));
        DrainRenderer();
    }

    public object RunF112ReentryLoadAcceptance()
    {
        LocalPlayableAuthorityView initial = ReadF112Authority();
        ulong request = _nextAdministrativeRequest;
        HandleKey(new InputEventKey { Keycode = Key.F9, Pressed = true });
        DriveLocalUntil(() => ReadF112Authority().LastAdministrativeSequence == request);
        LocalPlayableAuthorityView loaded = ReadF112Authority();
        if (!loaded.LastAdministrativeSucceeded || loaded.GenerationId <= initial.GenerationId ||
            loaded.PlayerPosition != GamePlayableWorld.PlayerStart || !loaded.DoorIsOpen ||
            loaded.BreadIsInBackpack || loaded.MainHandPrefabId != GameWorldPublicPrefabIds.IronSword)
            throw new InvalidOperationException("Re-entered product failed to restore prior central equipment/world save.");
        DriveLocalUntil(() => _residentGenerationId == loaded.GenerationId &&
            _player == loaded.PlayerPosition && _actorPresenter!.GetSnapshot().EquippedMainHands == 1);
        return new { initial.GenerationId, RestoredGeneration = loaded.GenerationId,
            loaded.PlayerPosition, loaded.DoorIsOpen, loaded.BreadIsInBackpack, loaded.MainHandPrefabId };
    }

    public void RunF112ProfileAcceptance()
    {
        LocalPlayableAuthorityView before = ReadF112Authority();
        foreach (ControlProfile profile in Enum.GetValues<ControlProfile>())
        {
            SetProfile(profile);
            SubmitF112Stop();
            LocalPlayableAuthorityView after = ReadF112Authority();
            if (before.GenerationId != after.GenerationId || before.PublicActor != after.PublicActor ||
                before.GuestIdentity != after.GuestIdentity)
                throw new InvalidOperationException("Camera/control switching replaced authoritative identity.");
        }
        SetProfile(ControlProfile.OrbitCommand);
    }

    private void SubmitF112Stop()
    {
        if (_sequencer is null || !_sequencer.TryCreateSemanticCommand(new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.None, true), out GamePlayerCommand stop))
            throw new InvalidOperationException("F1.12 Stop did not form.");
        ApplyCommand(stop, false, default, false);
        DriveLocalUntil(() => ReadF112Authority().LastCommandSequence == stop.Sequence);
    }

    private LocalPlayableAuthorityView ReadF112Authority() =>
        _localSession!.TryGetLatestView(out LocalPlayableAuthorityView view) ? view :
            throw new InvalidOperationException("F1.12 authority view unavailable.");
}
