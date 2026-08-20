#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Settings;
using UltraSim.Client.Debug;
using UltraSim.Client.ECS.Systems;
using UltraSim.Client.UI;
using UltraSim.Server.ECS.Systems;

namespace Client.ECS.ControlPanel.Sections
{
    /// <summary>
    /// Consolidated render settings panel – exposes range sliders and debug toggles
    /// for all renderer systems so users don't have to expand multiple system entries.
    /// </summary>
    [ControlPanelSection(defaultOrder: 6, defaultExpanded: true)]
    public partial class RenderingPanel : UIBuilder, IControlPanelSection
    {
        private enum RangeSlider
        {
            Dynamic,
            Static,
            Billboard,
            View
        }

        private sealed class SliderEntry
        {
            public required IntSetting Setting;
            public required HSlider Slider;
            public required Label ValueLabel;
            public required Label WarningLabel;
            public required int Recommended;
        }

        private static readonly Dictionary<RangeSlider, int> RecommendedRanges = new()
        {
            { RangeSlider.Dynamic, 1 },
            { RangeSlider.Static, 3 },
            { RangeSlider.Billboard, 5 },
            { RangeSlider.View, 5 }
        };

        private readonly World? _world;
        private bool _isExpanded = true;

        private RenderChunkManager? _renderChunkManager;
        private DynamicEntityRenderSystem? _dynamicSystem;
        private StaticEntityRenderSystem? _staticSystem;
        private BillboardEntityRenderSystem? _billboardSystem;
        private RenderVisibilitySystem? _visibilitySystem;
        private SimplifiedChunkSystem? _chunkSystem;

        private readonly Dictionary<RangeSlider, SliderEntry> _rangeSliders = new();
        private bool _suppressRangeCallbacks;
        private bool _suppressSettingCallbacks;

        private CheckBox? _chunkOutlineToggle;
        private bool _chunkOverlayDesiredState;
        private CheckBox? _frustumCullingCheck;
        private CheckBox? _xzOnlyFrustumCheck;
        private HSlider? _frustumPaddingSlider;
        private Label? _frustumPaddingValueLabel;
        private HSlider? _parallelThresholdSlider;
        private Label? _parallelThresholdValueLabel;

        public RenderingPanel(World? world)
        {
            _world = world;
        }

        public string Title => "Rendering";
        public string Id => "rendering_panel";

        public bool IsExpanded
        {
            get => _isExpanded;
            set => _isExpanded = value;
        }

        public Control? CreateHeaderButtons() => null;

        public Control CreateUI()
        {
            var container = CreateVBox(separation: 12);
            container.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            if (!TryResolveSystems(out var statusLabel))
            {
                container.AddChild(statusLabel);
                return container;
            }

            var renderSettings = _renderChunkManager!.SystemSettings;

            container.AddChild(CreateHeader("Render Ranges", 16));
            AddRangeSlider(container, "Dynamic Range", renderSettings.DynamicRange, RangeSlider.Dynamic);
            AddRangeSlider(container, "Static Range", renderSettings.StaticRange, RangeSlider.Static);
            AddRangeSlider(container, "Billboard Range", renderSettings.BillboardRange, RangeSlider.Billboard);
            AddRangeSlider(container, "View Distance", renderSettings.ViewDistance, RangeSlider.View);

            EnforceRangeOrdering();

            container.AddChild(new HSeparator());
            container.AddChild(CreateHeader("Visibility", 16));
            AddVisibilitySection(container);

            container.AddChild(new HSeparator());
            container.AddChild(CreateHeader("Debug", 16));

            AddDebugToggle(container, "Debug Dynamic Entities", _dynamicSystem?.SystemSettings.DebugDynamicEntities);
            AddDebugToggle(container, "Debug Static Entities", _staticSystem?.SystemSettings.DebugStaticEntities);
            AddDebugToggle(container, "Debug Billboards", _billboardSystem?.SystemSettings.DebugBillboardEntities);
            AddDebugToggle(container, "Debug Frustum Culling", _visibilitySystem?.SystemSettings.EnableDebugLogs);

            var outlinesSetting = renderSettings.ShowChunkOutlines;
            _chunkOverlayDesiredState = outlinesSetting.Value;
            _chunkOutlineToggle = new CheckBox
            {
                Text = "Show Chunk Outlines",
                ButtonPressed = outlinesSetting.Value
            };
            _chunkOutlineToggle.Toggled += pressed =>
            {
                _chunkOverlayDesiredState = pressed;
                outlinesSetting.Value = pressed;
                ApplyChunkOverlayVisibility(pressed);
            };
            outlinesSetting.ValueChanged += value =>
            {
                if (_chunkOutlineToggle != null && GodotObject.IsInstanceValid(_chunkOutlineToggle))
                {
                    _chunkOutlineToggle.ButtonPressed = (bool)value;
                }
            };
            container.AddChild(_chunkOutlineToggle);

            container.AddChild(new HSeparator());

            var applyButton = CreateButton("Apply Rendering Settings", OnApplyPressed, size: new Vector2(220, 36));
            container.AddChild(applyButton);

            return container;
        }

