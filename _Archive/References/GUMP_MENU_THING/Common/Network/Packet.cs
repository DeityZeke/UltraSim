
using System;

using Common;

namespace Common.Network
{
    public abstract class Packet
    {
        protected byte m_PacketID;

        public byte PacketID => m_PacketID;

        public Packet(byte packetID)
        {
            m_PacketID = packetID;
        }

        public abstract void Serialize(ref BinaryWriter writer);

        /*
        // Deserialize
        public Packet(byte packetID, ref PacketReader reader) // Creating a new packet via stream read, give it the ID of the new packet
        {
            //m_PacketID = reader.ReadByte(); //This gets read and consumed during packet assembly no need to 'get' it here
            m_PacketID = packetID;
        }
        */
    }
}