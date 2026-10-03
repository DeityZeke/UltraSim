using System.Diagnostics;
using System.Text.Json;
using Godot;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Core;

namespace UltraSim.Client.Godot;

public abstract partial class LocalPlayClientRoot
{
    /// <summary>Exported-product entry control; uses the same staged Join path as the menu.</summary>
    private async void RunClientEntryStudy(string scenario)
    {
        string stage = "setup";
        try
        {
            if (scenario is not ("official" or "primitive" or "cancel-authority" or
                "cancel-mesh" or "cancel-world" or "fail-content"))
                throw new ArgumentException("Unknown client-entry study scenario.");
            string directory = ReadLocalArgument("--client-entry-evidence=") ??
                throw new ArgumentException("--client-entry-evidence=<directory> is required.");
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException("Entry frame evidence requires native rendering.");
            for (int index = 0; index < 3; index++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!GetWindow().HasFocus())
                throw new InvalidOperationException("The exported client is not focused.");
            GodotClientSessionBoundarySnapshot baseline = CaptureSessionBoundary();
            ResetClientFrameIntervalsForDiagnostic();
            for (int index = 0; index < 2; index++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var elapsed = Stopwatch.StartNew();
            var observed = new List<object>();
            ClientWorldEntryStage last = ClientWorldEntryStage.None;
            bool cancelled = false;
            stage = "staged entry";
            StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot,
                useConceptDemoWorld: true,
                primitiveOnlyPresentation: scenario == "primitive",
                stagedEntry: true);
            for (int frame = 0; frame < 1800; frame++)
            {
                ClientWorldEntryProgress progress = _lastEntryProgress;
                if (progress.Stage != last && progress.Stage != ClientWorldEntryStage.None)
                {
                    ClientFrameIntervalSummary current = CaptureClientFrameIntervalsForDiagnostic();
                    observed.Add(new { Stage = progress.Stage.ToString(), progress.Detail,
                        ElapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
                        MaxFrameSoFarMilliseconds = current.MaximumMilliseconds });
                    last = progress.Stage;
                }
                ClientShellScreen screen = ShellSnapshot().Screen;
                if (screen is ClientShellScreen.InSession or ClientShellScreen.MainMenu or
                    ClientShellScreen.Error && frame > 0)
                    break;
                if (!cancelled && (scenario == "cancel-authority" &&
                        progress.Stage == ClientWorldEntryStage.StartingAuthority ||
                    scenario == "cancel-mesh" &&
                        progress.Stage == ClientWorldEntryStage.PreparingMeshes ||
                    scenario == "cancel-world" &&
                        progress.Stage == ClientWorldEntryStage.BuildingWorld))
                {
                    CancelSessionLoading();
                    cancelled = true;
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (frame == 1799) throw new TimeoutException("Staged entry did not terminate.");
            }
            double readyWall = elapsed.Elapsed.TotalMilliseconds;
            for (int index = 0; index < 2; index++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ClientFrameIntervalSummary frames = CaptureClientFrameIntervalsForDiagnostic();
            LocalCabinPresentationSnapshot session = _cabin?.GetPresentationSnapshot() ?? default;
            ClientShellScreen terminal = ShellSnapshot().Screen;
            bool expectsCancellation = scenario.StartsWith("cancel-", StringComparison.Ordinal);
            if (expectsCancellation && (terminal != ClientShellScreen.MainMenu ||
                _cabin is not null || !cancelled))
                throw new InvalidOperationException("Cancellation retained an active world.");
            if (scenario == "fail-content" && (terminal != ClientShellScreen.Error ||
                _cabin is not null))
                throw new InvalidOperationException("Injected failure retained an active world.");
            if (!expectsCancellation && scenario != "fail-content" &&
                (terminal != ClientShellScreen.InSession || !session.IsInitialized ||
                    session.RenderedChunks == 0 || session.SemanticResidentChunks == 0 ||
                    session.Actors.ActiveActors == 0))
                throw new InvalidOperationException("Ready was published without a playable world.");
            bool gameplayAccepted = false;
            if (terminal == ClientShellScreen.InSession &&
                OS.GetCmdlineUserArgs().Contains("--client-entry-verify-gameplay"))
            {
                stage = "post-Ready gameplay acceptance";
                _cabin!.RunD00Acceptance();
                gameplayAccepted = true;
            }
            bool retryReady = false;
            if (expectsCancellation)
            {
                stage = "retry after cancellation";
                StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot,
                    useConceptDemoWorld: true, stagedEntry: true);
                for (int frame = 0; frame < 1800 &&
                     ShellSnapshot().Screen == ClientShellScreen.Loading; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (frame == 1799) throw new TimeoutException("Post-cancel entry did not terminate.");
                }
                LocalCabinPresentationSnapshot retry = _cabin?.GetPresentationSnapshot() ?? default;
                retryReady = ShellSnapshot().Screen == ClientShellScreen.InSession &&
                    retry.IsInitialized && retry.RenderedChunks != 0 &&
                    retry.Actors.ActiveActors != 0;
                if (!retryReady)
                    throw new InvalidOperationException("A cancelled entry poisoned the next admission.");
            }
            if (_cabin is not null)
            {
                RetireCurrentSession();
                ReturnToMainMenu("Entry control retired.");
            }
            else if (terminal == ClientShellScreen.Error)
                ReturnToMainMenu("Failure control retired.");
            GodotClientSessionBoundarySnapshot residue = CaptureSessionBoundary();
            if (residue.WorldChildren != baseline.WorldChildren ||
                residue.ContentCatalogChildren != baseline.ContentCatalogChildren ||
                residue.OpenGumpWindows != 0 || residue.Shell.SessionActive)
                throw new InvalidOperationException(
                    $"World entry retained transient ownership: world={residue.WorldChildren}/{baseline.WorldChildren} " +
                    $"catalog={residue.ContentCatalogChildren}/{baseline.ContentCatalogChildren} gumps={residue.OpenGumpWindows} " +
                    $"session={residue.Shell.SessionActive}.");
            File.WriteAllText(Path.Combine(directory, $"{scenario}.json"),
                JsonSerializer.Serialize(new
                {
                    Format = "UltraSimClientWorldEntry/v1",
                    Scenario = scenario,
                    SourceHead = System.Environment.GetEnvironmentVariable("ULTRASIM_COMMIT") ?? "unrecorded",
                    ReadyWallMilliseconds = readyWall,
                    MaxEligibleFrameMilliseconds = frames.MaximumMilliseconds,
                    frames.Samples,
                    frames.Over50Milliseconds,
                    frames.Over100Milliseconds,
                    TerminalScreen = terminal.ToString(),
                    RetryReady = retryReady,
                    GameplayAccepted = gameplayAccepted,
                    Stages = observed,
                    Ready = new { session.IsInitialized, session.RenderedChunks,
                        session.SemanticResidentChunks, Actors = session.Actors.ActiveActors,
                        session.ApplicationSessionEpoch, session.ControlSessionEpoch },
                    Retired = new { residue.WorldChildren, residue.ContentCatalogChildren,
                        residue.OpenGumpWindows, residue.Shell.SessionActive },
                }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"CLIENT WORLD ENTRY STUDY PASS scenario={scenario} " +
                $"ready_ms={readyWall:0.000} max_frame_ms={frames.MaximumMilliseconds:0.000} " +
                $"samples={frames.Samples} stages={observed.Count}");
            RequestApplicationExit();
        }
        catch (Exception error)
        {
            GD.PushError($"CLIENT WORLD ENTRY STUDY FAIL stage={stage}: {error}");
            RetireCurrentSession();
            RequestApplicationExit(1);
        }
    }
}
