using Godot;
using UltraSim.Framework.ControlPlane;
using UltraSim.Client.ControlPlane;
using UltraSim.Client.Core;

namespace UltraSim.Client.Godot;

public abstract partial class ClientRoot
{
    private readonly ControlPlaneRegistry _adapterControlPlane = new();
    private GodotAdapterControlProvider? _adapterControlProvider;
    private ulong _adapterControlGeneration;
    private ClientSettingsControlProvider? _settingsControlProvider;
    private ulong _settingsControlGeneration;
    private ClientSettingsScopeKey _publishedSettingsScope;
    private ClientProfileRecommendation? _publishedRecommendation;
    private ClientProfileApplicationMode _publishedConsent;
    private ControlPlaneViewer? _userManagementViewer;
    private GodotControlPlaneView? _userManagementSurface;
    private ControlPlaneViewer? _developerManagementViewer;
    private GodotControlPlaneView? _developerManagementSurface;
    private string _managementStatus = "";
    private bool _showDeveloperManagement;

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
            (_contextLayer is null ? 0 : 1) + (_workspaceLayer is null ? 0 : 1),
            _uiComposition?.GetSnapshot().Gumps.OpenWindows ?? 0,
            _display.InternalRenderResolution.Width,
            _display.InternalRenderResolution.Height));
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
        RetireSettingsControl();
        if (_adapterControlProvider is null) return; // Partial startup never published it.
        if (!_adapterControlPlane.Retire(GodotAdapterControlProvider.Id, _adapterControlGeneration))
            throw new InvalidOperationException("Godot adapter Control Plane generation did not retire.");
        _adapterControlProvider = null;
        _adapterControlGeneration = 0;
    }

    private void EnsureSettingsControl()
    {
        if (_settingsControlProvider is not null && _publishedSettingsScope == _activeSettingsScope &&
            ReferenceEquals(_publishedRecommendation, _activeProfileRecommendation) &&
            _publishedConsent == _activeProfileMode) return;
        RetireSettingsControl();
        var provider = new ClientSettingsControlProvider(() => _settings, () => _resolved,
            CommitManagementSettings, _activeSettingsScope, _activeProfileRecommendation, _activeProfileMode,
            () => _display);
        if (!_adapterControlPlane.TryRegister(provider, out _settingsControlGeneration, out string reason))
            throw new InvalidOperationException($"Client settings publication failed: {reason}.");
        _settingsControlProvider = provider;
        _publishedSettingsScope = _activeSettingsScope;
        _publishedRecommendation = _activeProfileRecommendation;
        _publishedConsent = _activeProfileMode;
        var principal = new ControlPrincipal("local-user", ControlPrincipalKind.LocalUser,
            [ClientSettingsControlProvider.Read, ClientSettingsControlProvider.Write]);
        _userManagementViewer = new ControlPlaneViewer(_adapterControlPlane, principal);
        _userManagementSurface = new GodotControlPlaneView(_userManagementViewer, result =>
        {
            _managementStatus = result.Outcome.Status == ControlResultStatus.Accepted
                ? "Management change applied."
                : $"Management request rejected: {result.Outcome.Code} {result.Outcome.Diagnostic}";
            RenderCurrentSurface();
        });
        var developer = new ControlPrincipal("local-developer", ControlPrincipalKind.Developer,
            [GodotAdapterControlProvider.Read, ClientSettingsControlProvider.Read]);
        _developerManagementViewer = new ControlPlaneViewer(_adapterControlPlane, developer);
        _developerManagementSurface = new GodotControlPlaneView(_developerManagementViewer, result =>
        {
            _managementStatus = $"Developer request: {result.Outcome.Code}";
            RenderCurrentSurface();
        });
    }

    private void RetireSettingsControl()
    {
        if (_settingsControlProvider is null) return;
        if (!_adapterControlPlane.Retire(_settingsControlProvider.Definition.Id, _settingsControlGeneration))
            throw new InvalidOperationException("Client settings publication did not retire.");
        _settingsControlProvider = null;
        _settingsControlGeneration = 0;
        _userManagementViewer = null;
        _userManagementSurface = null;
        _developerManagementViewer = null;
        _developerManagementSurface = null;
    }

    private void CommitManagementSettings(ClientSettingsDocument candidate)
    {
        ResolvedClientDisplayConfiguration prior = _display;
        if (candidate.Display != _settings.Display)
            ApplyDisplay(ClientDisplayResolver.Resolve(candidate.Display));
        try { _settingsStore!.Save(candidate); }
        catch
        {
            if (_display != prior) ApplyDisplay(prior);
            throw;
        }
        _settings = candidate;
        _settingsLoadStatus = ClientSettingsLoadStatus.Loaded;
        ResolveEffectiveSettings();
    }

    private async Task RunP5ManagementSmokeAsync()
    {
        ClientSettingsDocument original = _settings;
        ResolvedClientDisplayConfiguration originalDisplay = _display;
        int exit = 0;
        try
        {
            _shell.OpenSettings();
            RenderCurrentSurface();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var user = FindChild("UserManagementSurface", true, false) as VBoxContainer ??
                throw new InvalidOperationException("F10 did not expose the generic User viewer.");
            var row = user.FindChild("ManagementControl_ui-scale", true, false) as HBoxContainer ??
                throw new InvalidOperationException("UI scale was not published by the Settings owner.");
            SpinBox editor = row.GetChildren().OfType<SpinBox>().Single();
            Button apply = row.GetChildren().OfType<Button>().Single();
            double targetScale = original.Global.UiScale == 1.25f ? 1.5 : 1.25;
            editor.Value = targetScale;
            apply.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Math.Abs(_resolved.UiScale - targetScale) > .001 ||
                !_settingsStore!.Load().Document.Global.UiScale.HasValue)
                throw new InvalidOperationException("Generic User control failed owner persistence/effective parity.");
            var developerToggle = FindChild("DeveloperManagementToggle", true, false) as Button ??
                throw new InvalidOperationException("Developer diagnostics were not discoverable.");
            developerToggle.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (FindChild("DeveloperManagementSurface", true, false) is not VBoxContainer)
                throw new InvalidOperationException("Developer diagnostics did not appear on request.");
            GD.Print("P5 MANAGEMENT NATIVE SMOKE PASS user=settings persisted=owner developer=read-only");
        }
        catch (Exception failure)
        {
            exit = 1;
            GD.PushError($"P5 MANAGEMENT NATIVE SMOKE FAIL: {failure}");
        }
        finally
        {
            try
            {
                _settingsStore!.Save(original);
                _settings = original;
                if (_display != originalDisplay) ApplyDisplay(originalDisplay);
                ResolveEffectiveSettings();
            }
            catch (Exception failure)
            {
                exit = 1;
                GD.PushError($"P5 MANAGEMENT RESTORE FAIL: {failure}");
            }
            finally { RequestApplicationExit(exit); }
        }
    }
}
