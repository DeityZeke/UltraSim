using System.Diagnostics;
using System.Text.Json;
using Godot;
using UltraSim.Client.BuiltInTestWorlds;

namespace UltraSim.Client.Godot;

public abstract partial class LocalPlayClientRoot
{
    /// <summary>
    /// Opt-in exported-product measurement. GC is sampled at 4 Hz; the ordinary
    /// ClientFrameIntervalSampler remains the only per-frame instrument.
    /// </summary>
    private async void RunClientHitchStudy(string scenario)
    {
        CancellationTokenSource? gcSamplingStop = null;
        Task? gcSampling = null;
        string stage = "setup";
        try
        {
            if (scenario is not ("idle" or "cold" or "move"))
                throw new ArgumentException("The study mode accepts only idle, cold or move controls.");
            string directory = ReadLocalArgument("--client-hitch-evidence=") ??
                throw new ArgumentException("--client-hitch-evidence=<directory> is required.");
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException("The hitch study requires actual native rendering.");

            bool forceDisabled = OS.GetCmdlineUserArgs().Contains(
                "--client-hitch-force-vsync-disabled", StringComparer.Ordinal);
            if (forceDisabled)
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);

            bool showPerformancePage = !OS.GetCmdlineUserArgs().Contains(
                "--client-hitch-hide-debug", StringComparer.Ordinal);

            if (scenario is "idle" or "move")
                StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot,
                    useConceptDemoWorld: true);
            if (showPerformancePage)
                ShowPerformancePageForStudy();
            await WaitWallSeconds(scenario == "cold" ? 2 : 8);
            if (!GetWindow().HasFocus())
                throw new InvalidOperationException("The client window is not focused after warmup.");
            if (showPerformancePage)
                UiComposition.DebugHost.GetNode<Button>(
                    "DebugPerformance/PerformanceBody/PerformanceActions/ResetPerformance")
                    .EmitSignal(Button.SignalName.Pressed);
            else
                ResetClientFrameIntervalsForDiagnostic();
            // Reset discards the previous timestamp. Re-arm it before a synchronous cold entry,
            // otherwise the first post-entry interval (the one that includes startup) is lost.
            if (scenario == "cold")
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            LocalCabinPresentationSnapshot before = _cabin?.GetPresentationSnapshot() ?? default;
            GodotClientPresentationPacingSnapshot pacingAtStart =
                GodotClientPresentationPacingSnapshot.Capture();
            using Process process = Process.GetCurrentProcess();
            TimeSpan cpuBefore = process.TotalProcessorTime;
            TimeSpan gcPauseBefore = GC.GetTotalPauseDuration();
            long allocatedBefore = GC.GetTotalAllocatedBytes(false);
            int[] collectionsBefore = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
            DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
            var clock = Stopwatch.StartNew();
            var gcTimeline = new List<object>(128);
            gcSamplingStop = new CancellationTokenSource();
            gcSampling = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
                while (await timer.WaitForNextTickAsync(gcSamplingStop.Token))
                    gcTimeline.Add(new
                    {
                        Milliseconds = clock.Elapsed.TotalMilliseconds,
                        PauseMilliseconds = (GC.GetTotalPauseDuration() - gcPauseBefore).TotalMilliseconds,
                        Gen0 = GC.CollectionCount(0) - collectionsBefore[0],
                        Gen1 = GC.CollectionCount(1) - collectionsBefore[1],
                        Gen2 = GC.CollectionCount(2) - collectionsBefore[2],
                    });
            });
            if (scenario == "cold")
                StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot,
                    useConceptDemoWorld: true);
            double coldEntryMilliseconds = scenario == "cold" ? clock.Elapsed.TotalMilliseconds : 0;
            if (scenario == "move")
                GD.Print("CLIENT HITCH STUDY MOVEMENT WINDOW: provide real physical movement input now");
            stage = "measurement";
            // Keep the cold first frame inside the 30-second ring even when startup blocks it.
            await WaitWallSeconds(scenario == "cold" ? 20 : 31);
            clock.Stop();
            stage = "window complete";
            DateTimeOffset endedUtc = DateTimeOffset.UtcNow;
            gcSamplingStop.Cancel();
            try { await gcSampling; }
            catch (OperationCanceledException) { }
            if (!GetWindow().HasFocus())
                throw new InvalidOperationException("The client lost focus during the measurement window.");
            GodotClientPresentationPacingSnapshot pacingAtEnd =
                GodotClientPresentationPacingSnapshot.Capture();
            if (pacingAtEnd != pacingAtStart)
                throw new InvalidOperationException(
                    $"Presentation pacing changed during measurement: {pacingAtStart} -> {pacingAtEnd}.");
            TimeSpan cpuAfter = process.TotalProcessorTime;
            TimeSpan gcPauseAfter = GC.GetTotalPauseDuration();
            long allocatedAfter = GC.GetTotalAllocatedBytes(false);
            int[] collectionsAfter = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
            LocalCabinPresentationSnapshot after = (_cabin ?? throw new InvalidOperationException(
                "The Local session retired during the measurement window.")).GetPresentationSnapshot();
            if (scenario == "move" && before.PlayerPosition == after.PlayerPosition)
                throw new InvalidOperationException("The real-input movement control did not move the player.");
            string framesPath = Path.Combine(directory, $"{scenario}.frames.json");
            double frameCaptureStartedMilliseconds = clock.Elapsed.TotalMilliseconds;
            stage = "frame snapshot";
            // Construct the export-only HUD after measurement for hidden-page controls.
            // Its initial Refresh and file serialization cannot perturb the measured window.
            if (!showPerformancePage)
                ShowPerformancePageForStudy();
            GodotClientPerformanceHud hud = UiComposition.DebugHost
                .GetNode<GodotClientPerformanceHud>("DebugPerformance");
            int frames = hud.SaveDiagnosticSnapshot(framesPath);
            using JsonDocument frameFile = JsonDocument.Parse(File.ReadAllText(framesPath));
            double eligibleMs = frameFile.RootElement.GetProperty("Summary")
                .GetProperty("EligibleMilliseconds").GetDouble();
            if (eligibleMs < (scenario == "cold" ? 18_000 : 29_000) || frames < 1000)
                throw new InvalidOperationException(
                    $"The focused sample is incomplete: {frames} frames, {eligibleMs:0} ms.");
            var evidence = new
            {
                Format = "UltraSimClientHitchStudy/v1",
                Scenario = scenario,
                SourceHead = System.Environment.GetEnvironmentVariable("ULTRASIM_COMMIT") ?? "unrecorded",
                StartedUtc = startedUtc,
                EndedUtc = endedUtc,
                WallMilliseconds = clock.Elapsed.TotalMilliseconds,
                ColdEntryMilliseconds = coldEntryMilliseconds,
                FrameCaptureStartedMilliseconds = frameCaptureStartedMilliseconds,
                EligibleMilliseconds = eligibleMs,
                Frames = frames,
                PerformancePageVisibleDuringMeasurement = showPerformancePage,
                ExplicitVSyncDisableRequested = forceDisabled,
                PacingAtStart = pacingAtStart,
                PacingAtEnd = pacingAtEnd,
                VegetationEnabled = frameFile.RootElement.GetProperty("Context")
                    .GetProperty("VegetationEnabled").GetBoolean(),
                ProcessCpuMilliseconds = (cpuAfter - cpuBefore).TotalMilliseconds,
                GcPauseMilliseconds = (gcPauseAfter - gcPauseBefore).TotalMilliseconds,
                AllocatedBytes = allocatedAfter - allocatedBefore,
                Gen0 = collectionsAfter[0] - collectionsBefore[0],
                Gen1 = collectionsAfter[1] - collectionsBefore[1],
                Gen2 = collectionsAfter[2] - collectionsBefore[2],
                GcTimeline = gcTimeline,
                Before = new { before.AuthorityTicks, before.PlayerPosition,
                    before.RenderedChunks, before.SemanticResidentChunks,
                    before.Renderer.ActiveNodes, before.Renderer.ActiveResources },
                After = new { after.AuthorityTicks, after.PlayerPosition,
                    after.RenderedChunks, after.SemanticResidentChunks,
                    after.Renderer.ActiveNodes, after.Renderer.ActiveResources },
            };
            File.WriteAllText(Path.Combine(directory, $"{scenario}.context.json"),
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"CLIENT HITCH STUDY PASS scenario={scenario} frames={frames} " +
                $"eligible_ms={eligibleMs:0} cpu_ms={evidence.ProcessCpuMilliseconds:0} " +
                $"gc={evidence.Gen0}/{evidence.Gen1}/{evidence.Gen2} dir={directory}");
            RetireCurrentSession();
            RequestApplicationExit();
        }
        catch (Exception error)
        {
            GD.PushError($"CLIENT HITCH STUDY FAIL stage={stage}: {error}");
            RetireCurrentSession();
            RequestApplicationExit(1);
        }
        finally
        {
            if (gcSamplingStop is not null)
            {
                gcSamplingStop.Cancel();
                if (gcSampling is not null)
                    try { await gcSampling; }
                    catch (OperationCanceledException) { }
                gcSamplingStop.Dispose();
            }
        }

        async Task WaitWallSeconds(int seconds)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(seconds))
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        void ShowPerformancePageForStudy()
        {
            for (int attempt = 0;
                attempt < 5 && UiComposition.DebugPage != GodotClientDebugPage.Performance;
                attempt++)
                UiComposition.CycleDebug(page => page.ToString());
            if (UiComposition.DebugPage != GodotClientDebugPage.Performance)
                throw new InvalidOperationException("The performance diagnostic page did not open.");
        }
    }
}
