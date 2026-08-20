
#nullable enable

using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;


using Common.Network;
using Common.Network.Packets;


using UltraSim.Client.Network;

using Godot;


namespace Client
{

    public partial class LoginScene : Control
    {

        private LineEdit m_UsernameLineEdit;
        private LineEdit m_PasswordLineEdit;
        private Button m_ConnectButton;
        private RichTextLabel m_StatusLabel;

        public override void _Ready()
        {
            m_UsernameLineEdit = GetNode<LineEdit>("VBoxContainer/Username");
            m_PasswordLineEdit = GetNode<LineEdit>("VBoxContainer/Password");
            m_ConnectButton = GetNode<Button>("VBoxContainer/Button_Connect");
            m_StatusLabel = GetNode<RichTextLabel>("VBoxContainer/Connection_Status");

            m_ConnectButton.Pressed += OnConnectPressed;

            ClientNetworkManager.OnConnected += () => UpdateStatus("Connected! ");
            ClientNetworkManager.OnDisconnected += () => UpdateStatus("Disconnected. Reconnecting...");
            ClientNetworkManager.OnReconnectAttempt += attempt => UpdateStatus($"Reconnect attempt {attempt}/{ClientNetworkManager.MaxReconnectAttempts}");
            ClientNetworkManager.OnReconnectFailed += () => UpdateStatus("Reconnect failed. Please try again.");

            EventBus.OnLoginSuccess += LogInSuccess;
            EventBus.OnLoginFailure += LogInFailure;
        }

        private void LogInSuccess()
        {
            GameManager.Instance.CurrentState = GameState.CharacterSelect;
        }

        private void LogInFailure(string reason)
        {

        }

        private async void OnConnectPressed()
        {
            string username = m_UsernameLineEdit.Text;
            string password = m_PasswordLineEdit.Text;

            //m_StatusLabel.Text = "Connecting...";

            try
            {

                await ClientNetworkManager.Connect("127.0.0.1", 4000);

                byte packetID = PacketManager.GetPacketID<LoginRequest>();
                GD.Print($"[CLIENT] Send :: PacketID " + packetID);
                var loginRequest = new LoginRequest(packetID, username, password);

                if (ClientNetworkManager.CurrentSession != null)
                    //ClientNetworkManager.CurrentSession.Send(loginRequest);
                    ClientNetworkManager.CurrentSession.QueueSend(loginRequest);

            }
            catch (Exception ex)
            {
                m_StatusLabel.Text = $"Connection error: {ex.Message}";
            }
        }

        private void UpdateStatus(string text)
        {
            // Schedule the actual UI change on the main thread
            CallDeferred(nameof(_DeferredUpdateStatus), text);
            GD.Print($"[UI] {text}");
        }

        private void _DeferredUpdateStatus(string text)
        {
            m_StatusLabel.Text = text;
        }

        private void GoToGame()
        {
            // Replace "GameScene.tscn" with your actual game scene
            GetTree().ChangeSceneToFile("res://GameScene.tscn");
        }
    }
}