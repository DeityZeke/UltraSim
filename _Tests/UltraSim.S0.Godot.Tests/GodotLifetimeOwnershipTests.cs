using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Framework.ControlPlane;

internal static class GodotLifetimeOwnershipTests
{
    internal static void Run()
    {
        var resources = new GodotOwnedResources();
        var node = (MeshInstance3D)GodotPrimitiveSubstrate.CreateActor(ClientActorVisualKind.HumanX, resources);
        Mesh retained = node.Mesh ?? throw new InvalidOperationException("Primitive mesh is missing.");
        Expect(resources.Count == 2, "Primitive mesh and material need an explicit owner.");
        node.Free();
        Expect(retained.NativeInstance != IntPtr.Zero, "Consumer retirement is not wrapper disposal.");
        resources.Dispose();
        resources.Dispose();
        Expect(resources.IsRetired && resources.Count == 0 && retained.NativeInstance == IntPtr.Zero,
            "Owned primitive wrappers did not retire exactly once.");

        var tooltipParent = new Control();
        var tooltip = new GodotWorldHoverTooltip(tooltipParent);
        StyleBoxFlat retainedTooltipStyle = tooltip.DiagnosticBackground;
        Expect(retainedTooltipStyle.NativeInstance != IntPtr.Zero,
            "Tooltip style was not live before owner retirement.");
        tooltip.Dispose();
        tooltipParent.Free();
        Expect(retainedTooltipStyle.NativeInstance == IntPtr.Zero,
            "Owned tooltip StyleBoxFlat was left to finalization.");

        var lifetime = new GodotApplicationLifetime();
        lifetime.BeginStartup();
        Expect(lifetime.RequestStop(0), "Startup close was not accepted.");
        lifetime.CompleteStartup();
        Expect(lifetime.Phase == GodotApplicationPhase.StopRequested && !lifetime.CanStartWork,
            "Startup completion resurrected a stopping application.");
        Expect(!lifetime.RequestStop(3) && lifetime.ExitCode == 3, "Duplicate close lost failure status.");
        Expect(lifetime.BeginStop() && !lifetime.BeginStop(), "Retirement was not exclusive.");
        Expect(!lifetime.ShutdownCompletion.IsCompleted, "Shutdown completed before owner retirement.");
        lifetime.CompleteStop();
        Expect(lifetime.ShutdownCompletion.IsCompletedSuccessfully, "Shutdown completion was not published.");

        AdapterControlPublication();
    }

    private static void AdapterControlPublication()
    {
        var current = new GodotAdapterStatus("Complete", GodotApplicationPhase.Starting, false, 4, 6);
        var provider = new GodotAdapterControlProvider(() => current);
        var registry = new ControlPlaneRegistry();
        Expect(registry.TryRegister(provider, out ulong generation, out _), "Native adapter publication was rejected.");
        var reader = new ControlPrincipal("native-reader", ControlPrincipalKind.TestAutomation,
            [GodotAdapterControlProvider.Read, GodotAdapterControlProvider.Select]);
        var denied = new ControlPrincipal("no-adapter-read", ControlPrincipalKind.TestAutomation, []);
        Expect(registry.Discover(denied).Count == 0 &&
            registry.Read(denied, GodotAdapterControlProvider.Id, generation,
                new DiagnosticId("phase"), out _) == ControlResultCode.Unauthorized,
            "Adapter state was visible without the read capability.");
        Expect(registry.Read(reader, GodotAdapterControlProvider.Id, generation,
                new ControlId("presentation-implementation"), out var presentation) == ControlResultCode.Accepted &&
            presentation?.Effective.AsOption().Value == "godot" && !presentation.Writable,
            "Physical presentation selection was not fixed/read-only.");
        Expect(registry.Read(reader, GodotAdapterControlProvider.Id, generation,
                new ControlId("audio-implementation"), out var audio) == ControlResultCode.Accepted &&
            audio?.Effective.AsOption().Value == "none",
            "The unimplemented Audio adapter was falsely published as active.");
        var attempt = registry.Set(new SetControlRequest(
            new(new("p3-adapter-selection"), reader), GodotAdapterControlProvider.Id,
            new("presentation-implementation"), generation, ControlValue.Option(new("godot"))));
        Expect(attempt.Outcome.Code == ControlResultCode.NotWritable,
            "A physical adapter was accepted as a live runtime setting.");
        ulong before = provider.Revision;
        current = current with { Phase = GodotApplicationPhase.Running, SessionActive = true, WorldRootChildren = 5 };
        Expect(registry.Read(reader, GodotAdapterControlProvider.Id, generation,
                new DiagnosticId("session-active"), out var session) == ControlResultCode.Accepted &&
            session?.Value.AsBool() == true && provider.Revision > before,
            "Adapter diagnostic did not track the owner lifecycle/session change.");
        Expect(registry.Retire(GodotAdapterControlProvider.Id, generation), "Adapter publication did not retire.");
        Expect(registry.Read(reader, GodotAdapterControlProvider.Id, generation,
                new DiagnosticId("phase"), out _) == ControlResultCode.UnknownProvider,
            "Retired adapter diagnostics stayed reachable.");
        Expect(registry.TryRegister(new GodotAdapterControlProvider(() => current),
                out ulong replacement, out _) && replacement > generation &&
            !registry.Retire(GodotAdapterControlProvider.Id, generation) &&
            registry.Retire(GodotAdapterControlProvider.Id, replacement),
            "A stale adapter generation retired its replacement.");
    }

    internal static ArrayMesh[] CaptureTerrain(Node root) => Capture(root,
        name => name == "BaseTerrain" || name.StartsWith("AdditionalTerrain_", StringComparison.Ordinal));

    internal static ArrayMesh[] CaptureActorTools(Node root) => Capture(root,
        name => name is "PrimitiveBlade" or "PrimitiveGuard" or "PrimitiveGrip" or "AcceptedInteractCue");

    internal static void RequireRetired(ArrayMesh[] meshes, string boundary)
    {
        Expect(meshes.Length != 0, $"{boundary}: test retained no owned meshes.");
        foreach (ArrayMesh mesh in meshes)
            Expect(mesh.NativeInstance == IntPtr.Zero, $"{boundary}: owned ArrayMesh was left to finalization.");
    }

    private static ArrayMesh[] Capture(Node root, Func<string, bool> include)
    {
        var meshes = new List<ArrayMesh>();
        Visit(root);
        return meshes.ToArray();
        void Visit(Node node)
        {
            if (node is MeshInstance3D instance && include(node.Name.ToString()) && instance.Mesh is ArrayMesh mesh)
                meshes.Add(mesh);
            for (int index = 0; index < node.GetChildCount(); index++) Visit(node.GetChild(index));
        }
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
