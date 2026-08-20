
#nullable enable

using System;
using System.Net;
using System.Threading.Tasks;


using Godot;

namespace Server.Network
{
    public partial class NetworkServerNode : Node
    {
        private MessagePump? m_MessagePump;

        public override void _Ready()
        {
            AssemblyManager.Invoke("Configure");
            AssemblyManager.Invoke("Configure_Server");
            AssemblyManager.Invoke("Configure_Client"); // FOR TESTING SPLIT OFF TO CLIENT

            AssemblyManager.Invoke("Initialize");


            // Example: Listen on all interfaces, port 4000
            var endpoint = new IPEndPoint(IPAddress.Any, 4000);
            var listener = new Listener(endpoint);

            m_MessagePump = new MessagePump(listener);

            // Start the async server task
            Task.Run(() => m_MessagePump!.RunAsync());
        }

        public override void _ExitTree()
        {
            // Clean up on shutdown
            m_MessagePump?.Dispose();
            GD.Print("Network server stopped.");
        }
    }
}