using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Content;
using UltraSim.Client.Core;

namespace UltraSim.Client.Godot;

public abstract partial class LocalPlayClientRoot
{
    private async void RunF112ProductDogfood()
    {
        GodotClientSessionBoundarySnapshot baseline = CaptureSessionBoundary();
        var lifetimes = new List<object>(3);
        var samples = new List<object>(360);
        var epochs = new HashSet<ulong>();
        string mode = ReadLocalArgument("--f1-12-art=") ?? "installed";
        int seconds = int.TryParse(ReadLocalArgument("--f1-12-seconds="), out int requested)
            ? requested : 40;
        try
        {
            if (seconds is < 4 or > 120 || mode is not ("installed" or "absent" or "primitive") ||
                string.IsNullOrWhiteSpace(ReadLocalArgument("--f1-12-save-root=")))
                throw new InvalidOperationException("F1.12 requires bounded duration/art and an isolated save root.");
            for (int lifetime = 0; lifetime < 3; lifetime++)
            {
                RequireNode<Button>("Capability_SinglePlayer").EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(2);
                RequireNode<LineEdit>("GuestDisplayName").Text = $"F1.12 Guest {lifetime}";
                RequireNode<Button>("GuestJoin").EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(3);
                LocalCabinClient cabin = _cabin ?? throw new InvalidOperationException("Product guest entry failed.");
                LocalCabinPresentationSnapshot initial = cabin.GetPresentationSnapshot();
                if (!epochs.Add(initial.ApplicationSessionEpoch))
                    throw new InvalidOperationException("Product re-entry reused retired application epoch.");
                if (mode == "installed" && !initial.OfficialPresentation.Available ||
                    mode == "absent" && (initial.OfficialPresentation.Available || initial.Actors.AnimatedActors != 0) ||
                    mode == "primitive" && (initial.Actors.AnimatedActors != 0 ||
                        initial.Actors.PrimitiveFallbackActors != initial.Actors.ActiveActors))
                    throw new InvalidOperationException("Actual installed/absent art did not match requested product.");
                GD.Print($"F1.12 START lifetime={lifetime} art={mode}");
                // Existing maintained mechanics; administrative relocations only choose probe starting points.
                cabin.RunD03Acceptance();
                await Frames(3);
                cabin.ValidateD03TooltipAfterNativeLayout();
                cabin.RunD00Acceptance();
                cabin.RunF112ProfileAcceptance();
                cabin.RunF112CabinAcceptance();
                object farSave = cabin.RunF112SaveLoadAcceptance(far: true);
                object centralSave = cabin.RunF112SaveLoadAcceptance(far: false);
                cabin.RunF112ItemAcceptance();
                cabin.RunD02Acceptance();
                cabin.PrepareF112CentralControl();
                cabin.RunD01Acceptance();
                LocalF16DerivedAcceptanceSnapshot? derived = mode == "primitive"
                    ? null : cabin.RunF16DerivedAcceptance();
                // D0.3 deliberately freezes the physics pump for its native tooltip-layout probe.
                // Return to ordinary product servicing before asynchronous Chat/Settings/soak work.
                cabin.SetPhysicsProcess(true);
                await RunChatFocusSmoke(cabin);
                ulong appBeforeSettings = cabin.GetPresentationSnapshot().ApplicationSessionEpoch;
                await RunF112SettingsAcceptance();
                if (cabin.GetPresentationSnapshot().ApplicationSessionEpoch != appBeforeSettings)
                    throw new InvalidOperationException("Product Settings replaced active Game session.");
                // Normalize source/location/window for a stable bounded trend window.
                cabin.PrepareF112CentralControl();
                cabin.RunD01Acceptance();
                await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
                LocalCabinPresentationSnapshot stable = cabin.GetPresentationSnapshot();
                long started = Stopwatch.GetTimestamp();
                int churn = 0;
                for (int second = 0; second < seconds; second++)
                {
                    if (second % 4 == 0)
                    {
                        for (int page = 0; page < 4; page++)
                            _UnhandledInput(new InputEventKey
                            { Keycode = Key.F11, PhysicalKeycode = Key.F11, Pressed = true });
                        if (CaptureSessionBoundary().DebugPage != GodotClientDebugPage.Off)
                            throw new InvalidOperationException("Debug did not cycle back to Off.");
                        cabin.RunF17UiAcceptance();
                        churn++;
                    }
                    await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
                    LocalCabinPresentationSnapshot current = cabin.GetPresentationSnapshot();
                    // Public truth continues to arrive/move during this real product window. Its
                    // nodes/resources may change legitimately; isolate stable structural ownership.
                    if (current.Renderer.ActiveNodes - current.Renderer.ActivePublicNodes !=
                            stable.Renderer.ActiveNodes - stable.Renderer.ActivePublicNodes ||
                        current.Renderer.ActiveResources - current.Renderer.ActivePublicBatchGroups !=
                            stable.Renderer.ActiveResources - stable.Renderer.ActivePublicBatchGroups ||
                        current.Renderer.ActiveRenderChunks != stable.Renderer.ActiveRenderChunks ||
                        current.Actors.ActiveActors != stable.Actors.ActiveActors || current.OpenUiViews != 0 ||
                        current.DerivedResources.LiveTextures != stable.DerivedResources.LiveTextures ||
                        current.DerivedResources.LiveMaterials != stable.DerivedResources.LiveMaterials)
                        throw new InvalidOperationException("Stable product/UI churn changed live presentation ownership: " +
                            $"nodes={stable.Renderer.ActiveNodes}/{current.Renderer.ActiveNodes}, " +
                            $"resources={stable.Renderer.ActiveResources}/{current.Renderer.ActiveResources}, " +
                            $"actors={stable.Actors.ActiveActors}/{current.Actors.ActiveActors}, ui={current.OpenUiViews}, " +
                            $"textures={stable.DerivedResources.LiveTextures}/{current.DerivedResources.LiveTextures}, " +
                            $"materials={stable.DerivedResources.LiveMaterials}/{current.DerivedResources.LiveMaterials}; " +
                            $"beforeRenderer={stable.Renderer}; afterRenderer={current.Renderer}.");
                    using Process process = Process.GetCurrentProcess();
                    samples.Add(new { lifetime, second, ElapsedSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds,
                        ManagedBytes = GC.GetTotalMemory(false), process.WorkingSet64,
                        Gen0 = GC.CollectionCount(0), Gen1 = GC.CollectionCount(1), Gen2 = GC.CollectionCount(2),
                        current.SemanticResidentChunks, current.RenderedChunks, current.Actors,
                        current.Renderer, current.DerivedResources, current.PresentationCache,
                        ProcessFrameMilliseconds = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000d,
                        UiChurnCycles = churn });
                }
                lifetimes.Add(new { lifetime, Initial = initial, Final = cabin.GetPresentationSnapshot(),
                    centralSave, farSave, derived, UiChurnCycles = churn,
                    StableWindowSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds });
                // Leave with focused text + live views to attack real retirement, not already-empty state.
                cabin.PrepareF07RetirementCanary($"F1.12 Guest {lifetime}", CabinHumanBody.XBot);
                RetireCurrentSession();
                ReturnToMainMenu("F1.12 product lifetime retired.");
                await Frames(3);
                RequireCleanSessionBoundary(in baseline, $"F1.12 lifetime {lifetime}");
                GD.Print($"F1.12 LIFETIME PASS lifetime={lifetime} uiCycles={churn} retirement=clean");
            }
            StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot,
                useConceptDemoWorld: true, guestDisplayName: "F1.12 Reload Guest");
            if (!epochs.Add(_cabin!.GetPresentationSnapshot().ApplicationSessionEpoch))
                throw new InvalidOperationException("Re-entry Load reused a retired application epoch.");
            object reentryLoad = _cabin!.RunF112ReentryLoadAcceptance();
            RetireCurrentSession();
            ReturnToMainMenu("F1.12 re-entry Load retired.");
            await Frames(3);
            RequireCleanSessionBoundary(in baseline, "F1.12 re-entry Load");
            string path = ReadLocalArgument("--f1-12-evidence=") ??
                throw new InvalidOperationException("F1.12 needs an evidence output path.");
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Schema = "client-f1.12-product-dogfood/v1", SourceHead = ReadLocalArgument("--f1-12-head="),
                Edition = GetSnapshot().Edition, ArtMode = mode, DisplayBackend = DisplayServer.GetName(),
                Renderer = RenderingServer.GetCurrentRenderingMethod(), VegetationOptIn =
                    OS.GetCmdlineUserArgs().Contains("--f1-11-vegetation"),
                Baseline = baseline, lifetimes, reentryLoad, samples, FinalBoundary = CaptureSessionBoundary(),
                HumanFeelRated = false,
            }, new JsonSerializerOptions { WriteIndented = true,
                Converters = { new JsonStringEnumConverter() } }));
            GD.Print("F1.12 PRODUCT DOGFOOD PASS lifetimes=3 settings=product " +
                "saveLoad=central/far item=pickup/equip/drop/repickup retirement=clean");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"F1.12 product dogfood failed: {exception}");
            RetireCurrentSession();
            GetTree().Quit(1);
        }
        async Task Frames(int count)
        {
            for (int index = 0; index < count; index++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
