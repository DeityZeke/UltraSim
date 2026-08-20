// Shared/Network/Packets/ChatPackets.cs
// Chat and messaging related packets

namespace Network.Shared.Network.Packets
{
    // ═══════════════════════════════════════════════════════════════════════
    //                     CLIENT → SERVER
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Client → Server: Send a chat message.
    /// </summary>
    [EstimatedSize(256)]
    public readonly struct ChatSend : IPacket<ChatSend>, IClientPacket
    {
        public static ushort PacketId => 0x0301;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public const int MaxMessageLength = 200;

        public readonly ChatType Type;
        public readonly string Message;
        public readonly string? Target;  // For whispers/private messages

        public ChatSend(ChatType type, string message, string? target = null)
        {
            Type = type;
            Message = message.Length > MaxMessageLength
                ? message.Substring(0, MaxMessageLength)
                : message;
            Target = target;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteByte((byte)Type);
            writer.WriteString(Message);
            writer.WriteString(Target ?? string.Empty);
        }

        public static ChatSend Deserialize(ref BitReader reader)
        {
            var type = (ChatType)reader.ReadByte();
            var message = reader.ReadString();
            var target = reader.ReadString();

            return new ChatSend(
                type,
                message,
                string.IsNullOrEmpty(target) ? null : target
            );
        }
    }

    /// <summary>
    /// Client → Server: Request to join a chat channel.
    /// </summary>
    [EstimatedSize(32)]
    public readonly struct ChannelJoinRequest : IPacket<ChannelJoinRequest>, IClientPacket
    {
        public static ushort PacketId => 0x0302;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly string ChannelName;
        public readonly string? Password;

        public ChannelJoinRequest(string channelName, string? password = null)
        {
            ChannelName = channelName;
            Password = password;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteString(ChannelName);
            writer.WriteString(Password ?? string.Empty);
        }

        public static ChannelJoinRequest Deserialize(ref BitReader reader)
        {
            var name = reader.ReadString();
            var password = reader.ReadString();

            return new ChannelJoinRequest(
                name,
                string.IsNullOrEmpty(password) ? null : password
            );
        }
    }

