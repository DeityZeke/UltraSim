using Godot;
using UltraSim.Client.Core;

namespace UltraSim.Client.Godot;

public abstract partial class ClientRoot
{
    /// <summary>Exercises real Settings controls and their Save callbacks, not just the resolver.</summary>
    protected async Task RunF112SettingsAcceptance()
    {
        ClientSettingsDocument original = _settings;
        ResolvedClientDisplayConfiguration originalDisplay = _display;
        Node world = WorldRoot;
        SubViewport output = UiComposition.GameplayViewport;
        try
        {
            await Open();
            Control<CheckBox>("DisplayShowAdvanced").ButtonPressed = true;
            Control<SpinBox>("DisplayApplicationWidth").Value = 1920;
            Control<SpinBox>("DisplayApplicationHeight").Value = 1080;
            Control<OptionButton>("DisplayFrameMode").Select((int)ClientGameplayFrameMode.Custom - 1);
            Control<SpinBox>("DisplayFrameWidth").Value = 1280;
            Control<SpinBox>("DisplayFrameHeight").Value = 720;
            Control<SpinBox>("DisplayOutputWidth").Value = 1280;
            Control<SpinBox>("DisplayOutputHeight").Value = 720;
            Control<SpinBox>("DisplayInternalScale").Value = 50;
            Control<HSlider>("SettingsUiScale").Value = 1.25;
            Control<HSlider>("SettingsRenderDistance").Value = 3;
            await Save();
            if (_display.SettingsMode != ClientDisplaySettingsMode.Advanced ||
                _display.InternalRenderResolution != ClientPixelSize.Create(640, 360) ||
                _display.GameplayFrame.Frame.Width != 1280 || _resolved.RenderDistanceChunks != 3 ||
                !Mathf.IsEqualApprox(_resolved.UiScale, 1.25f))
                throw new InvalidOperationException("Product Advanced Settings did not realize saved controls.");

            await Open();
            Control<CheckBox>("DisplayShowAdvanced").ButtonPressed = false;
            OptionButton aspect = Control<OptionButton>("DisplayAspect");
            aspect.Select((int)ClientDisplayAspectPreset.FourThree - 1);
            aspect.EmitSignal(OptionButton.SignalName.ItemSelected, aspect.Selected);
            await Save();
            if (_display.SettingsMode != ClientDisplaySettingsMode.Simple ||
                _display.InternalRenderScalePercent != 100 ||
                _display.GameplayOutputResolution != _display.ApplicationResolution ||
                3 * _display.ApplicationResolution.Width != 4 * _display.ApplicationResolution.Height)
                throw new InvalidOperationException("Advanced hidden settings leaked through Simple relock.");

            foreach (ClientDisplayMode mode in Enum.GetValues<ClientDisplayMode>())
            {
                await Open();
                Control<OptionButton>("DisplayWindowMode").Select((int)mode - 1);
                await Save();
                if (_display.DisplayMode != mode ||
                    _display.GameplayOutputResolution != _display.ApplicationResolution)
                    throw new InvalidOperationException("Window mode changed logical output identity.");
            }
            if (!ReferenceEquals(world, WorldRoot) || !ReferenceEquals(output, UiComposition.GameplayViewport))
                throw new InvalidOperationException("Settings replaced presentation owner roots.");
        }
        finally
        {
            // Restore the exact user-owned document; never leave acceptance preferences enabled.
            _settingsStore!.Save(original);
            _settings = original;
            ApplyDisplay(originalDisplay);
            ResolveEffectiveSettings();
            if (_shell.Snapshot().Screen == ClientShellScreen.Settings) _shell.CloseSettings();
            if (_shell.Snapshot().Screen == ClientShellScreen.InSessionOptions) _shell.ResumeSession();
            RenderCurrentSurface();
        }

        async Task Open()
        {
            _shell.OpenSettings();
            RenderCurrentSurface();
            await Frame();
        }
        async Task Save()
        {
            Control<Button>("SettingsSave").EmitSignal(BaseButton.SignalName.Pressed);
            await Frame();
            await Frame();
            if (_shell.Snapshot().Screen != ClientShellScreen.InSessionOptions)
                throw new InvalidOperationException("Settings Save failed to return to active product.");
            _shell.ResumeSession();
            RenderCurrentSurface();
        }
        async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        T Control<T>(string name) where T : Node =>
            FindChild(name, recursive: true, owned: false) as T ??
                throw new InvalidOperationException($"Settings product control {name} missing.");
    }
}
