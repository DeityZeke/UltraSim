using Godot;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Core;
using UltraSim.Game.Admission;
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
    private string _guestAdmissionName = string.Empty;
    private GameGuestBody _guestAdmissionBody = GameGuestBody.HumanFemale;
    private string? _guestAdmissionError;

    public override void _Ready()
    {
        base._Ready();
        string[] arguments = OS.GetCmdlineUserArgs();
        bool conceptDemoSmoke = arguments.Contains("--d0-0-smoke", StringComparer.Ordinal);
        bool residencySmoke = arguments.Contains("--d0-1-smoke", StringComparer.Ordinal);
        bool farIslandSmoke = arguments.Contains("--d0-2-smoke", StringComparer.Ordinal);
        bool signSmoke = arguments.Contains("--d0-3-smoke", StringComparer.Ordinal);
        bool fallbackLabSmoke = arguments.Contains("--d0-4-smoke", StringComparer.Ordinal);
        bool guestAdmissionSmoke = arguments.Contains("--d0-5-smoke", StringComparer.Ordinal);
        bool f02Smoke = arguments.Contains("--f0-2-smoke", StringComparer.Ordinal);
        if (arguments.Contains("--composition-smoke", StringComparer.Ordinal) ||
            !conceptDemoSmoke && !residencySmoke && !farIslandSmoke && !signSmoke &&
            !fallbackLabSmoke && !guestAdmissionSmoke && !f02Smoke)
            return;
        try
        {
            if (guestAdmissionSmoke)
            {
                RunGuestAdmissionSmoke();
                return;
            }
            if (signSmoke || fallbackLabSmoke)
            {
                StartLocalCabin(
                    CabinContentVariant.Default, CabinHumanBody.XBot, useConceptDemoWorld: true);
                RunPresentationSmokeAfterNativeFrames(fallbackLabSmoke);
                return;
            }
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

    private async void RunPresentationSmokeAfterNativeFrames(bool fallbackLab)
    {
        try
        {
            // MultiMesh render bounds are committed by RenderingServer frames, not by semantic
            // construction in _Ready. Cross that native boundary before claiming visible output.
            for (int frame = 0; frame < 3; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            LocalCabinPresentationSnapshot evidence = fallbackLab
                ? _cabin!.RunD04Acceptance()
                : _cabin!.RunD03Acceptance();
            if (evidence.AuthoredChunks != GamePlayableWorld.ConceptChunkCount ||
                evidence.SemanticResidentChunks >= evidence.AuthoredChunks ||
                evidence.RenderedChunks >= evidence.SemanticResidentChunks)
                throw new InvalidOperationException(
                    "The Concept Demo did not preserve authored > resident > rendered structure.");
            GD.Print(
                (fallbackLab ? "D0.4 FALLBACK LAB PASS " : "D0.3 INFORMATIONAL SIGN PASS ") +
                $"player={evidence.PlayerPosition} authored/resident/rendered=" +
                $"{evidence.AuthoredChunks}/{evidence.SemanticResidentChunks}/" +
                $"{evidence.RenderedChunks} radius={evidence.RenderDistanceChunks} " +
                $"authorityTicks={evidence.AuthorityTicks} content={evidence.ContentFingerprint}");
            RetireCurrentSession();
            ReturnToMainMenu(fallbackLab
                ? "Concept Demo D0.4 Fallback Lab smoke passed."
                : "Concept Demo D0.3 informational-sign smoke passed.");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"D0.{(fallbackLab ? 4 : 3)} native presentation failed: {exception}");
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
        _guestAdmissionName = string.Empty;
        _guestAdmissionBody = GameGuestBody.HumanFemale;
        _guestAdmissionError = null;
        BeginAdmission("Choose a temporary guest identity for this demo session.");
    }

    protected override void BuildCapabilityAdmissionSurface(in ClientShellSnapshot snapshot)
    {
        VBoxContainer column = CreateCenteredColumn(440);
        column.AddChild(Title("Enter Demo"));
        column.AddChild(CenteredLabel(
            "This name and body exist only for the current session. No account or saved character is created."));
        column.AddChild(new Label { Text = "Name" });
        var name = new LineEdit
        {
            Name = "GuestDisplayName",
            Text = _guestAdmissionName,
            PlaceholderText = "1–24 characters",
            MaxLength = GameGuestAdmissionPolicy.MaximumDisplayNameUtf8Bytes,
            SelectAllOnFocus = false,
        };
        column.AddChild(name);
        column.AddChild(new Label { Text = "Body" });
        var body = new OptionButton { Name = "GuestBody" };
        body.AddItem("X / Human Female", (int)GameGuestBody.HumanFemale);
        body.AddItem("Y / Human Male", (int)GameGuestBody.HumanMale);
        body.Select(_guestAdmissionBody == GameGuestBody.HumanMale ? 1 : 0);
        column.AddChild(body);
        if (!string.IsNullOrWhiteSpace(_guestAdmissionError))
        {
            column.AddChild(new Label
            {
                Name = "GuestAdmissionError",
                Text = _guestAdmissionError,
                Modulate = new Color(1f, 0.48f, 0.42f),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        }
        Button join = MenuButton("Join");
        join.Name = "GuestJoin";
        ConnectDeferred(join, () => SubmitGuestAdmission(name.Text, (GameGuestBody)body.GetSelectedId()));
        name.TextSubmitted += _ => join.EmitSignal(BaseButton.SignalName.Pressed);
        column.AddChild(join);
        Button back = MenuButton("Back");
        back.Name = "GuestBack";
        ConnectDeferred(back, () => ReturnToMainMenu());
        column.AddChild(back);
        name.GrabFocus();
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
        bool useConceptDemoWorld,
        string? guestDisplayName = null)
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
            useConceptDemoWorld,
            guestDisplayName);
        _cabin = cabin;
        WorldRoot.AddChild(cabin);
        LocalCabinPresentationSnapshot snapshot = cabin.GetPresentationSnapshot();
        if (!snapshot.IsInitialized)
            throw new InvalidOperationException(snapshot.InitializationError ??
                "The local Cabin consumer did not initialize.");
        CommitSession(useConceptDemoWorld
            ? $"Concept Demo — {snapshot.DisplayName} — {human}"
            : $"Cabin — {variant} — {human}");
    }

    private void SubmitGuestAdmission(string name, GameGuestBody body)
    {
        _guestAdmissionName = name;
        _guestAdmissionBody = body;
        var request = new GameGuestAdmissionRequest(body, name);
        if (!GameGuestAdmissionPolicy.TryNormalize(
                request, out GameGuestIdentity identity,
                out _, out string? message))
        {
            _guestAdmissionError = message;
            BeginAdmission(message ?? "The guest identity was rejected.");
            return;
        }
        try
        {
            _guestAdmissionError = null;
            StartLocalCabin(
                CabinContentVariant.Default,
                identity.Body == GameGuestBody.HumanFemale
                    ? CabinHumanBody.XBot
                    : CabinHumanBody.YBot,
                useConceptDemoWorld: true,
                identity.DisplayName);
        }
        catch (Exception exception)
        {
            RetireCurrentSession();
            FailSession(exception.Message);
        }
    }

    private async void RunGuestAdmissionSmoke()
    {
        try
        {
            RequireNode<Button>("Capability_SinglePlayer").EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (ShellSnapshot().Screen != ClientShellScreen.Admission || _cabin is not null)
                throw new InvalidOperationException("Single Player did not stop at guest admission.");

            RequireNode<LineEdit>("GuestDisplayName").Text = "\t";
            RequireNode<Button>("GuestJoin").EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (ShellSnapshot().Screen != ClientShellScreen.Admission || _cabin is not null ||
                FindChild("GuestAdmissionError", recursive: true, owned: false) is not Label)
                throw new InvalidOperationException(
                    "Rejected guest input advanced or failed to produce bounded feedback.");

            RequireNode<LineEdit>("GuestDisplayName").Text = "  Demo Éowyn  ";
            OptionButton body = RequireNode<OptionButton>("GuestBody");
            body.Select(1);
            RequireNode<Button>("GuestJoin").EmitSignal(BaseButton.SignalName.Pressed);
            for (int frame = 0; frame < 3; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (ShellSnapshot().Screen != ClientShellScreen.InSession || _cabin is null)
                throw new InvalidOperationException("A valid guest did not enter the local world.");
            LocalCabinPresentationSnapshot evidence = _cabin.RunD05Acceptance(
                "Demo Éowyn", CabinHumanBody.YBot);
            await RunChatFocusSmoke(_cabin);
            GD.Print(
                $"D0.5 GUEST ADMISSION PASS name={evidence.DisplayName} body={evidence.HumanBody} " +
                $"player={evidence.PlayerPosition} authorityTicks={evidence.AuthorityTicks} " +
                "chatFocus=pass");
            RetireCurrentSession();
            ReturnToMainMenu("Concept Demo D0.5 guest admission smoke passed.");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"D0.5 guest admission failed: {exception}");
            RetireCurrentSession();
            FailSession(exception.Message);
            GetTree().Quit(1);
        }
    }

    private async Task RunChatFocusSmoke(LocalCabinClient cabin)
    {
        LineEdit chat = RequireNode<LineEdit>("ChatInput");
        LocalChatInputSnapshot before = cabin.GetChatInputSnapshot();
        chat.Text = "keyboard submit canary";
        chat.GrabFocus();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (cabin.GetChatInputSnapshot() is not { HasFocus: true, GameplayInputAllowed: false })
            throw new InvalidOperationException(
                "Chat focus did not become the shell's exclusive text-input owner.");

        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = Key.Enter,
            PhysicalKeycode = Key.Enter,
            Pressed = true,
        });
        LocalChatInputSnapshot submitted = default;
        for (int frame = 0; frame < 240; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            submitted = cabin.GetChatInputSnapshot();
            if (submitted.AppliedOccurrences == before.AppliedOccurrences + 1) break;
        }
        if (submitted.HasFocus || submitted.Draft.Length != 0 ||
            submitted.AppliedOccurrences != before.AppliedOccurrences + 1)
            throw new InvalidOperationException(
                "Enter did not submit and retire the focused Chat draft exactly once.");

        chat.Text = "preserved draft";
        chat.GrabFocus();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = Key.Escape,
            PhysicalKeycode = Key.Escape,
            Pressed = true,
        });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        LocalChatInputSnapshot escaped = cabin.GetChatInputSnapshot();
        if (escaped.HasFocus || escaped.Draft != "preserved draft" ||
            ShellSnapshot().Screen != ClientShellScreen.InSession)
            throw new InvalidOperationException(
                "Escape did not release Chat focus locally while preserving the draft.");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!cabin.GetChatInputSnapshot().GameplayInputAllowed)
            throw new InvalidOperationException(
                "Gameplay input did not resume after Chat released focus.");

        chat.Text = "click-away draft";
        chat.GrabFocus();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Rect2 chatBounds = chat.GetGlobalRect();
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = new Vector2(chatBounds.Position.X, chatBounds.Position.Y - 32),
        });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        LocalChatInputSnapshot clickedAway = cabin.GetChatInputSnapshot();
        if (clickedAway.HasFocus || clickedAway.Draft != "click-away draft" ||
            ShellSnapshot().Screen != ClientShellScreen.InSession)
            throw new InvalidOperationException(
                "Clicking outside Chat did not release focus while preserving the draft.");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!cabin.GetChatInputSnapshot().GameplayInputAllowed)
            throw new InvalidOperationException(
                "Gameplay input did not resume after clicking away from Chat.");

        chat.Text = string.Empty;
    }

    private T RequireNode<T>(string name) where T : Node =>
        FindChild(name, recursive: true, owned: false) as T ??
        throw new InvalidOperationException($"Required guest-admission node '{name}' was missing.");

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