        private bool TryResolveSystems(out Control statusLabel)
        {
            statusLabel = CreateLabel("Render systems unavailable.", color: new Color(1, 0.5f, 0.5f));

            if (_world == null || _world.Systems == null)
                return false;

            var systems = _world.Systems;
            _renderChunkManager = systems.GetSystem(typeof(RenderChunkManager)) as RenderChunkManager;
            _dynamicSystem = systems.GetSystem(typeof(DynamicEntityRenderSystem)) as DynamicEntityRenderSystem;
            _staticSystem = systems.GetSystem(typeof(StaticEntityRenderSystem)) as StaticEntityRenderSystem;
            _billboardSystem = systems.GetSystem(typeof(BillboardEntityRenderSystem)) as BillboardEntityRenderSystem;
            _visibilitySystem = systems.GetSystem(typeof(RenderVisibilitySystem)) as RenderVisibilitySystem;
            _chunkSystem = systems.GetSystem(typeof(SimplifiedChunkSystem)) as SimplifiedChunkSystem;

            return _renderChunkManager != null;
        }

        private void AddRangeSlider(Container parent, string label, IntSetting setting, RangeSlider kind)
        {
            var sliderContainer = CreateVBox(separation: 4);
            sliderContainer.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

            var headerRow = CreateHBox(separation: 6);
            var nameLabel = CreateLabel(label, fontSize: 14);
            headerRow.AddChild(nameLabel);
            var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            headerRow.AddChild(spacer);
            var valueLabel = CreateLabel(setting.Value.ToString(), color: new Color(0.8f, 0.9f, 1.0f), fontSize: 14, hAlign: HorizontalAlignment.Right);
            headerRow.AddChild(valueLabel);
            sliderContainer.AddChild(headerRow);

            var slider = new HSlider
            {
                MinValue = 1,
                MaxValue = 10,
                Step = 1,
                Value = setting.Value,
                CustomMinimumSize = new Vector2(180, 0),
                TooltipText = setting.Tooltip,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
            };
            slider.ValueChanged += value => OnRangeSliderChanged(kind, (int)value);
            sliderContainer.AddChild(slider);

            var warningLabel = CreateLabel($"Recommended ≤ {RecommendedRanges[kind]}", color: new Color(1.0f, 0.6f, 0.2f), fontSize: 12);
            warningLabel.Visible = setting.Value > RecommendedRanges[kind];
            sliderContainer.AddChild(warningLabel);

            var entry = new SliderEntry
            {
                Setting = setting,
                Slider = slider,
                ValueLabel = valueLabel,
                WarningLabel = warningLabel,
                Recommended = RecommendedRanges[kind]
            };
            _rangeSliders[kind] = entry;

            setting.ValueChanged += value =>
            {
                if (_suppressSettingCallbacks)
                    return;
                var intValue = Convert.ToInt32(value);
                SetSliderValue(kind, intValue, updateSetting: false);
            };

            parent.AddChild(sliderContainer);
        }

        private void AddDebugToggle(Container parent, string label, BoolSetting? setting)
        {
            var checkbox = new CheckBox
            {
                Text = label,
                ButtonPressed = setting?.Value ?? false,
                Disabled = setting == null
            };

            if (setting != null)
            {
                checkbox.Toggled += pressed => setting.Value = pressed;
                setting.ValueChanged += value =>
                {
                    if (GodotObject.IsInstanceValid(checkbox))
                        checkbox.ButtonPressed = (bool)value;
                };
            }

            parent.AddChild(checkbox);
        }

