using System.Collections.ObjectModel;

namespace UltraSim.Client.Core;

/// <summary>Names one physically composed client capability exposed by the application shell.</summary>
public enum ClientCapability : byte
{
    SinglePlayer = 1,
    Multiplayer = 2,
    Settings = 3,
    Exit = 4,
}

/// <summary>Immutable capability catalog supplied by one concrete executable root.</summary>
public readonly record struct ClientCompositionManifest(
    string Edition,
    ReadOnlyCollection<ClientCapability> Capabilities)
{
    public static ClientCompositionManifest SinglePlayer { get; } = new(
        "Single Player", Frozen(ClientCapability.SinglePlayer, ClientCapability.Settings, ClientCapability.Exit));

    public static ClientCompositionManifest Online { get; } = new(
        "Online", Frozen(ClientCapability.Multiplayer, ClientCapability.Settings, ClientCapability.Exit));

    public static ClientCompositionManifest Complete { get; } = new(
        "Complete",
        Frozen(ClientCapability.SinglePlayer, ClientCapability.Multiplayer,
            ClientCapability.Settings, ClientCapability.Exit));

    private static ReadOnlyCollection<ClientCapability> Frozen(params ClientCapability[] capabilities) =>
        Array.AsReadOnly(capabilities);
}
