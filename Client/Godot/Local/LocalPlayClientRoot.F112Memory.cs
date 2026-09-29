using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using UltraSim.Client.BuiltInTestWorlds;

namespace UltraSim.Client.Godot;

public abstract partial class LocalPlayClientRoot
{
    private async void RunF112MemoryControl()
    {
        GodotClientSessionBoundarySnapshot baseline = CaptureSessionBoundary();
        var samples = new List<object>(64);
        int lifetimes = int.TryParse(ReadLocalArgument("--f1-12-lifetimes="), out int count) ? count : 8;
        int seconds = int.TryParse(ReadLocalArgument("--f1-12-seconds="), out int duration) ? duration : 15;
        string path = ReadLocalArgument("--f1-12-evidence=") ?? "/tmp/ultrasim-f112-memory.json";
        try
        {
            if (lifetimes is < 6 or > 10 || seconds is < 4 or > 60 ||
                ReadLocalArgument("--f1-12-save-root=") is null)
                throw new InvalidOperationException("Memory control requires bounded lifetimes/window and isolated save root.");
            await AwaitF112Boundary(GetTree(), SceneTree.SignalName.ProcessFrame);
            Sample(-1, "shell-initial", null);
            for (int lifetime = 0; lifetime < lifetimes; lifetime++)
            {
                StartLocalCabin(CabinContentVariant.Default, CabinHumanBody.XBot, useConceptDemoWorld: true,
                    guestDisplayName: $"R1 Memory {lifetime}");
                _cabin!.SetF112StableRadius(1);
                await Settle();
                Sample(lifetime, "live-start", _cabin.GetPresentationSnapshot());
                for (int point = 0; point < 3; point++)
                {
                    if (OS.GetCmdlineUserArgs().Contains("--f1-12-memory-ui")) _cabin.RunF17UiAcceptance();
                    await ToSignal(GetTree().CreateTimer(seconds / 3d), SceneTreeTimer.SignalName.Timeout);
                    Sample(lifetime, $"live-{point}", _cabin.GetPresentationSnapshot());
                }
                RetireCurrentSession();
                ReturnToMainMenu("F1.12-R1 memory lifetime retired.");
                await Settle();
                RequireCleanSessionBoundary(in baseline, $"F1.12-R1 memory {lifetime}");
                Sample(lifetime, "retired-settled", null);
                Flush(); // bounded evidence retained even if a later lifetime fails
            }
            // Separate diagnostic experiment, only after the ordinary campaign. Not product cleanup.
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Settle();
            Sample(lifetimes, "retired-forced-gc-diagnostic", null);
            Flush();
            GD.Print($"F1.12-R1 MEMORY PASS lifetimes={lifetimes} radius=1 retirement=clean");
            RequestApplicationExit(0);
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString()); RetireCurrentSession(); Flush(); RequestApplicationExit(1);
        }
        void Sample(int lifetime, string phase, LocalCabinPresentationSnapshot? presentation) =>
            samples.Add(new { lifetime, phase, Memory = GodotDiagnosticProcessMemory.Capture(), presentation,
                Boundary = CaptureSessionBoundary(), Workspace = UiComposition.GetSnapshot() });
        void Flush() => File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Schema = "client-f1.12-r1-memory/v1", SourceHead = ReadLocalArgument("--f1-12-head="),
            Display = DisplayServer.GetName(), Art = ReadLocalArgument("--f1-12-art=") ?? "installed",
            lifetimes, seconds, Radius = 1, Churn = OS.GetCmdlineUserArgs().Contains("--f1-12-memory-ui"), samples,
        }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
        async Task Settle()
        {
            for (int frame = 0; frame < 8; frame++) await AwaitF112Boundary(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
        }
    }
}
