using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.World;

public partial class T010PlayableWorldAcceptance
{
    private void RunTemporalMotionNativeAcceptance()
    {
        WorldPosition remotePosition = default;
        ClientActorDefinition remoteDefinition = default;
        Expect(WorldPosition.TryFromGlobalMillimeters(0, 0, 0, out WorldPosition origin) &&
            WorldPosition.TryFromGlobalMillimeters(1_000, 0, 0, out remotePosition),
            "The native temporal-motion positions were invalid.");
        ClientActorDefinitionCatalog catalog = ClientActorDefinitionCatalog.ConceptDemo;
        Expect(catalog.TryGet(GameWorldPublicPrefabIds.Character,
                out ClientActorDefinition controlledDefinition) &&
            catalog.TryGet(GameWorldPublicPrefabIds.WoodlandAnimal, out remoteDefinition),
            "The native temporal-motion actor definitions were unavailable.");
        WorldPublicEntityId controlledId = new(8_101, 1);
        WorldPublicEntityId remoteId = new(8_102, 1);
        ClientActorPresentationRecord controlledRecord = new(
            controlledId, GameWorldPublicPrefabIds.Character, origin, WorldYaw.Zero,
            controlledDefinition, "Controlled", true, GameWorldPublicPrefabIds.IronSword,
            ClientPublicActorVerticalPhase.Grounded, PublicVersion: 1);
        ClientActorPresentationRecord remoteRecord = new(
            remoteId, GameWorldPublicPrefabIds.WoodlandAnimal, remotePosition, WorldYaw.Zero,
            remoteDefinition, "Other", true, PublicVersion: 1);
        ClientActorPresentationChange[] additions =
        [
            new(ClientActorPresentationChangeKind.Added, controlledId, controlledRecord),
            new(ClientActorPresentationChangeKind.Added, remoteId, remoteRecord),
        ];
        using var presenter = new GodotActorPresenter(this, catalog, 2);
        presenter.SetControlledActorPose(controlledId, origin, WorldYaw.Zero, 1_000);
        presenter.Apply(additions, origin, 1_000);
        Node3D? remoteRoot = null;
        Expect(presenter.TryGetActorRoot(controlledId, out Node3D? controlledRoot) &&
            controlledRoot is not null &&
            presenter.TryGetActorRoot(remoteId, out remoteRoot) &&
            remoteRoot is not null,
            "Temporal-motion actor roots were not realized.");
        Node3D? hand = FindNamedDescendant(controlledRoot!, "MainHandSocket") as Node3D;
        Node3D? nameplate = FindNamedDescendant(controlledRoot!, "Nameplate") as Node3D;
        Node3D? overhead = FindNamedDescendant(controlledRoot!, "OverheadSpeech") as Node3D;
        Expect(hand is not null && nameplate is not null && overhead is not null,
            "The controlled actor lacked attached equipment/nameplate/overhead anchors.");
        Vector3 initialHand = hand!.GlobalPosition;
        Vector3 initialNameplate = nameplate!.GlobalPosition;
        Vector3 initialOverhead = overhead!.GlobalPosition;
        var camera = new Camera3D { Name = "TemporalAcceptanceCamera" };
        AddChild(camera);
        try
        {
            GameplayFrameLayout frame = GameplayFrameLayout.MaxFit(
                ClientPixelSize.Create(64, 64), ClientPixelSize.Create(1920, 1080));
            var cameraRig = new GodotGameplayCameraRig(camera, ClientStockCameraRigs.Orbit, frame);
            Vector3 initialCamera = camera.Position;
            var controlled = new ClientControlledMotionPresentation();
            controlled.ObserveOwner(1, new GameControlSessionEpoch(2), 3, controlledId,
                1, 0, origin, WorldYaw.Zero, false, 1_000_000);
            Expect(controlled.ObserveAdmittedCommand(1,
                GameLocomotionUpdate.CreateDirect(short.MaxValue, 0), 1_001_000),
                "The native temporal predictor did not accept semantic movement.");
            ClientResolvedMotionPose prediction = controlled.Sample(1_101_000);
            WorldPosition predictedPosition = default;
            Expect(prediction.Provenance == ClientMotionPoseProvenance.PredictedControlled &&
                prediction.Pose.TryGetWorldPosition(out predictedPosition),
                "The native temporal predictor did not resolve a valid presented pose.");
            presenter.SetControlledActorPose(controlledId, predictedPosition,
                prediction.Pose.Orientation, 1_101);
            presenter.UpdateOriginAndLifetimes(origin, 1_101);
            cameraRig.SetTargetOffset(new Vector3(
                prediction.Pose.GlobalXMillimeters * 0.001f, 0, 0));
            Expect(controlledRoot!.Position.X > 0.5f &&
                MathF.Abs(camera.Position.X - initialCamera.X - controlledRoot.Position.X) < 0.002f &&
                MathF.Abs(hand.GlobalPosition.X - initialHand.X - controlledRoot.Position.X) < 0.002f &&
                MathF.Abs(nameplate.GlobalPosition.X - initialNameplate.X - controlledRoot.Position.X) < 0.002f &&
                MathF.Abs(overhead.GlobalPosition.X - initialOverhead.X - controlledRoot.Position.X) < 0.002f &&
                controlledRecord.Position == origin,
                "Predicted motion did not move the whole actor/camera while preserving semantic truth.");
            Expect(WorldPosition.TryFromGlobalMillimeters(300, 0, 0,
                out WorldPosition ownerPosition),
                "The native temporal owner position was invalid.");
            controlled.ObserveOwner(1, new GameControlSessionEpoch(2), 3, controlledId,
                2, 1, ownerPosition, WorldYaw.Zero, false, 1_151_000);
            ClientResolvedMotionPose reconciled = controlled.Sample(1_151_000);
            Expect(reconciled.Pose.GlobalXMillimeters >= 300 &&
                reconciled.Pose.GlobalXMillimeters <= 900,
                "The owner update caused a double-motion jump.");

            var remote = new ClientRemoteActorInterpolation();
            ClientResolvedMotionPose middle = default;
            Expect(remote.Observe(3, remoteId, 1,
                ClientMotionPose.FromAuthority(remotePosition, WorldYaw.Zero), 1_000_000) &&
                WorldPosition.TryFromGlobalMillimeters(2_000, 0, 0,
                    out WorldPosition secondRemotePosition) &&
                remote.Observe(3, remoteId, 2,
                    ClientMotionPose.FromAuthority(secondRemotePosition, WorldYaw.Zero), 1_100_000) &&
                remote.TrySample(remoteId, 1_150_000, out middle) &&
                middle.Provenance == ClientMotionPoseProvenance.InterpolatedRemote &&
                middle.Pose.GlobalXMillimeters == 1_500 &&
                presenter.SetPresentedPose(remoteId, middle.Pose),
                "The remote authoritative samples did not yield an intermediate native pose.");
            presenter.UpdateOriginAndLifetimes(origin, 1_150);
            Expect(MathF.Abs(remoteRoot!.Position.X - 1.5f) < 0.002f &&
                remoteRecord.Position == remotePosition,
                "The remote actor did not realize an intermediate pose beside exact semantic truth.");
            remote.Remove(remoteId);
            Expect(!remote.TrySample(remoteId, 1_160_000, out _),
                "Removal retained old remote interpolation samples.");
            GD.Print($"F1.8A TEMPORAL NATIVE PASS predicted_mm=" +
                $"{prediction.Pose.GlobalXMillimeters} interpolated_mm=" +
                $"{middle.Pose.GlobalXMillimeters} camera=follow anchors=follow semantic=exact");
        }
        finally
        {
            RemoveChild(camera);
            camera.QueueFree();
        }
    }
}
