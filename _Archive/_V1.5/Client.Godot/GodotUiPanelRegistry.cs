using System;
using System.Collections.Generic;
using UltraSim.UI.Contracts;
using Godot;

namespace Client.Godot
{
    public delegate Control GodotUiPanelFactory(in UiPanelOpenCommand command);

    public sealed class GodotUiPanelRegistry
    {
        private readonly Dictionary<uint, GodotUiPanelFactory> _factories = new();

        public void Register(UiPanelTypeId panelTypeId, GodotUiPanelFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factories[panelTypeId.Value] = factory;
        }

        public bool TryCreate(in UiPanelOpenCommand command, out Control? panel)
        {
            if (_factories.TryGetValue(command.PanelTypeId.Value, out GodotUiPanelFactory? factory))
            {
                panel = factory(in command);
                return panel != null;
            }

            panel = null;
            return false;
        }
    }
}
