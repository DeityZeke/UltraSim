using UltraSim.UI.Authoring;
using UltraSim.UI.Contracts;

namespace Server.Godot.UI
{
    /// <summary>
    /// Login panel sent to clients after a successful IP check.
    /// Collects username (field 0) and password (field 1) via text input nodes.
    ///
    /// Security note: the client does NOT send credentials via UiPanelResponse.
    /// The Login button is a packet-bound action — the client intercepts it and
    /// sends a <c>LoginRequestPacket (0xE8)</c> with a SHA-256 challenge-response
    /// instead. See <see cref="LoginPacketCodec"/> for the full scheme.
    ///
    /// The server opens this panel with a 16-byte random challenge nonce in the
    /// open payload (see <see cref="LoginPacketCodec.EncodeChallengePayload"/>).
    /// The Cancel button is handled via the normal UiPanelResponse path.
    /// </summary>
    public sealed class LoginPanel : UiPanel
    {
        public new const ushort TypeId = 0xFF02;
        public static readonly uint LoginButton  = ActionId("login");
        public static readonly uint CancelButton = ActionId("cancel");

        public LoginPanel() : base(typeId: TypeId, revision: 1)
        {
            Size(340, 280);
            Header().Text("Login", style: "text.title.centered");
            Separator();
            Row().Text("Username:");
            TextInput(placeholder: "Enter username...", maxLength: 32);
            Row().Text("Password:");
            TextInput(placeholder: "Enter password...", isPassword: true, maxLength: 64);
            Separator();
            Footer()
                .Button(LoginButton,  label: "Login")
                .Button(CancelButton, label: "Cancel");
        }
    }
}
