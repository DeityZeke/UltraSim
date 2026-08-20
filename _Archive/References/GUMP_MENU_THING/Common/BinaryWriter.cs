
using System;
using System.Text;

using Godot;

namespace Common
{
    /// <summary>
    /// Fast, allocation-free binary writer for primitives and Godot Control nodes.
    /// </summary>
    public ref struct BinaryWriter
    {
        private Span<byte> m_Buffer;
        private int m_Offset;

        public BinaryWriter(Span<byte> buffer)
        {
            m_Buffer = buffer;
            m_Offset = 0;
        }

        public int BytesWritten => m_Offset;
        public int Length => m_Buffer.Length;

#region Primitives

        public void Skip(int bytes) => m_Offset += bytes;

        public void WriteByte(byte value)
        {
            m_Buffer[m_Offset++] = value;
        }

        public void WriteBool(bool value)
        {
            WriteByte((byte)(value ? 1 : 0));
        }

        public void WriteShort(short value)
        {
            BitConverter.TryWriteBytes(m_Buffer.Slice(m_Offset, 2), value);
            m_Offset += 2;
        }

        public void WriteUShort(ushort value)
        {
            BitConverter.TryWriteBytes(m_Buffer.Slice(m_Offset, 2), value);
            m_Offset += 2;
        }

        public void WriteInt(int value)
        {
            BitConverter.TryWriteBytes(m_Buffer.Slice(m_Offset, 4), value);
            m_Offset += 4;
        }

        public void WriteUInt(uint value)
        {
            BitConverter.TryWriteBytes(m_Buffer.Slice(m_Offset, 4), value);
            m_Offset += 4;
        }

        public void WriteLong(long value)
        {
            BitConverter.TryWriteBytes(m_Buffer.Slice(m_Offset, 8), value);
            m_Offset += 8;
        }

        public void WriteULong(ulong value)
        {
            BitConverter.TryWriteBytes(m_Buffer.Slice(m_Offset, 8), value);
            m_Offset += 8;
        }

        public void WriteFloat(float value)
        {
            BitConverter.TryWriteBytes(m_Buffer.Slice(m_Offset, 4), value);
            m_Offset += 4;
        }

        public void WriteDouble(double value)
        {
            BitConverter.TryWriteBytes(m_Buffer.Slice(m_Offset, 8), value);
            m_Offset += 8;
        }

        /// <summary>
        /// Writes a UTF8 string with a ushort length prefix.
        /// </summary>
        public void WriteString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteUShort(0);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > ushort.MaxValue)
                throw new ArgumentException("String too long for packet");

            WriteUShort((ushort)bytes.Length);
            bytes.CopyTo(m_Buffer.Slice(m_Offset, bytes.Length));
            m_Offset += bytes.Length;
        }

        public void WriteBytes(ReadOnlySpan<byte> data)
        {
            if (data.Length > ushort.MaxValue)
                throw new ArgumentException("Data too long for packet");

            WriteUShort((ushort)data.Length);
            data.CopyTo(m_Buffer.Slice(m_Offset, data.Length));
            m_Offset += data.Length;
        }

        public void WriteVector2(Vector2 v)
        {
            WriteFloat(v.X);
            WriteFloat(v.Y);
        }

        public void WriteColor(Color c)
        {
            WriteFloat(c.R);
            WriteFloat(c.G);
            WriteFloat(c.B);
            WriteFloat(c.A);
        }

#endregion

#region Godot Controls & Menu

        public void Write(Control control)
        {
            switch (control)
            {
                case Label label: Write(label); break;
                case Button button: Write(button); break;
                case RichTextLabel richText: Write(richText); break;
                default:
                    WriteByte((byte)MenuNodeID.Unknown);
                    break;
            }
        }

        public void Write(Label label)
        {
            WriteByte((byte)MenuNodeID.Label);
            WriteVector2(label.Position);
            WriteColor(label.SelfModulate);
            WriteString(label.Text);
        }

        public void Write(Button button)
        {
            WriteByte((byte)MenuNodeID.Button);
            WriteVector2(button.Position);
            WriteColor(button.SelfModulate);
            WriteString(button.Text);
        }

        public void Write(RichTextLabel richText)
        {
            WriteByte((byte)MenuNodeID.RichTextLabel);
            WriteVector2(richText.Position);
            WriteColor(richText.SelfModulate);
            WriteString(richText.Text);
        }

        // Extend here for more controls
#endregion
    }
}