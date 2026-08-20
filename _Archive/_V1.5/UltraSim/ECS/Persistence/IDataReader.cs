using System;

namespace UltraSim.ECS
{
    /// <summary>
    /// Host-injected sequential data reader for world load input.
    /// Mirrors IDataWriter — implementations must read in the same order they were written.
    /// </summary>
    public interface IDataReader
    {
        bool ReadBool();
        byte ReadByte();
        ushort ReadUInt16();
        int ReadInt32();
        uint ReadUInt32();
        long ReadInt64();
        ulong ReadUInt64();
        float ReadFloat();
        string ReadString();
        int ReadBytes(byte[] buffer, int offset, int count);
        int ReadBytes(Span<byte> destination);
    }
}
