using Godot;
using UltraSim.UI.Authoring;
using UltraSim.UI.Contracts;

namespace Server.Godot.UI
{
    /// <summary>
    /// Simple test panel sent to every client on connect.
    /// Demonstrates the full schema panel flow: open → render → button press → server response.
    /// </summary>
    public sealed class WelcomePanel : UiPanel
    {
        public new const ushort TypeId = 0xFF01;
        public const uint OkButton = 0;

        public WelcomePanel(long sessionId) : base(typeId: TypeId, revision: 1)
        {
            Size(260, 130);
            Header().Text("Connected", style: "text.title.centered");
            Separator();
            Row().Text("Session: ").Dynamic(sessionId.ToString());
            Separator();
            Footer().Button(id: OkButton, label: "OK");
        }

        public override void OnResponse(IUiPanelContext ctx, uint actionId)
        {
            GD.Print($"[WelcomePanel] session={ctx.InstanceId.Value} action={actionId}");
            ctx.Close();
        }
    }
}
