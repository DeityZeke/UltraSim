using Godot;
using UltraSim.Client.Bootstrap.Godot;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Core;

namespace UltraSim.Client.Local.Godot;

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
        if (OS.GetCmdlineUserArgs().Contains("--composition-smoke", StringComparer.Ordinal) ||
            !OS.GetCmdlineUserArgs().Contains("--f0-2-smoke", StringComparer.Ordinal))
            return;
        try
        {
            CabinContentVariant variant = ReadVariant();
            CabinHumanBody human = ReadHuman();
            StartLocalCabin(variant, human);
            LocalCabinPresentationSnapshot first = _cabin!.GetPresentationSnapshot();
            ValidateEffectiveSettings(first);
            _cabin.RunF02Acceptance();

            RetireCurrentSession();
            ReturnToMainMenu("First local session retired.");
            if (WorldRoot.HasNode("LocalCabinSession") ||
                ContentCatalogRoot.GetChildCount() != 0)
                throw new InvalidOperationException("Local session presentation did not retire cleanly.");

            StartLocalCabin(variant, human);
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
            StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot);
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

    private void StartLocalCabin(CabinContentVariant variant, CabinHumanBody human)
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
            ProjectSettings.GlobalizePath("user://single-player/cabin-saves"),
            HudLayer,
            WorldUiLayer,
            human,
            EffectiveSettings);
        _cabin = cabin;
        WorldRoot.AddChild(cabin);
        LocalCabinPresentationSnapshot snapshot = cabin.GetPresentationSnapshot();
        if (!snapshot.IsInitialized)
            throw new InvalidOperationException(snapshot.InitializationError ??
                "The local Cabin consumer did not initialize.");
        CommitSession($"Cabin — {variant} — {human}");
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
            !Mathf.IsEqualApprox(snapshot.CameraFovDegrees, EffectiveSettings.CameraFovDegrees))
            throw new InvalidOperationException(
                "The local Cabin consumer did not apply the persistent shell settings.");
    }
}
