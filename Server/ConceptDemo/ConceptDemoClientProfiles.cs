using UltraSim.Game.Protocol;

namespace UltraSim.Server.ConceptDemo;

public static class ConceptDemoClientProfiles
{
    public static RemoteClientProfileAdvertisement Create(string name)
    {
        bool orbit = string.Equals(name, "orbit", StringComparison.OrdinalIgnoreCase);
        bool direct = string.Equals(name, "third-person", StringComparison.OrdinalIgnoreCase);
        bool independent = string.Equals(name, "independent", StringComparison.OrdinalIgnoreCase);
        if (!orbit && !direct && !independent)
            throw new ArgumentException(
                "--client-profile must be orbit, third-person, or independent.", nameof(name));

        RemoteClientControlGrammar grammar = orbit
            ? RemoteClientControlGrammar.OrbitCommand
            : independent
                ? RemoteClientControlGrammar.IndependentMoveLook
                : RemoteClientControlGrammar.ThirdPersonDirect;
        int distance = orbit ? 14_000 : direct ? 8_500 : 12_000;
        int fov = direct ? 760 : 700;
        bool free = !independent;
        var camera = new RemoteClientCameraRig(
            RemoteClientCameraProjection.Perspective,
            RemoteClientCameraTarget.ControlledActor,
            RemoteClientCameraFollow.Immediate,
            free ? RemoteClientCameraAxisMode.Free : RemoteClientCameraAxisMode.Locked,
            free ? RemoteClientCameraAxisMode.Free : RemoteClientCameraAxisMode.Locked,
            free ? RemoteClientCameraZoom.Stepped : RemoteClientCameraZoom.Disabled,
            1_600, 3_000, distance, 55_000,
            independent ? -450 : -401,
            orbit ? 450 : independent ? 353 : 195,
            115, 773, 344, free ? 2_000 : 0, fov, 20_000);
        string select = orbit ? "LEFT" : "MIDDLE";
        string destination = orbit ? "LEFT_DOUBLE" : "MIDDLE_DOUBLE";
        string primary = orbit ? "MIDDLE" : "LEFT";
        RemoteClientProfileBinding[] bindings =
        [
            Binding(RemoteClientSemanticAction.MoveLeft, RemoteClientPhysicalInputKind.Keyboard, "A"),
            Binding(RemoteClientSemanticAction.MoveRight, RemoteClientPhysicalInputKind.Keyboard, "D"),
            Binding(RemoteClientSemanticAction.MoveForward, RemoteClientPhysicalInputKind.Keyboard, "W"),
            Binding(RemoteClientSemanticAction.MoveBackward, RemoteClientPhysicalInputKind.Keyboard, "S"),
            Binding(RemoteClientSemanticAction.Select, RemoteClientPhysicalInputKind.MouseButton, select),
            Binding(RemoteClientSemanticAction.Destination, RemoteClientPhysicalInputKind.MouseButton, destination),
            Binding(RemoteClientSemanticAction.ContextMenu, RemoteClientPhysicalInputKind.MouseButton, "RIGHT"),
            Binding(RemoteClientSemanticAction.Primary, RemoteClientPhysicalInputKind.MouseButton, primary),
            Binding(RemoteClientSemanticAction.Interact, RemoteClientPhysicalInputKind.Keyboard, "E"),
            Binding(RemoteClientSemanticAction.OpenInventory, RemoteClientPhysicalInputKind.Keyboard, "I"),
            Binding(RemoteClientSemanticAction.Chat, RemoteClientPhysicalInputKind.Keyboard, "ENTER"),
            Binding(RemoteClientSemanticAction.Jump, RemoteClientPhysicalInputKind.Keyboard, "SPACE"),
            Binding(RemoteClientSemanticAction.CameraLook, RemoteClientPhysicalInputKind.MouseButton, "MIDDLE_DRAG"),
            Binding(RemoteClientSemanticAction.CameraZoomIn, RemoteClientPhysicalInputKind.MouseButton, "WHEELUP"),
            Binding(RemoteClientSemanticAction.CameraZoomOut, RemoteClientPhysicalInputKind.MouseButton, "WHEELDOWN"),
        ];
        if (!RemoteClientProfileAdvertisement.TryCreate(
                revision: 1,
                grammar,
                camera,
                bindings,
                out RemoteClientProfileAdvertisement? profile))
            throw new InvalidOperationException("The built-in Concept Demo client profile is invalid.");
        return profile!;
    }

    private static RemoteClientProfileBinding Binding(
        RemoteClientSemanticAction action,
        RemoteClientPhysicalInputKind kind,
        string code) => new(action, kind, RemoteClientInputModifiers.None, code);
}
