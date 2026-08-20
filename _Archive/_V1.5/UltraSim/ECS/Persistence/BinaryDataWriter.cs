using System;
using System.IO;
using System.Text;

namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// File-backed binary implementation of <see cref="IDataWriter"/>.
    /// All primitives are written little-endian (via <see cref="BinaryWriter"/>).
    /// Strings are length-prefixed: [int32 byteCount][UTF-8 bytes].
    /// Owns and disposes the underlying <see cref="BinaryWriter"/> and its stream
    /// unless <paramref name="leaveOpen"/> is true.
    /// </summary>
    public sealed class BinaryDataWriter : IDataWriter, IDisposable
    {
        private readonly BinaryWriter _writer;
        private bool _disposed;

        /// <summary>Wraps an existing stream. Call <see cref="Flush"/> before disposing if needed.</summary>
        public BinaryDataWriter(Stream stream, bool leaveOpen = false)
        {
            ArgumentNullException.ThrowIfNull(stream);
            _writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen);
        }

        /// <summary>Opens (or creates) <paramref name="filePath"/> for exclusive write access.</summary>
        public BinaryDataWriter(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            _writer = new BinaryWriter(
                File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None),
                Encoding.UTF8,
                leaveOpen: false);
        }

        public long Position => _writer.BaseStream.Position;

        public void WriteBool(bool value)    => _writer.Write(value ? (byte)1 : (byte)0);
        public void WriteByte(byte value)    => _writer.Write(value);
        public void WriteUInt16(ushort value)=> _writer.Write(value);
        public void WriteInt32(int value)    => _writer.Write(value);
        public void WriteUInt32(uint value)  => _writer.Write(value);
        public void WriteInt64(long value)   => _writer.Write(value);
        public void WriteUInt64(ulong value) => _writer.Write(value);
        public void WriteFloat(float value)  => _writer.Write(value);

        public void WriteString(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            _writer.Write(bytes.Length); // int32 length prefix (matches ReadString)
            _writer.Write(bytes);
        }

        public void WriteBytes(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            _writer.Write(buffer, offset, count);
        }

        // Span overload — BinaryWriter.Write(ReadOnlySpan<byte>) is available in .NET 8+.
        public void WriteBytes(ReadOnlySpan<byte> data)
            => _writer.Write(data);

        public void Flush() => _writer.Flush();

        public void Dispose()
        {
            if (_disposed) return;
            _writer.Dispose();
            _disposed = true;
        }
    }
}
