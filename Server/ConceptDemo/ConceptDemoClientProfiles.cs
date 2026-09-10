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
        RemoteClientCameraProfile camera = orbit
            ? RemoteClientCameraProfile.Orbit
            : independent
                ? RemoteClientCameraProfile.IndependentMoveLook
                : RemoteClientCameraProfile.ThirdPersonFollow;
        uint distance = orbit ? 14_000u : direct ? 8_500u : 12_000u;
        ushort fov = direct ? (ushort)760 : (ushort)700;
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
        ];
        if (!RemoteClientProfileAdvertisement.TryCreate(
                revision: 1,
                grammar,
                camera,
                distance,
                cameraFovTenths: fov,
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
