
using System;

using Common;

using Godot;

namespace Common.Network.Packets
{
    public enum LoginResponseCode : byte
    {
        Success = 0,
        InvalidAccount = 1,
        BadPassword = 2,
        Banned = 3,
        UnknownError = 255
    }

    public partial class LoginRequestResponce : Packet
    {
        public readonly LoginResponseCode Response;

        public LoginRequestResponce(byte packetID, LoginResponseCode response)
            : base(packetID)
        {
            Response = response;
        }

        public override void Serialize(ref BinaryWriter writer)
        {
            writer.WriteByte((byte)Response); // Success is 0
        }

        public static LoginRequestResponce CreateLoginRequestResponce(byte packetID, ref BinaryReader reader)
        {
            return new LoginRequestResponce(packetID, ref reader);
        }

        public LoginRequestResponce(byte packetID, ref BinaryReader reader)
            : base(packetID)
        {
            Response = (LoginResponseCode)reader.ReadByte();
        }
    }
}