
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Godot;

namespace Server.Network
{
    public class Listener : IDisposable
    {
        private readonly IPEndPoint m_Endpoint;
        private readonly Socket m_ListenerSocket;
        private bool m_IsListening = true;

        public Listener(IPEndPoint endpoint)
        {
            m_Endpoint = endpoint;
            m_ListenerSocket = new Socket(m_Endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            m_ListenerSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
            m_ListenerSocket.Bind(m_Endpoint);
            m_ListenerSocket.Listen(1024);

            GD.Print($"[NETWORK] Listening on {m_Endpoint.Address}:{m_Endpoint.Port}");
        }

        // Accept all pending clients (async, batch)
        public async Task<List<Socket>> SliceAsync(CancellationToken? externalToken = null)
        {
            var accepted = new List<Socket>();
            if (!m_IsListening) return accepted;
            CancellationToken token = externalToken ?? CancellationToken.None;

            while (m_ListenerSocket.Poll(0, SelectMode.SelectRead))
            {
                try
                {
                    var client = await m_ListenerSocket.AcceptAsync(token);
                    accepted.Add(client);
                }
                catch
                {
                    break;
                }
            }
            return accepted;
        }

        public void Dispose()
        {
            m_IsListening = false;
            try { m_ListenerSocket.Close(); } catch { }
        }
    }
}