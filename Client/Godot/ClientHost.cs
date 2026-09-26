using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
#if ULTRASIM_ONLINE || ULTRASIM_COMPLETE
using UltraSim.Client.Network;
#endif

namespace UltraSim.Client.Application.Godot;

/// <summary>
/// Fixed Godot scene identity compiled into one of three physical client dependency products.
/// MSBuild composition, not runtime feature hiding, owns which capability assemblies exist.
/// </summary>
public partial class ClientHost :
#if ULTRASIM_SINGLE_PLAYER || ULTRASIM_COMPLETE
    LocalPlayClientRoot
#else
    ClientRoot
#endif
{
#if ULTRASIM_SINGLE_PLAYER
    public const string CompiledEdition = "SinglePlayer";
#elif ULTRASIM_ONLINE
    public const string CompiledEdition = "Online";
#elif ULTRASIM_COMPLETE
    public const string CompiledEdition = "Complete";
#else
#error The UltraSim client host requires exactly one compiled edition.
#endif

    protected override ClientCompositionManifest Composition
    {
        get
        {
#if ULTRASIM_SINGLE_PLAYER
            return ClientCompositionManifest.SinglePlayer;
#elif ULTRASIM_ONLINE
            _ = typeof(RemoteWorldStateClient);
            return ClientCompositionManifest.Online;
#else
            _ = typeof(RemoteWorldStateClient);
            return ClientCompositionManifest.Complete;
#endif
        }
    }

    protected override string SessionPresentationDiagnostics()
    {
#if ULTRASIM_ONLINE || ULTRASIM_COMPLETE
        if (_remotePlayable is not null)
            return _remotePlayable.GetSnapshot().Renderer.ToDiagnosticText() + "\n" +
                _remotePlayable.GetSnapshot().Actors.ToDiagnosticText();
#endif
        return base.SessionPresentationDiagnostics();
    }

    public override void _Ready()
    {
        try
        {
            ValidateProductIdentity();
        }
        catch (Exception exception)
        {
            GD.PushError($"Client product composition mismatch: {exception.Message}");
            GetTree().Quit(1);
            return;
        }
        base._Ready();
#if ULTRASIM_COMPLETE
        if (OS.GetCmdlineUserArgs().Contains("--f0-7-smoke", StringComparer.Ordinal))
            Callable.From(RunF07MixedSessionLifecycleSmoke).CallDeferred();
#endif
    }

    // Godot's native script dispatch generator binds lifecycle overrides from the canonical
    // native-derived class source. Keep these overrides here and delegate capability-specific
    // work to partial implementation files; a lifecycle override in a secondary partial file can
    // compile normally yet remain invisible to native dispatch.
    public override void _Process(double delta)
    {
        base._Process(delta);
#if ULTRASIM_ONLINE || ULTRASIM_COMPLETE
        ProcessRemote(delta);
#endif
    }

    public override void _ExitTree()
    {
#if ULTRASIM_ONLINE || ULTRASIM_COMPLETE
        ExitRemote();
#endif
        base._ExitTree();
    }

    private static void ValidateProductIdentity()
    {
        string? expectedArgument = OS.GetCmdlineUserArgs()
            .FirstOrDefault(static argument =>
                argument.StartsWith("--expect-edition=", StringComparison.Ordinal));
        if (expectedArgument is not null)
        {
            string expected = expectedArgument["--expect-edition=".Length..];
            if (!string.Equals(expected, CompiledEdition, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"command expected '{expected}', compiled product is '{CompiledEdition}'.");
        }

        bool singlePlayer = OS.HasFeature("ultrasim_single_player");
        bool online = OS.HasFeature("ultrasim_online");
        bool complete = OS.HasFeature("ultrasim_complete");
        int exportedProductFeatures = (singlePlayer ? 1 : 0) + (online ? 1 : 0) + (complete ? 1 : 0);
        if (exportedProductFeatures == 0)
            return; // Normal editor and non-exported development launch.
        if (exportedProductFeatures != 1)
            throw new InvalidOperationException("export must contain exactly one UltraSim product feature.");

        string exportedEdition = singlePlayer ? "SinglePlayer" : online ? "Online" : "Complete";
        if (!string.Equals(exportedEdition, CompiledEdition, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"export feature is '{exportedEdition}', compiled product is '{CompiledEdition}'.");
    }
}
