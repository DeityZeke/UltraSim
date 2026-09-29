using UltraSim.Framework.ControlPlane;

namespace UltraSim.Client.Godot;

public abstract partial class ClientRoot
{
    private readonly ControlPlaneRegistry _adapterControlPlane = new();
    private GodotAdapterControlProvider? _adapterControlProvider;
    private ulong _adapterControlGeneration;

    /// <summary>Cold owner-thread publication; no adapter selection is mutable through it.</summary>
    protected ControlPlaneRegistry AdapterControlPlane => _adapterControlPlane;

    private void RegisterAdapterControl()
    {
        if (_adapterControlProvider is not null)
            throw new InvalidOperationException("Godot adapter diagnostics are already registered.");
        var provider = new GodotAdapterControlProvider(() => new GodotAdapterStatus(
            _composition.Edition, ApplicationPhase, _shell.Snapshot().SessionActive,
            _worldRoot?.GetChildCount() ?? 0,
            (_worldUiLayer is null ? 0 : 1) + (_hudLayer is null ? 0 : 1) +
            (_menuLayer is null ? 0 : 1) + (_debugLayer is null ? 0 : 1) +
            (_contextLayer is null ? 0 : 1) + (_workspaceLayer is null ? 0 : 1)));
        if (!_adapterControlPlane.TryRegister(provider, out _adapterControlGeneration, out string reason))
            throw new InvalidOperationException($"Godot adapter Control Plane publication failed: {reason}.");
        _adapterControlProvider = provider;
    }

    private void RequireAdapterControl()
    {
        if (_adapterControlProvider is null || _adapterControlGeneration == 0)
            throw new InvalidOperationException("Godot adapter Control Plane publication is absent.");
        var principal = new ControlPrincipal("client-native-smoke", ControlPrincipalKind.TestAutomation,
            [GodotAdapterControlProvider.Read]);
        if (_adapterControlPlane.Read(principal, GodotAdapterControlProvider.Id,
                _adapterControlGeneration, new DiagnosticId("edition"), out var edition) != ControlResultCode.Accepted ||
            edition?.Value.AsText() != _composition.Edition ||
            _adapterControlPlane.Read(principal, GodotAdapterControlProvider.Id,
                _adapterControlGeneration, new ControlId("presentation-implementation"), out var selected) !=
                ControlResultCode.Accepted ||
            selected?.Effective.AsOption().Value != "godot")
            throw new InvalidOperationException("Godot adapter Control Plane selection disagrees with the client composition.");
    }

    private void RetireAdapterControl()
    {
        if (_adapterControlProvider is null) return; // Partial startup never published it.
        if (!_adapterControlPlane.Retire(GodotAdapterControlProvider.Id, _adapterControlGeneration))
            throw new InvalidOperationException("Godot adapter Control Plane generation did not retire.");
        _adapterControlProvider = null;
        _adapterControlGeneration = 0;
    }
}
