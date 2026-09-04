using Godot;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Core;
using UltraSim.Game.Composition;

namespace UltraSim.Client.Godot;

/// <summary>
/// Shared F0.2 local-capability composition used by the Single Player and Complete executables.
/// The shared bootstrap remains capability-free; Online therefore never resolves this assembly.
/// </summary>
public abstract partial class LocalPlayClientRoot : ClientRoot
{
    private LocalCabinClient? _cabin;
    private Node? _catalogMarker;

    public override void _Ready()
    {
        base._Ready();
        string[] arguments = OS.GetCmdlineUserArgs();
        bool conceptDemoSmoke = arguments.Contains("--d0-0-smoke", StringComparer.Ordinal);
        bool residencySmoke = arguments.Contains("--d0-1-smoke", StringComparer.Ordinal);
        bool farIslandSmoke = arguments.Contains("--d0-2-smoke", StringComparer.Ordinal);
        bool f02Smoke = arguments.Contains("--f0-2-smoke", StringComparer.Ordinal);
        if (arguments.Contains("--composition-smoke", StringComparer.Ordinal) ||
            !conceptDemoSmoke && !residencySmoke && !farIslandSmoke && !f02Smoke)
            return;
        try
        {
            if (conceptDemoSmoke || residencySmoke || farIslandSmoke)
            {
                StartLocalCabin(
                    CabinContentVariant.Default, CabinHumanBody.XBot, useConceptDemoWorld: true);
                LocalCabinPresentationSnapshot evidence;
                if (farIslandSmoke)
                    evidence = _cabin!.RunD02Acceptance();
                else
                {
                    _cabin!.RunD00Acceptance();
                    evidence = residencySmoke
                        ? _cabin.RunD01Acceptance()
                        : _cabin.GetPresentationSnapshot();
                }
                if (evidence.AuthoredChunks != UltraSim.Game.Composition.GamePlayableWorld.ConceptChunkCount ||
                    evidence.SemanticResidentChunks >= evidence.AuthoredChunks ||
                    evidence.RenderedChunks >= evidence.SemanticResidentChunks)
                    throw new InvalidOperationException(
                        "The Concept Demo did not preserve authored > resident > rendered structure.");
                GD.Print(
                    $"{(farIslandSmoke ? "D0.2 FAR ISLAND" :
                        residencySmoke ? "D0.1 RESIDENCY" : "D0.0 CONCEPT DEMO")} PASS " +
                    $"player={evidence.PlayerPosition} authored/resident/rendered=" +
                    $"{evidence.AuthoredChunks}/{evidence.SemanticResidentChunks}/" +
                    $"{evidence.RenderedChunks} radius={evidence.RenderDistanceChunks} " +
                    $"authorityTicks={evidence.AuthorityTicks} content={evidence.ContentFingerprint}");
                RetireCurrentSession();
                ReturnToMainMenu(farIslandSmoke
                    ? "Concept Demo D0.2 Far Island smoke passed."
                    : residencySmoke
                        ? "Concept Demo D0.1 residency smoke passed."
                        : "Concept Demo D0.0 smoke passed.");
                GetTree().Quit(0);
                return;
            }

            CabinContentVariant variant = ReadVariant();
            CabinHumanBody human = ReadHuman();
            StartLocalCabin(variant, human, useConceptDemoWorld: false);
            LocalCabinPresentationSnapshot first = _cabin!.GetPresentationSnapshot();
            ValidateEffectiveSettings(first);
            _cabin.RunF02Acceptance();

            RetireCurrentSession();
            ReturnToMainMenu("First local session retired.");
            if (WorldRoot.HasNode("LocalCabinSession") ||
                ContentCatalogRoot.GetChildCount() != 0)
                throw new InvalidOperationException("Local session presentation did not retire cleanly.");

            StartLocalCabin(variant, human, useConceptDemoWorld: false);
            LocalCabinPresentationSnapshot second = _cabin!.GetPresentationSnapshot();
            ValidateEffectiveSettings(second);
            if (!second.IsInitialized || first.ApplicationSessionEpoch == second.ApplicationSessionEpoch ||
                first.ControlSessionEpoch == second.ControlSessionEpoch ||
                first.ContentFingerprint != second.ContentFingerprint ||
                first.ContentGeneration == second.ContentGeneration ||
                second.PlayerPosition != UltraSim.Game.Composition.GamePlayableWorld.PlayerStart ||
                second.DoorIsOpen || !second.BreadIsInBackpack || second.OpenUiViews != 0)
                throw new InvalidOperationException("Repeated local entry reused retired session/content lineage.");

