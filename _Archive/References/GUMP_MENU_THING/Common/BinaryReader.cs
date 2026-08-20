
using System;
using System.Text;

using Godot;

namespace Common
{
    /// <summary>
    /// Fast, allocation-free binary reader for primitives and Godot Control nodes.
    /// </summary>
    public ref struct BinaryReader
    {
        private ReadOnlySpan<byte> m_Buffer;
        private int m_Offset;

        public BinaryReader(ReadOnlySpan<byte> buffer)
        {
            m_Buffer = buffer;
            m_Offset = 0;
        }

        public int BytesRead => m_Offset;
        public int Length => m_Buffer.Length;
        public bool IsEOF => m_Offset >= m_Buffer.Length;

#region Primitives

        private void Ensure(int count)
        {
            if (m_Offset + count > m_Buffer.Length)
                throw new IndexOutOfRangeException($"BinaryReader tried to read {count} bytes at offset {m_Offset}, but only {m_Buffer.Length - m_Offset} bytes remain.");
        }

        public byte ReadByte()
        {
            Ensure(1);
            return m_Buffer[m_Offset++];
        }

        public bool ReadBool() => ReadByte() != 0;

        public short ReadShort()
        {
            Ensure(2);
            short value = BitConverter.ToInt16(m_Buffer.Slice(m_Offset, 2));
            m_Offset += 2;
            return value;
        }

        public ushort ReadUShort()
        {
            Ensure(2);
            ushort value = BitConverter.ToUInt16(m_Buffer.Slice(m_Offset, 2));
            m_Offset += 2;
            return value;
        }

        public int ReadInt()
        {
            Ensure(4);
            int value = BitConverter.ToInt32(m_Buffer.Slice(m_Offset, 4));
            m_Offset += 4;
            return value;
        }

        public uint ReadUInt()
        {
            Ensure(4);
            uint value = BitConverter.ToUInt32(m_Buffer.Slice(m_Offset, 4));
            m_Offset += 4;
            return value;
        }

        public long ReadLong()
        {
            Ensure(8);
            long value = BitConverter.ToInt64(m_Buffer.Slice(m_Offset, 8));
            m_Offset += 8;
            return value;
        }

        public ulong ReadULong()
        {
            Ensure(8);
            ulong value = BitConverter.ToUInt64(m_Buffer.Slice(m_Offset, 8));
            m_Offset += 8;
            return value;
        }

        public float ReadFloat()
        {
            Ensure(4);
            float value = BitConverter.ToSingle(m_Buffer.Slice(m_Offset, 4));
            m_Offset += 4;
            return value;
        }

        public double ReadDouble()
        {
            Ensure(8);
            double value = BitConverter.ToDouble(m_Buffer.Slice(m_Offset, 8));
            m_Offset += 8;
            return value;
        }

        public string ReadString()
        {
            ushort len = ReadUShort();
            if (len == 0)
                return string.Empty;
            Ensure(len);
            string value = Encoding.UTF8.GetString(m_Buffer.Slice(m_Offset, len));
            m_Offset += len;
            return value;
        }

        public ReadOnlySpan<byte> ReadBytes()
        {
            ushort len = ReadUShort();
            Ensure(len);
            var span = m_Buffer.Slice(m_Offset, len);
            m_Offset += len;
            return span;
        }

        public Vector2 ReadVector2()
        {
            float x = ReadFloat();
            float y = ReadFloat();
            return new Vector2(x, y);
        }

        public Color ReadColor()
        {
            float r = ReadFloat();
            float g = ReadFloat();
            float b = ReadFloat();
            float a = ReadFloat();
            return new Color(r, g, b, a);
        }
#endregion

#region Godot Controls & Menu

        public Control ReadControl()
        {
            var type = (MenuNodeID)ReadByte();
            switch (type)
            {
                case MenuNodeID.Label:
                    return ReadLabel();
                case MenuNodeID.Button:
                    return ReadButton();
                case MenuNodeID.RichTextLabel:
                    return ReadRichTextLabel();
                default:
                    throw new NotSupportedException($"Unknown or unsupported MenuNodeID: {type}");
            }
        }

        public Label ReadLabel()
        {
            var label = new Label();
            label.Position = ReadVector2();
            label.SelfModulate = ReadColor();
            label.Text = ReadString();
            return label;
        }

        public Button ReadButton()
        {
            var button = new Button();
            button.Position = ReadVector2();
            button.SelfModulate = ReadColor();
            button.Text = ReadString();
            return button;
        }

        public RichTextLabel ReadRichTextLabel()
        {
            var richText = new RichTextLabel();
            richText.Position = ReadVector2();
            richText.SelfModulate = ReadColor();
            richText.Text = ReadString();
            return richText;
        }

        // Extend here for more controls

#endregion
    }
}