        private void AddVisibilitySection(Container parent)
        {
            if (_visibilitySystem == null)
            {
                parent.AddChild(CreateLabel("Render visibility system unavailable.", color: new Color(1.0f, 0.5f, 0.5f), fontSize: 12));
                return;
            }

            var settings = _visibilitySystem.SystemSettings;

            _frustumCullingCheck = new CheckBox
            {
                Text = "Enable Frustum Culling",
                ButtonPressed = settings.EnableFrustumCulling.Value
            };
            _frustumCullingCheck.Toggled += pressed => settings.EnableFrustumCulling.Value = pressed;
            settings.EnableFrustumCulling.ValueChanged += value =>
            {
                if (_frustumCullingCheck != null && GodotObject.IsInstanceValid(_frustumCullingCheck))
                    _frustumCullingCheck.ButtonPressed = (bool)value;
            };
            parent.AddChild(_frustumCullingCheck);

            _xzOnlyFrustumCheck = new CheckBox
            {
                Text = "Use XZ-Only Frustum",
                ButtonPressed = settings.UseXZOnlyFrustum.Value
            };
            _xzOnlyFrustumCheck.Toggled += pressed => settings.UseXZOnlyFrustum.Value = pressed;
            settings.UseXZOnlyFrustum.ValueChanged += value =>
            {
                if (_xzOnlyFrustumCheck != null && GodotObject.IsInstanceValid(_xzOnlyFrustumCheck))
                    _xzOnlyFrustumCheck.ButtonPressed = (bool)value;
            };
            parent.AddChild(_xzOnlyFrustumCheck);

            (_frustumPaddingSlider, _frustumPaddingValueLabel) = AddFloatSettingSlider(
                parent,
                "Frustum Padding",
                settings.FrustumPadding,
                0.0f,
                128.0f,
                4.0f,
                value => $"{value:0.#} m");

            (_parallelThresholdSlider, _parallelThresholdValueLabel) = AddIntSettingSlider(
                parent,
                "Parallel Threshold",
                settings.ParallelThreshold,
                32,
                2048,
                32);
        }

