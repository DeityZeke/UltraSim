
using System;
using System.Threading.Tasks;

using Godot;

using Common.Network;
using Common.Network.Packets;

namespace Server.Network.PacketHandlers
{
    public partial class LoginPacketHandler
    {
        public static void Configure_Server()
        {

            PacketManager.AddHandler<LoginRequest>(LoginHandler);

            /*
            PacketManager.AddHandler<LoginRequest>(async (packet, session) =>
            {
                await LoginHandler(packet, session);
            });
            */
        }

        public static async Task LoginHandler(Packet packet, Session session)
        {
            LoginRequest loginRequest = (LoginRequest)packet;

            GD.Print($"Login received for: {loginRequest.Username}");

            // --- AUTHENTICATION BLOCK ---
            // Simulated authentication logic
            bool userExists = true;         // Replace with DB/user store check
            bool passwordCorrect = true;    // Verify against stored hash
            bool isBanned = false;          // Check for active ban status

            LoginResponseCode responseCode;

            if (!userExists)
                responseCode = LoginResponseCode.InvalidAccount;
            else if (!passwordCorrect)
                responseCode = LoginResponseCode.BadPassword;
            else if (isBanned)
                responseCode = LoginResponseCode.Banned;
            else
                responseCode = LoginResponseCode.Success;

            // --- Send Response ---
            byte responseID = PacketManager.GetPacketID<LoginRequestResponce>();
            var response = new LoginRequestResponce(responseID, responseCode);

            //session.Send(response);
            session.QueueSend(response);

            //return Task.CompletedTask;
        }
    }
}