    /// <summary>
    /// Client → Server: Leave a chat channel.
    /// </summary>
    [EstimatedSize(32)]
    public readonly struct ChannelLeaveRequest : IPacket<ChannelLeaveRequest>, IClientPacket
    {
        public static ushort PacketId => 0x0303;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly string ChannelName;

        public ChannelLeaveRequest(string channelName)
        {
            ChannelName = channelName;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteString(ChannelName);
        }

        public static ChannelLeaveRequest Deserialize(ref BitReader reader)
        {
            return new ChannelLeaveRequest(reader.ReadString());
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //                     SERVER → CLIENT
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Server → Client: Chat message to display.
    /// </summary>
    [EstimatedSize(384)]
    public readonly struct ChatReceive : IPacket<ChatReceive>, IServerPacket
    {
        public static ushort PacketId => 0x0310;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly ChatType Type;
        public readonly uint SenderSerial;  // 0 for system messages
        public readonly string SenderName;
        public readonly string Message;
        public readonly ushort Hue;         // Text color
        public readonly ushort Font;        // Font style

        public ChatReceive(ChatType type, uint senderSerial, string senderName,
            string message, ushort hue = 0, ushort font = 0)
        {
            Type = type;
            SenderSerial = senderSerial;
            SenderName = senderName;
            Message = message;
            Hue = hue;
            Font = font;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteByte((byte)Type);
            writer.WriteUInt32(SenderSerial);
            writer.WriteString(SenderName);
            writer.WriteString(Message);
            writer.WriteUInt16(Hue);
            writer.WriteUInt16(Font);
        }

        public static ChatReceive Deserialize(ref BitReader reader)
        {
            return new ChatReceive(
                (ChatType)reader.ReadByte(),
                reader.ReadUInt32(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadUInt16(),
                reader.ReadUInt16()
            );
        }
    }

    /// <summary>
    /// Server → Client: System message (announcements, errors, etc).
    /// </summary>
    [EstimatedSize(256)]
    public readonly struct SystemMessage : IPacket<SystemMessage>, IServerPacket
    {
        public static ushort PacketId => 0x0311;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly SystemMessageType Type;
        public readonly string Message;
        public readonly ushort Hue;

        public SystemMessage(SystemMessageType type, string message, ushort hue = 0x3B2)
        {
            Type = type;
            Message = message;
            Hue = hue;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteByte((byte)Type);
            writer.WriteString(Message);
            writer.WriteUInt16(Hue);
        }

        public static SystemMessage Deserialize(ref BitReader reader)
        {
            return new SystemMessage(
                (SystemMessageType)reader.ReadByte(),
                reader.ReadString(),
                reader.ReadUInt16()
            );
        }
    }

    /// <summary>
    /// Server → Client: Channel joined successfully.
    /// </summary>
    [EstimatedSize(64)]
    public readonly struct ChannelJoined : IPacket<ChannelJoined>, IServerPacket
    {
        public static ushort PacketId => 0x0312;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly byte ChannelId;     // Server-assigned channel ID for this session
        public readonly string ChannelName;
        public readonly ushort MemberCount;

        public ChannelJoined(byte channelId, string channelName, ushort memberCount)
        {
            ChannelId = channelId;
            ChannelName = channelName;
            MemberCount = memberCount;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteByte(ChannelId);
            writer.WriteString(ChannelName);
            writer.WriteUInt16(MemberCount);
        }

        public static ChannelJoined Deserialize(ref BitReader reader)
        {
            return new ChannelJoined(
                reader.ReadByte(),
                reader.ReadString(),
                reader.ReadUInt16()
            );
        }
    }

    /// <summary>
    /// Server → Client: Channel left or kicked.
    /// </summary>
    [EstimatedSize(32)]
    public readonly struct ChannelLeft : IPacket<ChannelLeft>, IServerPacket
    {
        public static ushort PacketId => 0x0313;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly byte ChannelId;
        public readonly ChannelLeaveReason Reason;

        public ChannelLeft(byte channelId, ChannelLeaveReason reason)
        {
            ChannelId = channelId;
            Reason = reason;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteByte(ChannelId);
            writer.WriteByte((byte)Reason);
        }

        public static ChannelLeft Deserialize(ref BitReader reader)
        {
            return new ChannelLeft(
                reader.ReadByte(),
                (ChannelLeaveReason)reader.ReadByte()
            );
        }
    }

    /// <summary>
    /// Server → Client: Overhead text above an entity.
    /// </summary>
    [EstimatedSize(256)]
    public readonly struct OverheadText : IPacket<OverheadText>, IServerPacket
    {
        public static ushort PacketId => 0x0314;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly uint Serial;
        public readonly string Text;
        public readonly ushort Hue;
        public readonly OverheadTextType Type;

        public OverheadText(uint serial, string text, ushort hue, OverheadTextType type)
        {
            Serial = serial;
            Text = text;
            Hue = hue;
            Type = type;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(Serial);
            writer.WriteString(Text);
            writer.WriteUInt16(Hue);
            writer.WriteByte((byte)Type);
        }

        public static OverheadText Deserialize(ref BitReader reader)
        {
            return new OverheadText(
                reader.ReadUInt32(),
                reader.ReadString(),
                reader.ReadUInt16(),
                (OverheadTextType)reader.ReadByte()
            );
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //                           ENUMS
    // ═══════════════════════════════════════════════════════════════════════

    public enum ChatType : byte
    {
        /// <summary>Local area chat, visible to nearby players.</summary>
        Say = 0,

        /// <summary>Louder, larger range than Say.</summary>
        Yell = 1,

        /// <summary>Private message to a specific player.</summary>
        Whisper = 2,

        /// <summary>Emote action (*waves*, etc).</summary>
        Emote = 3,

        /// <summary>Guild chat.</summary>
        Guild = 4,

        /// <summary>Alliance chat.</summary>
        Alliance = 5,

        /// <summary>Party/group chat.</summary>
        Party = 6,

        /// <summary>Custom channel chat.</summary>
        Channel = 7,

        /// <summary>System announcement.</summary>
        System = 8,

        /// <summary>Global/world chat.</summary>
        Global = 9,

        /// <summary>Trade channel.</summary>
        Trade = 10,

        /// <summary>Help/newbie channel.</summary>
        Help = 11
    }

    public enum SystemMessageType : byte
    {
        Info = 0,
        Warning = 1,
        Error = 2,
        Success = 3,
        Announcement = 4,
        ServerMessage = 5,
        Debug = 6
    }

    public enum ChannelLeaveReason : byte
    {
        Voluntary = 0,
        Kicked = 1,
        Banned = 2,
        ChannelClosed = 3,
        Disconnected = 4
    }

    public enum OverheadTextType : byte
    {
        Speech = 0,
        Emote = 1,
        System = 2,
        Label = 3,
        Whisper = 4,
        Yell = 5
    }
}
