
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Godot;

using Common.Network;

namespace Server.Network
{
    public class MessagePump : IDisposable
    {
        private readonly Listener m_Listener;
        private readonly List<Session> m_Sessions = new();
        private readonly ConcurrentQueue<Session> m_WorkingQueue = new();
        private readonly ConcurrentQueue<Session> m_ThrottledQueue = new();
        private readonly ConcurrentQueue<Session> m_DisposedQueue = new();
        private readonly CancellationTokenSource m_Cts = new();

        public MessagePump(Listener listener)
        {
            m_Listener = listener;
        }

        public async Task RunAsync()
        {
            while (!m_Cts.IsCancellationRequested)
            {
                // Accept new clients
                var newClients = await m_Listener.SliceAsync();
                foreach (var client in newClients)
                {
                    GD.Print($"[NETWORK] New connection from {client.RemoteEndPoint}");
                    var session = new Session(client);
                    session.OnPacketReceive += (s, p) => m_WorkingQueue.Enqueue(s);
                    m_Sessions.Add(session);
                    session.Start();
                }

                // Process sessions in working queue (batch)
                int processed = 0, maxPerTick = 500;
                while (processed < maxPerTick && m_WorkingQueue.TryDequeue(out var session))
                {
                    if (session.IsDisposed)
                    {
                        m_DisposedQueue.Enqueue(session);
                        continue;
                    }

                    if (session.IsThrottled)
                    {
                        m_ThrottledQueue.Enqueue(session);
                        continue;
                    }


                    // Handle packet processing 
                    while (session.TryDequeueInbound(out var packet) && packet != null)
                    {
                        //session.PacketsThisTick++;
                        
                        await PacketManager.HandlePacket(packet, session);
                    }

                    processed++;
                }

                // Re-enqueue throttled sessions
                while (m_ThrottledQueue.TryDequeue(out var throttledSession))
                    m_WorkingQueue.Enqueue(throttledSession);

                // Cleanup disposed
                while (m_DisposedQueue.TryDequeue(out var disposedSession))
                    m_Sessions.Remove(disposedSession);

                await Task.Delay(1, m_Cts.Token); // Main server tick
            }
        }

        public void Dispose() => m_Cts.Cancel();
    }
}