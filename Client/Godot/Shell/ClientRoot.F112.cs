using Godot;
using UltraSim.Client.Core;

namespace UltraSim.Client.Godot;

public abstract partial class ClientRoot
{
    /// <summary>Actual product Settings + Save, with caller-owned native ray nomination after each realization.</summary>
    protected async Task<object> RunF112PointerSettingsAcceptance(Func<Task<object>> pointer)
    {
        ClientSettingsDocument original = _settings;
        ResolvedClientDisplayConfiguration originalDisplay = _display;
        var rows = new List<object>(4);
        try
        {
            for (int index = 0; index < 4; index++)
            {
                _shell.OpenSettings(); RenderCurrentSurface(); await Frame();
                C<OptionButton>("DisplayWindowMode").Select((int)ClientDisplayMode.Windowed - 1);
                C<CheckBox>("DisplayShowAdvanced").ButtonPressed = index >= 2;
                OptionButton aspect = C<OptionButton>("DisplayAspect");
                aspect.Select((int)(index == 1 ? ClientDisplayAspectPreset.FourThree :
                    ClientDisplayAspectPreset.SixteenNine) - 1);
                aspect.EmitSignal(OptionButton.SignalName.ItemSelected, aspect.Selected);
                OptionButton resolution = C<OptionButton>("DisplayResolution");
                IReadOnlyList<ClientPixelSize> presets = ClientDisplayPresetCatalog.ForAspect(
                    index == 1 ? ClientDisplayAspectPreset.FourThree : ClientDisplayAspectPreset.SixteenNine);
                int selected = 0;
                for (int item = 0; item < presets.Count; item++)
                    if (presets[item] == ClientPixelSize.Create(index == 1 ? 1600 : 1920, index == 1 ? 1200 : 1080)) selected = item;
                resolution.Select(selected);
                C<SpinBox>("DisplayApplicationWidth").Value = 1920;
                C<SpinBox>("DisplayApplicationHeight").Value = 1080;
                C<OptionButton>("DisplayFrameMode").Select((int)ClientGameplayFrameMode.Custom - 1);
                C<SpinBox>("DisplayFrameWidth").Value = index == 2 ? 1280 : 1920;
                C<SpinBox>("DisplayFrameHeight").Value = index == 2 ? 720 : 1080;
                C<SpinBox>("DisplayOutputWidth").Value = index == 2 ? 960 : 1920;
                C<SpinBox>("DisplayOutputHeight").Value = index == 2 ? 540 : 1080;
                C<SpinBox>("DisplayInternalScale").Value = index == 3 ? 50 : 100;
                C<Button>("SettingsSave").EmitSignal(BaseButton.SignalName.Pressed);
                await Frame(); await Frame();
                if (_shell.Snapshot().Screen != ClientShellScreen.InSessionOptions)
                    throw new InvalidOperationException("Pointer matrix failed actual Settings Save.");
                _shell.ResumeSession(); RenderCurrentSurface(); await Frame();
                rows.Add(new { index, Display = _display, Workspace = UiComposition.GetSnapshot(), Results = await pointer() });
            }
            // Explicit device preference is user-owned even when a remote profile is active.
            _shell.OpenSettings(); RenderCurrentSurface(); await Frame();
            C<CheckBox>("DisplayShowAdvanced").ButtonPressed = true;
            C<CheckBox>("SettingsExperimentalVegetation").ButtonPressed = true;
            C<Button>("SettingsSave").EmitSignal(BaseButton.SignalName.Pressed);
            await Frame(); await Frame();
            if (!_settings.ExperimentalVegetation || !_settingsStore!.Load().Document.ExperimentalVegetation)
                throw new InvalidOperationException("Vegetation Settings did not persist through Save.");
            _shell.ResumeSession(); RenderCurrentSurface();
            return new { rows, VegetationSaved = true, AppliesNextEntry = true };
        }
        finally
        {
            _settingsStore!.Save(original); _settings = original;
            ApplyDisplay(originalDisplay); ResolveEffectiveSettings();
            if (_shell.Snapshot().Screen == ClientShellScreen.Settings) _shell.CloseSettings();
            if (_shell.Snapshot().Screen == ClientShellScreen.InSessionOptions) _shell.ResumeSession();
            RenderCurrentSurface();
        }
        async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        T C<T>(string name) where T : Node => FindChild(name, true, false) as T ??
            throw new InvalidOperationException($"Missing actual product control {name}.");
    }
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
            await Frame();
            CheckBox vegetation = Control<CheckBox>("SettingsExperimentalVegetation");
            CheckBox advanced = Control<CheckBox>("DisplayShowAdvanced");
            if (!vegetation.IsVisibleInTree() || vegetation.GetParent() != advanced.GetParent() ||
                vegetation.GetIndex() != advanced.GetIndex() + 1 || !vegetation.Text.Contains("grass"))
                throw new InvalidOperationException("Advanced grass preference is not discoverable beside Show Advanced.");
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
            if (Control<CheckBox>("SettingsExperimentalVegetation").Visible)
                throw new InvalidOperationException("Experimental grass preference leaked into Simple Settings.");
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
