using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;

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
