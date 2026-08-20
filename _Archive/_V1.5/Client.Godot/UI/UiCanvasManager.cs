using Godot;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class UiCanvasManager : CanvasLayer
    {
        public override void _Ready()
        {
            Visible = true;
        }

        public virtual void SetCanvasVisible(bool visible)
        {
            Visible = true;
        }
    }
}
