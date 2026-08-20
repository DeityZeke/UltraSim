using System;

namespace UltraSim.ECS
{
    /// <summary>
    /// Host-injected sequential data writer for world save output.
    /// Implementations may write Godot .res, JSON, binary, etc.
    /// The default binary implementation uses BitWriter internally.
    /// </summary>
    public interface IDataWriter
    {
        void WriteBool(bool value);
        void WriteByte(byte value);
        void WriteUInt16(ushort value);
        void WriteInt32(int value);
        void WriteUInt32(uint value);
        void WriteInt64(long value);
        void WriteUInt64(ulong value);
        void WriteFloat(float value);
        void WriteString(string value);
        void WriteBytes(byte[] buffer, int offset, int count);
        void WriteBytes(ReadOnlySpan<byte> data);
    }
}
