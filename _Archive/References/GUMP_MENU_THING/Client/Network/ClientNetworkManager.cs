#nullable enable

using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Common.Network;

using Godot;

namespace Client.Network
{
    public static class ClientNetworkManager
    {
        public static int MaxReconnectAttempts => m_MaxReconnectAttempts;

        public static Session? CurrentSession { get; private set; }
        public static bool IsConnected => CurrentSession != null && !CurrentSession.IsDisposed;

        public static event Action? OnConnected;
        public static event Action? OnDisconnected;
        public static event Action? OnThrottled;

        public static event Action<int>? OnReconnectAttempt;
        public static event Action? OnReconnectFailed;

        private static CancellationTokenSource? m_ReconnectCts;

        private static string? m_LastIP;
        private static int m_LastPort;
        private static int m_ReconnectDelay = 3000; // ms
        private static int m_MaxReconnectAttempts = 5;

        public static async Task<bool> Connect(string ip, int port)
        {
            m_LastIP = ip;
            m_LastPort = port;
            return await TryConnect(ip, port);
        }

        private static async Task<bool> TryConnect(string ip, int port)
        {
            try
            {
                var client = new TcpClient();
                await client.ConnectAsync(ip, port);

                var session = new Session(client.Client);
                session.OnPacketReceive += HandlePacket;
                session.OnThrottled += () => OnThrottled?.Invoke();
                session.OnDisconnected += HandleDisconnect;
                session.Start();

                CurrentSession = session;
                OnConnected?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                GD.PrintErr("[CLIENT] Connection failed: " + ex.Message);
                return false;
            }
        }

        private static async void HandleDisconnect()
        {
            GD.Print("[CLIENT] Disconnected. Attempting reconnect...");
            OnDisconnected?.Invoke();

            m_ReconnectCts?.Cancel();
            m_ReconnectCts = new CancellationTokenSource();
            var token = m_ReconnectCts.Token;

            int attempts = 0;
            while (attempts < m_MaxReconnectAttempts && !token.IsCancellationRequested)
            {
                attempts++;
                OnReconnectAttempt?.Invoke(attempts);
                try
                {
                    await Task.Delay(m_ReconnectDelay, token);
                    if (await TryConnect(m_LastIP!, m_LastPort))
                    {
                        GD.Print("[CLIENT] Reconnected.");
                        OnConnected?.Invoke();
                        return;
                    }
                    GD.Print($"[CLIENT] Reconnect attempt {attempts} failed.");
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }

            GD.PrintErr("[CLIENT] Reconnect failed. Giving up.");
            OnReconnectFailed?.Invoke();
        }

        private static async void HandlePacket(Session session, Packet packet)
        {
            await PacketManager.HandlePacket(packet, session);
        }

        public static void Disconnect()
        {
            m_ReconnectCts?.Cancel();
            CurrentSession?.Dispose();
            CurrentSession = null;
            OnDisconnected?.Invoke();
        }
    }
}