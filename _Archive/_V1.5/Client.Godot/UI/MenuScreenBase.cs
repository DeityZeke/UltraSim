using Godot;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class MenuScreenBase : Control
    {
        [Export] public StringName ScreenId { get; set; } = new();
        [Export] public NodePath? DefaultFocusPath { get; set; }
        [Export] public MenuPresentationMode PresentationMode { get; set; } = MenuPresentationMode.ExclusiveAll;

        public virtual void ShowScreen()
        {
            Visible = true;
            ProcessMode = ProcessModeEnum.Inherit;
            MouseFilter = MouseFilterEnum.Stop;

            Control? defaultFocus = GetNodeOrNull<Control>(DefaultFocusPath);
            if (defaultFocus != null)
                defaultFocus.CallDeferred(Control.MethodName.GrabFocus);
        }

        public virtual void HideScreen()
        {
            Visible = false;
            ProcessMode = ProcessModeEnum.Disabled;
        }
    }
}
