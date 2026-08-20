using Godot;
using System.Collections.Generic;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class MenuScreenHost : Control
    {
        [Signal] public delegate void ScreenChangedEventHandler(StringName screenId);

        private readonly Dictionary<StringName, MenuScreenBase> _screens = new();

        public StringName CurrentScreenId { get; private set; } = new();

        public override void _Ready()
        {
            RebuildIndex();
            HideAllScreens();
        }

        public T? GetScreen<T>(StringName screenId) where T : MenuScreenBase
        {
            return _screens.TryGetValue(screenId, out MenuScreenBase? screen) ? screen as T : null;
        }

        public void ShowScreen(StringName screenId)
        {
            foreach ((StringName id, MenuScreenBase screen) in _screens)
            {
                if (id == screenId)
                    screen.ShowScreen();
                else
                    screen.HideScreen();
            }

            CurrentScreenId = screenId;
            EmitSignal(SignalName.ScreenChanged, CurrentScreenId);
        }

        public void HideAllScreens()
        {
            foreach (MenuScreenBase screen in _screens.Values)
                screen.HideScreen();

            CurrentScreenId = new StringName();
            EmitSignal(SignalName.ScreenChanged, CurrentScreenId);
        }

        private void RebuildIndex()
        {
            _screens.Clear();

            foreach (Node child in GetChildren())
            {
                if (child is not MenuScreenBase screen)
                    continue;

                if (screen.ScreenId.IsEmpty)
                {
                    GD.PushWarning($"MenuScreenHost child '{screen.Name}' is missing ScreenId.");
                    continue;
                }

                if (_screens.ContainsKey(screen.ScreenId))
                {
                    GD.PushWarning($"MenuScreenHost duplicate ScreenId '{screen.ScreenId}' on '{screen.Name}'.");
                    continue;
                }

                _screens.Add(screen.ScreenId, screen);
            }
        }
    }
}
