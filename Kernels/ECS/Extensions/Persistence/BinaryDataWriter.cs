using System.IO;
using System.Text;

namespace UltraSim.ECS.Persistence;

/// <summary>
/// Binary implementation of <see cref="IDataWriter"/> backed by a stream or file.
/// </summary>
public sealed class BinaryDataWriter : IDataWriter, IDisposable
{
    private readonly BinaryWriter _writer;
    private bool _disposed;

    public BinaryDataWriter(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen);
    }

    public BinaryDataWriter(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _writer = new BinaryWriter(
            File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None),
            Encoding.UTF8,
            leaveOpen: false);
    }

    public long Position => _writer.BaseStream.Position;

    public void WriteBool(bool value) => _writer.Write(value ? (byte)1 : (byte)0);
    public void WriteByte(byte value) => _writer.Write(value);
    public void WriteUInt16(ushort value) => _writer.Write(value);
    public void WriteInt32(int value) => _writer.Write(value);
    public void WriteUInt32(uint value) => _writer.Write(value);
    public void WriteInt64(long value) => _writer.Write(value);
    public void WriteUInt64(ulong value) => _writer.Write(value);
    public void WriteFloat(float value) => _writer.Write(value);

    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        _writer.Write(bytes.Length);
        _writer.Write(bytes);
    }

    public void WriteBytes(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _writer.Write(buffer, offset, count);
    }

    public void WriteBytes(ReadOnlySpan<byte> data) => _writer.Write(data);

    public void Flush() => _writer.Flush();

    public void Dispose()
    {
        if (_disposed)
            return;

        _writer.Dispose();
        _disposed = true;
    }
}
