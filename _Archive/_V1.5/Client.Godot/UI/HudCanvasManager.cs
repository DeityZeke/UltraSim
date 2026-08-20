using Godot;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class HudCanvasManager : UiCanvasManager
    {
        [Export] public PackedScene? HudScene { get; set; }

        private Control? _hudInstance;

        public void ShowHud()
        {
            EnsureHudInstance();
        }

        public void HideHud()
        {
            ClearHudInstance();
        }

        private void EnsureHudInstance()
        {
            if (_hudInstance != null || HudScene == null)
                return;

            _hudInstance = HudScene.Instantiate<Control>();
            AddChild(_hudInstance);
        }

        private void ClearHudInstance()
        {
            if (_hudInstance == null)
                return;

            _hudInstance.QueueFree();
            _hudInstance = null;
        }
    }
}
