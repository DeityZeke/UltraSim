
using System;

using Common;

using Godot;

namespace Common.Network.Packets
{
    public partial class LoginRequest : Packet
    {
        public readonly string Username;
        public readonly string Password;

        public LoginRequest(byte packetID, string username, string password) : base(packetID)
        {
            Username = username;
            Password = password;
        }

        public override void Serialize(ref BinaryWriter writer)
        {
            writer.WriteString(Username);
            writer.WriteString(Password);
        }

        // Used to Register the Packets Deserialize // MUST MATCH THE DESRIALIZE CONSTRUCTOR!
        public static LoginRequest CreateLoginRequest(byte packetID, ref BinaryReader reader)
        {
            return new LoginRequest(packetID, ref reader);
        }

        // Deserialize
        public LoginRequest(byte packetID, ref BinaryReader reader) : base(packetID)
        {
            Username = reader.ReadString();
            Password = reader.ReadString();
        }
    }
}