        private (HSlider Slider, Label ValueLabel) AddFloatSettingSlider(
            Container parent,
            string label,
            FloatSetting setting,
            float min,
            float max,
            float step,
            Func<float, string> formatter)
        {
            var container = CreateVBox(separation: 4);
            container.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

            var header = CreateHBox(separation: 6);
            var nameLabel = CreateLabel(label, fontSize: 14);
            header.AddChild(nameLabel);
            header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var valueLabel = CreateLabel(formatter(setting.Value), color: new Color(0.8f, 0.9f, 1.0f), fontSize: 14, hAlign: HorizontalAlignment.Right);
            header.AddChild(valueLabel);
            container.AddChild(header);

            var slider = new HSlider
            {
                MinValue = min,
                MaxValue = max,
                Step = step,
                Value = setting.Value,
                CustomMinimumSize = new Vector2(180, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
            };
            slider.ValueChanged += value =>
            {
                var floatValue = (float)value;
                valueLabel.Text = formatter(floatValue);
                setting.Value = floatValue;
            };
            setting.ValueChanged += value =>
            {
                if (!GodotObject.IsInstanceValid(slider))
                    return;
                var floatValue = Convert.ToSingle(value);
                slider.Value = floatValue;
                valueLabel.Text = formatter(floatValue);
            };

            container.AddChild(slider);
            parent.AddChild(container);
            return (slider, valueLabel);
        }

        private (HSlider Slider, Label ValueLabel) AddIntSettingSlider(
            Container parent,
            string label,
            IntSetting setting,
            int min,
            int max,
            int step)
        {
            var container = CreateVBox(separation: 4);
            container.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

            var header = CreateHBox(separation: 6);
            var nameLabel = CreateLabel(label, fontSize: 14);
            header.AddChild(nameLabel);
            header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var valueLabel = CreateLabel(setting.Value.ToString(), color: new Color(0.8f, 0.9f, 1.0f), fontSize: 14, hAlign: HorizontalAlignment.Right);
            header.AddChild(valueLabel);
            container.AddChild(header);

            var slider = new HSlider
            {
                MinValue = min,
                MaxValue = max,
                Step = step,
                Value = setting.Value,
                CustomMinimumSize = new Vector2(180, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
            };
            slider.ValueChanged += value =>
            {
                int intValue = (int)value;
                valueLabel.Text = intValue.ToString();
                setting.Value = intValue;
            };
            setting.ValueChanged += value =>
            {
                if (!GodotObject.IsInstanceValid(slider))
                    return;
                int intValue = Convert.ToInt32(value);
                slider.Value = intValue;
                valueLabel.Text = intValue.ToString();
            };

            container.AddChild(slider);
            parent.AddChild(container);
            return (slider, valueLabel);
        }

        private void OnRangeSliderChanged(RangeSlider range, int newValue)
        {
            if (_suppressRangeCallbacks)
                return;

            _suppressRangeCallbacks = true;
            try
            {
                ApplyRangeSliderChange(range, newValue);
            }
            finally
            {
                _suppressRangeCallbacks = false;
            }
        }

        private void ApplyRangeSliderChange(RangeSlider range, int newValue)
        {
            newValue = Math.Clamp(newValue, 1, 10);

            switch (range)
            {
                case RangeSlider.Dynamic:
                    InternalSetSliderValue(RangeSlider.Dynamic, newValue, true);
                    if (GetSliderValue(RangeSlider.Static) < newValue)
                        InternalSetSliderValue(RangeSlider.Static, newValue, true);
                    if (GetSliderValue(RangeSlider.Billboard) < newValue)
                        InternalSetSliderValue(RangeSlider.Billboard, newValue, true);
                    if (GetSliderValue(RangeSlider.View) < newValue)
                        InternalSetSliderValue(RangeSlider.View, newValue, true);
                    break;

                case RangeSlider.Static:
                    if (newValue < GetSliderValue(RangeSlider.Dynamic))
                        newValue = GetSliderValue(RangeSlider.Dynamic);
                    InternalSetSliderValue(RangeSlider.Static, newValue, true);
                    if (GetSliderValue(RangeSlider.Billboard) < newValue)
                        InternalSetSliderValue(RangeSlider.Billboard, newValue, true);
                    if (GetSliderValue(RangeSlider.View) < newValue)
                        InternalSetSliderValue(RangeSlider.View, newValue, true);
                    break;

                case RangeSlider.Billboard:
                    if (newValue < GetSliderValue(RangeSlider.Static))
                        newValue = GetSliderValue(RangeSlider.Static);
                    InternalSetSliderValue(RangeSlider.Billboard, newValue, true);
                    if (GetSliderValue(RangeSlider.View) < newValue)
                        InternalSetSliderValue(RangeSlider.View, newValue, true);
                    break;

                case RangeSlider.View:
                    InternalSetSliderValue(RangeSlider.View, newValue, true);
                    if (GetSliderValue(RangeSlider.Billboard) > newValue)
                        InternalSetSliderValue(RangeSlider.Billboard, newValue, true);
                    if (GetSliderValue(RangeSlider.Static) > newValue)
                        InternalSetSliderValue(RangeSlider.Static, newValue, true);
                    if (GetSliderValue(RangeSlider.Dynamic) > newValue)
                        InternalSetSliderValue(RangeSlider.Dynamic, newValue, true);
                    break;
            }
        }

        private void SetSliderValue(RangeSlider range, int value, bool updateSetting = true)
        {
            var previous = _suppressRangeCallbacks;
            _suppressRangeCallbacks = true;
            try
            {
                InternalSetSliderValue(range, value, updateSetting);
            }
            finally
            {
                _suppressRangeCallbacks = previous;
            }
        }

        private void InternalSetSliderValue(RangeSlider range, int value, bool updateSetting)
        {
            if (!_rangeSliders.TryGetValue(range, out var entry))
                return;

            value = Math.Clamp(value, 1, 10);

            if ((int)entry.Slider.Value != value)
                entry.Slider.Value = value;

            entry.ValueLabel.Text = value.ToString();
            entry.WarningLabel.Visible = value > entry.Recommended;

            if (updateSetting && entry.Setting.Value != value)
            {
                var prev = _suppressSettingCallbacks;
                _suppressSettingCallbacks = true;
                entry.Setting.Value = value;
                _suppressSettingCallbacks = prev;
            }
        }

        private int GetSliderValue(RangeSlider range)
        {
            return _rangeSliders.TryGetValue(range, out var entry)
                ? (int)entry.Slider.Value
                : 0;
        }

        private void EnforceRangeOrdering()
        {
            if (_rangeSliders.Count == 0)
                return;

            _suppressRangeCallbacks = true;
            try
            {
                ApplyRangeSliderChange(RangeSlider.Dynamic, _rangeSliders[RangeSlider.Dynamic].Setting.Value);
                ApplyRangeSliderChange(RangeSlider.Static, _rangeSliders[RangeSlider.Static].Setting.Value);
                ApplyRangeSliderChange(RangeSlider.Billboard, _rangeSliders[RangeSlider.Billboard].Setting.Value);
                ApplyRangeSliderChange(RangeSlider.View, _rangeSliders[RangeSlider.View].Setting.Value);
            }
            finally
            {
                _suppressRangeCallbacks = false;
            }
        }

        private void ApplyChunkOverlayVisibility(bool visible)
        {
            _chunkOverlayDesiredState = visible;

            if (!IsInsideTree())
                return;

            var overlay = FindChunkOverlay();
            if (overlay == null)
            {
                if (!visible)
                    return;

                overlay = CreateChunkOverlay();
                if (overlay == null)
                    return;
            }

            // Ensure world reference is set (in case overlay was created by HybridBootstrapper)
            if (_world != null)
                overlay.SetWorld(_world);

            overlay.OverlayEnabled = visible;
            overlay.Visible = visible;
            if (visible && overlay.IsInsideTree())
                overlay.RefreshNow();
        }

        private ChunkDebugOverlay? FindChunkOverlay()
        {
            if (!IsInsideTree())
                return null;

            var tree = GetTree();
            var root = tree?.Root;
            return root?.FindChild("ChunkDebugOverlay", recursive: true, owned: false) as ChunkDebugOverlay;
        }

        private ChunkDebugOverlay? CreateChunkOverlay()
        {
            if (!IsInsideTree())
                return null;

            if (_world == null)
            {
                Logging.Log("[RenderingPanel] World not initialized, cannot create chunk overlay yet.", LogSeverity.Warning);
                return null;
            }

            var chunkManager = _renderChunkManager?.GetChunkManager() ?? _chunkSystem?.GetChunkManager();

            var overlay = new ChunkDebugOverlay
            {
                Name = "ChunkDebugOverlay",
                OverlayEnabled = true,
                ShowWireframes = true,
                ShowPlanes = true
            };
            overlay.SetWorld(_world);
            overlay.SetChunkManager(chunkManager);

            overlay.AutoRefresh = true;
            overlay.RefreshIntervalSeconds = 0.1f;  // Fast initial refresh

            var scene = GetTree()?.CurrentScene ?? GetTree()?.Root;
            scene?.AddChild(overlay);

            // Don't call RefreshNow() immediately - let AutoRefresh handle it after _Ready()
            return overlay;
        }

        private void OnApplyPressed()
        {
            _renderChunkManager?.SaveSettings();
            _dynamicSystem?.SaveSettings();
            _staticSystem?.SaveSettings();
            _billboardSystem?.SaveSettings();
            _visibilitySystem?.SaveSettings();

            Logging.Log("[RenderingPanel] Applied rendering settings");
        }

        public void Update(double delta)
        {
            // No per-frame logic required.
        }

        public void OnShow()
        {
            if (_renderChunkManager == null)
                return;

            foreach (var (range, entry) in _rangeSliders)
            {
                SetSliderValue(range, entry.Setting.Value, updateSetting: false);
            }
            EnforceRangeOrdering();
            ApplyChunkOverlayVisibility(_renderChunkManager.SystemSettings.ShowChunkOutlines.Value);
            RefreshVisibilityControls();
        }

        public void OnHide()
        {
            // Nothing to do.
        }

        private void RefreshVisibilityControls()
        {
            if (_visibilitySystem == null)
                return;

            var settings = _visibilitySystem.SystemSettings;

            if (_frustumCullingCheck != null && GodotObject.IsInstanceValid(_frustumCullingCheck))
                _frustumCullingCheck.ButtonPressed = settings.EnableFrustumCulling.Value;

            if (_xzOnlyFrustumCheck != null && GodotObject.IsInstanceValid(_xzOnlyFrustumCheck))
                _xzOnlyFrustumCheck.ButtonPressed = settings.UseXZOnlyFrustum.Value;

            if (_frustumPaddingSlider != null && GodotObject.IsInstanceValid(_frustumPaddingSlider))
            {
                _frustumPaddingSlider.Value = settings.FrustumPadding.Value;
                if (_frustumPaddingValueLabel != null)
                    _frustumPaddingValueLabel.Text = $"{settings.FrustumPadding.Value:0.#} m";
            }

            if (_parallelThresholdSlider != null && GodotObject.IsInstanceValid(_parallelThresholdSlider))
            {
                _parallelThresholdSlider.Value = settings.ParallelThreshold.Value;
                if (_parallelThresholdValueLabel != null)
                    _parallelThresholdValueLabel.Text = settings.ParallelThreshold.Value.ToString();
            }
        }
    }
}
