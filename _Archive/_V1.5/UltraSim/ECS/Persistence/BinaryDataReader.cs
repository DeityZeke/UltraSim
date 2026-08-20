using System;
using System.IO;
using System.Text;

namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// File-backed binary implementation of <see cref="IDataReader"/>.
    /// Mirrors <see cref="BinaryDataWriter"/> — reads must occur in the same order as writes.
    /// All primitives are read little-endian (via <see cref="BinaryReader"/>).
    /// Strings are decoded as [int32 byteCount][UTF-8 bytes].
    /// </summary>
    public sealed class BinaryDataReader : IDataReader, IDisposable
    {
        private readonly BinaryReader _reader;
        private bool _disposed;

        /// <summary>Wraps an existing stream for sequential read.</summary>
        public BinaryDataReader(Stream stream, bool leaveOpen = false)
        {
            ArgumentNullException.ThrowIfNull(stream);
            _reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen);
        }

        /// <summary>Opens <paramref name="filePath"/> for shared read access.</summary>
        public BinaryDataReader(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            _reader = new BinaryReader(
                File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read),
                Encoding.UTF8,
                leaveOpen: false);
        }

        public long Position => _reader.BaseStream.Position;

        public bool  ReadBool()    => _reader.ReadByte() != 0;
        public byte  ReadByte()    => _reader.ReadByte();
        public ushort ReadUInt16() => _reader.ReadUInt16();
        public int   ReadInt32()   => _reader.ReadInt32();
        public uint  ReadUInt32()  => _reader.ReadUInt32();
        public long  ReadInt64()   => _reader.ReadInt64();
        public ulong ReadUInt64()  => _reader.ReadUInt64();
        public float ReadFloat()   => _reader.ReadSingle();

        public string ReadString()
        {
            int byteCount = _reader.ReadInt32();
            if (byteCount < 0)
                throw new InvalidDataException($"Invalid string length prefix: {byteCount}.");
            if (byteCount == 0)
                return string.Empty;

            byte[] bytes = _reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount)
                throw new EndOfStreamException($"Expected {byteCount} string bytes; stream yielded {bytes.Length}.");

            return Encoding.UTF8.GetString(bytes);
        }

        public int ReadBytes(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return _reader.Read(buffer, offset, count);
        }

        // Span overload — BinaryReader.Read(Span<byte>) is available in .NET 8+.
        public int ReadBytes(Span<byte> destination)
            => _reader.Read(destination);

        public void Dispose()
        {
            if (_disposed) return;
            _reader.Dispose();
            _disposed = true;
        }
    }
}
