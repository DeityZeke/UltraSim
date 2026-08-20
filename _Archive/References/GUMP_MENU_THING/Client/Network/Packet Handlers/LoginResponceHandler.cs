
using System;
using System.Threading.Tasks;

using Godot;

using Common.Network;
using Common.Network.Packets;


namespace Client.Network.PacketHandlers
{
    public partial class LoginResponcePacketHandler
    {

        public static void Configure_Client()
        {

            PacketManager.AddHandler<LoginRequestResponce>(LoginResponceHandler);
        }

        public static Task LoginResponceHandler(Packet packet, Session session)
        {

            var pckt = (LoginRequestResponce)packet;

            GD.Print($"Login Request Responce Recieved: {pckt.Response.ToString()}");

            if (pckt.Response == LoginResponseCode.Success)
            {
                EventBus.RaiseLoginSuccess();
            }
            else
            {
                EventBus.RaiseLoginFailure(pckt.Response.ToString());
            }

            return Task.CompletedTask;
        }
    }
}