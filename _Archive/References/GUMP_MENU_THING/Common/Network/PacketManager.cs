
#nullable enable

using System;
using System.Threading.Tasks;

using Common;

using Godot;

using Common.Network.Packets;

namespace Common.Network
{
    public delegate Packet PacketDeserializer(byte id, ref BinaryReader reader);

    public static class PacketManager
    {
        public static void Configure()
        {
            // Register Core Packets Here?
            PacketManager.RegisterPacket<LoginRequest>(0x01, LoginRequest.CreateLoginRequest);
            PacketManager.RegisterPacket<LoginRequestResponce>(0x02, LoginRequestResponce.CreateLoginRequestResponce);
        }

        private static readonly Type[] m_PacketTypes = new Type[byte.MaxValue];
        private static readonly PacketDeserializer[] m_PacketRegistry = new PacketDeserializer[byte.MaxValue];

        private static readonly Func<Packet, Session, Task>[] m_PacketHandlers = new Func<Packet, Session, Task>[byte.MaxValue];

        public static void RegisterPacket<T>(byte packetID, PacketDeserializer deserializer) where T : Packet
        {
            //GD.PrintStack();
            //GD.Print(System.Environment.StackTrace.ToString());

            if (m_PacketRegistry[packetID] != null)
                GD.PushError($"Packet ID 0x{packetID:X2} already registered to {m_PacketTypes[packetID]?.Name}");

            GD.Print($"[PACKETS] Register Packet: 0x{packetID:X2} :: {typeof(T)}");

            m_PacketTypes[packetID] = typeof(T);
            m_PacketRegistry[packetID] = deserializer;
        }

        public static void AddHandler<T>(Func<Packet, Session, Task> handler) where T : Packet
        {
            //GD.Print(System.Environment.StackTrace.ToString());

            GD.Print($"[HANDLERS] Adding Handler For: {typeof(T)}" );

            m_PacketHandlers[GetPacketID<T>()] = async (packet, session) =>
            {
                await handler((T)packet, session);
            };
        }

        public static byte GetPacketID<T>() where T : Packet
        {
            return (byte)Array.IndexOf(m_PacketTypes, typeof(T));
        }

        public static Packet? Parse(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < 1)
            {
                GD.PrintErr("[PARSE] Ignoring empty buffer");
                return null;
            }

            try
            {
                var reader = new BinaryReader(buffer);

                byte packetID = reader.ReadByte();

                GD.Print($"[NETWORK] Manager (PARSE) ID: 0x{packetID:X2}");

                if (packetID >= m_PacketRegistry.Length || m_PacketRegistry[packetID] == null)
                {
                    GD.PrintErr($"[PARSE] Unknown or unregistered packet ID: 0x{packetID:X2}");
                    return null;
                }

                return m_PacketRegistry[packetID](packetID, ref reader);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[PacketManager.Parse] Exception: {ex}");
                return null;
            }
        }

        public static async Task HandlePacket(Packet packet, Session session)
        {
            if (packet == null)
            {
                GD.PrintErr($"[HANDLER] Tried to handle a null packet.");
                return;
            }

            GD.Print($"[NETWORK] Manager (HANDLE) ID: 0x{packet.PacketID:X2}");

            var handler = m_PacketHandlers[packet.PacketID];

            if (handler != null)
            {
                try
                {
                    await handler(packet, session);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[HANDLER] Exception in packet handler: {ex}");
                }
            }
            else
                GD.PrintErr($"[HANDLER] Unknown or unregistered packet ID: 0x{packet.PacketID:X2}");
        }
    }
}