            GD.Print(
                $"F0.2 SINGLE PLAYER PASS variant={variant} human={human} " +
                $"fingerprint={second.ContentFingerprint} meshes={second.HumanMeshInstances} " +
                $"session={first.ApplicationSessionEpoch}->{second.ApplicationSessionEpoch} " +
                $"authorityTicks={first.AuthorityTicks}/{second.AuthorityTicks}");
            RetireCurrentSession();
            ReturnToMainMenu("F0.2 lifecycle smoke passed.");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"F0.2 local composition failed: {exception}");
            RetireCurrentSession();
            FailSession(exception.Message);
            GetTree().Quit(1);
        }
    }

    public override void _Process(double delta)
    {
        if (_cabin is null) return;
        ClientShellSnapshot shell = ShellSnapshot();
        _cabin.SetGameplayInputAllowed(shell.Focus.GameplayInputAllowed);
    }

    public override void _ExitTree()
    {
        RetireCurrentSession();
        base._ExitTree();
    }

    protected override void BeginCapability(ClientCapability capability)
    {
        if (capability != ClientCapability.SinglePlayer)
        {
            base.BeginCapability(capability);
            return;
        }
        try
        {
            StartLocalCabin(
                CabinContentVariant.Default, CabinHumanBody.XBot, useConceptDemoWorld: true);
        }
        catch (Exception exception)
        {
            RetireCurrentSession();
            FailSession(exception.Message);
        }
    }

    protected override void RetireCurrentSession()
    {
        if (_cabin is not null)
        {
            _cabin.Shutdown();
            WorldRoot.RemoveChild(_cabin);
            _cabin.Free();
            _cabin = null;
        }
        if (_catalogMarker is not null)
        {
            ContentCatalogRoot.RemoveChild(_catalogMarker);
            _catalogMarker.Free();
            _catalogMarker = null;
        }
    }

    protected override void OnEffectiveSettingsChanged(ResolvedClientSettings settings) =>
        _cabin?.ApplySettings(settings);

    private void StartLocalCabin(
        CabinContentVariant variant,
        CabinHumanBody human,
        bool useConceptDemoWorld)
    {
        if (_cabin is not null)
            throw new InvalidOperationException("A local Cabin session is already active.");
        BeginSessionLoading("Starting local Cabin authority…");
        CabinContentOracle oracle = CabinContentOracle.Create();
        CabinResolvedContent resolved = variant switch
        {
            CabinContentVariant.Default => oracle.Default,
            CabinContentVariant.Replace => oracle.Replace,
            CabinContentVariant.Add => oracle.Add,
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        _catalogMarker = new Node { Name = $"CabinCatalog_{variant}_{resolved.Catalog.Fingerprint}" };
        ContentCatalogRoot.AddChild(_catalogMarker);

        var cabin = new LocalCabinClient { Name = "LocalCabinSession" };
        cabin.Configure(
            oracle,
            resolved,
            ProjectSettings.GlobalizePath(useConceptDemoWorld
                ? "user://single-player/concept-demo-d0-0-saves"
                : "user://single-player/cabin-saves"),
            HudLayer,
            WorldUiLayer,
            human,
            EffectiveSettings,
            useConceptDemoWorld);
        _cabin = cabin;
        WorldRoot.AddChild(cabin);
        LocalCabinPresentationSnapshot snapshot = cabin.GetPresentationSnapshot();
        if (!snapshot.IsInitialized)
            throw new InvalidOperationException(snapshot.InitializationError ??
                "The local Cabin consumer did not initialize.");
        CommitSession(useConceptDemoWorld
            ? $"Concept Demo — {human}"
            : $"Cabin — {variant} — {human}");
    }

    private static CabinContentVariant ReadVariant()
    {
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (!argument.StartsWith("--cabin-content=", StringComparison.Ordinal)) continue;
            return argument["--cabin-content=".Length..] switch
            {
                "default" => CabinContentVariant.Default,
                "replace" => CabinContentVariant.Replace,
                "add" => CabinContentVariant.Add,
                _ => throw new InvalidOperationException($"Unknown Cabin content argument '{argument}'."),
            };
        }
        return CabinContentVariant.Default;
    }

    private static CabinHumanBody ReadHuman()
    {
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (!argument.StartsWith("--human=", StringComparison.Ordinal)) continue;
            return argument["--human=".Length..] switch
            {
                "x" => CabinHumanBody.XBot,
                "y" => CabinHumanBody.YBot,
                _ => throw new InvalidOperationException($"Unknown human calibration argument '{argument}'."),
            };
        }
        return CabinHumanBody.XBot;
    }

    private void ValidateEffectiveSettings(in LocalCabinPresentationSnapshot snapshot)
    {
        if (snapshot.ControlGrammar != EffectiveSettings.ControlGrammar ||
            snapshot.CameraProfile != EffectiveSettings.CameraProfile ||
            !Mathf.IsEqualApprox(snapshot.CameraDistance, EffectiveSettings.CameraDistance) ||
            !Mathf.IsEqualApprox(snapshot.CameraFovDegrees, EffectiveSettings.CameraFovDegrees) ||
            snapshot.RenderDistanceChunks != Math.Min(
                EffectiveSettings.RenderDistanceChunks, GamePlayableWorld.MaximumChunk) ||
            !EffectiveSettings.TryGetBinding(
                ClientSemanticAction.Jump, out ClientPhysicalInput jump) ||
            jump.Kind != ClientPhysicalInputKind.Keyboard || jump.Code != "SPACE")
            throw new InvalidOperationException(
                "The local Cabin consumer did not apply the persistent shell settings.");
    }
}
