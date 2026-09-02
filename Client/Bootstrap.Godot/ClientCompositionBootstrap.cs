using System.Collections.ObjectModel;
using Godot;

namespace UltraSim.Client.Bootstrap.Godot;

/// <summary>Names one physically composed client capability exposed by the bootstrap menu.</summary>
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
        Frozen(ClientCapability.SinglePlayer, ClientCapability.Multiplayer, ClientCapability.Settings, ClientCapability.Exit));

    private static ReadOnlyCollection<ClientCapability> Frozen(params ClientCapability[] capabilities) =>
        Array.AsReadOnly(capabilities);
}

/// <summary>Shared minimal F0.0a bootstrap; concrete roots provide a fixed compile-time manifest.</summary>
public abstract partial class ClientCompositionBootstrap : Control
{
    protected abstract ClientCompositionManifest Composition { get; }

    public override void _Ready()
    {
        ClientCompositionManifest composition = Composition;
        Validate(composition);
        BuildMenu(composition);
        string capabilities = string.Join(',', composition.Capabilities);
        GD.Print($"ULTRASIM CLIENT READY edition={composition.Edition} capabilities={capabilities}");
        if (OS.GetCmdlineUserArgs().Contains("--composition-smoke", StringComparer.Ordinal))
        {
            GD.Print($"F0.0a COMPOSITION SMOKE PASS edition={composition.Edition} capabilities={capabilities}");
            GetTree().Quit(0);
        }
    }

    private void BuildMenu(in ClientCompositionManifest composition)
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color(0.035f, 0.05f, 0.045f) };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        column.AddThemeConstantOverride("separation", 12);
        center.AddChild(column);
        column.AddChild(new Label
        {
            Text = $"UltraSim — {composition.Edition}",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        foreach (ClientCapability capability in composition.Capabilities)
        {
            var button = new Button { Text = Label(capability), CustomMinimumSize = new Vector2(340, 44) };
            if (capability == ClientCapability.Exit)
                button.Pressed += () => GetTree().Quit(0);
            else
                button.Pressed += () => GD.Print($"F0.0a placeholder selected: {capability}");
            column.AddChild(button);
        }
    }

    private static string Label(ClientCapability capability) => capability switch
    {
        ClientCapability.SinglePlayer => "Single Player",
        ClientCapability.Multiplayer => "Multiplayer / Direct Connect",
        ClientCapability.Settings => "Settings",
        ClientCapability.Exit => "Exit",
        _ => throw new ArgumentOutOfRangeException(nameof(capability)),
    };

    private static void Validate(in ClientCompositionManifest composition)
    {
        if (string.IsNullOrWhiteSpace(composition.Edition) || composition.Capabilities is null ||
            composition.Capabilities.Count == 0 ||
            composition.Capabilities[^1] != ClientCapability.Exit ||
            composition.Capabilities.Distinct().Count() != composition.Capabilities.Count)
            throw new InvalidOperationException("The executable composition manifest is invalid.");
    }
}
