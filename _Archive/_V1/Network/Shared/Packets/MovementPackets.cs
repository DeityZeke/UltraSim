// Shared/Network/Packets/MovementPackets.cs
// Movement and position synchronization packets

namespace Network.Shared.Network.Packets
{
    // ═══════════════════════════════════════════════════════════════════════
    //                     CLIENT → SERVER
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Client → Server: Movement input request.
    /// Uses bit-packing for compact transmission.
    /// </summary>
    [EstimatedSize(16)]
    [FallbackToTcp]
    public readonly struct MovementRequest : IPacket<MovementRequest>, IClientPacket
    {
        public static ushort PacketId => 0x0101;
        public static ChannelType Channel => ChannelType.UnreliableSequenced;

        /// <summary>
        /// Direction (0-7 for 8 cardinal/diagonal directions).
        /// </summary>
        public readonly byte Direction;

        /// <summary>
        /// Movement type flags.
        /// </summary>
        public readonly MoveFlags Flags;

        /// <summary>
        /// Client-side input sequence for reconciliation.
        /// </summary>
        public readonly uint InputSequence;

        /// <summary>
        /// Client's predicted X position.
        /// </summary>
        public readonly ushort PredictedX;

        /// <summary>
        /// Client's predicted Y position.
        /// </summary>
        public readonly ushort PredictedY;

        /// <summary>
        /// Client's predicted Z position.
        /// </summary>
        public readonly sbyte PredictedZ;

        public MovementRequest(byte direction, MoveFlags flags, uint inputSequence,
            ushort predictedX, ushort predictedY, sbyte predictedZ)
        {
            Direction = direction;
            Flags = flags;
            InputSequence = inputSequence;
            PredictedX = predictedX;
            PredictedY = predictedY;
            PredictedZ = predictedZ;
        }

        public void Serialize(ref BitWriter writer)
        {
            // Pack direction (3 bits) and flags (5 bits) into one byte
            writer.WriteBits(Direction, 3);
            writer.WriteBits((uint)Flags, 5);

            writer.AlignToByte();
            writer.WriteUInt32(InputSequence);
            writer.WriteUInt16(PredictedX);
            writer.WriteUInt16(PredictedY);
            writer.WriteSByte(PredictedZ);
        }

        public static MovementRequest Deserialize(ref BitReader reader)
        {
            byte direction = (byte)reader.ReadBits(3);
            var flags = (MoveFlags)reader.ReadBits(5);

            reader.AlignToByte();
            return new MovementRequest(
                direction,
                flags,
                reader.ReadUInt32(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadSByte()
            );
        }
    }

    [Flags]
    public enum MoveFlags : byte
    {
        None = 0,
        Running = 1 << 0,
        Sneaking = 1 << 1,
        Mounted = 1 << 2,
        Swimming = 1 << 3,
        Flying = 1 << 4
    }

    /// <summary>
    /// Client → Server: Stop movement.
    /// </summary>
    [EstimatedSize(8)]
    [FallbackToTcp]
    public readonly struct MovementStop : IPacket<MovementStop>, IClientPacket
    {
        public static ushort PacketId => 0x0102;
        public static ChannelType Channel => ChannelType.UnreliableSequenced;

        public readonly uint InputSequence;
        public readonly ushort X;
        public readonly ushort Y;
        public readonly sbyte Z;

        public MovementStop(uint inputSequence, ushort x, ushort y, sbyte z)
        {
            InputSequence = inputSequence;
            X = x;
            Y = y;
            Z = z;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(InputSequence);
            writer.WriteUInt16(X);
            writer.WriteUInt16(Y);
            writer.WriteSByte(Z);
        }

        public static MovementStop Deserialize(ref BitReader reader)
        {
            return new MovementStop(
                reader.ReadUInt32(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadSByte()
            );
        }
    }

    /// <summary>
    /// Client → Server: Request to turn/face direction without moving.
    /// </summary>
    [EstimatedSize(4)]
    public readonly struct FacingRequest : IPacket<FacingRequest>, IClientPacket
    {
        public static ushort PacketId => 0x0103;
        public static ChannelType Channel => ChannelType.UnreliableSequenced;

        public readonly byte Direction;

        public FacingRequest(byte direction)
        {
            Direction = direction;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteByte(Direction);
        }

        public static FacingRequest Deserialize(ref BitReader reader)
        {
            return new FacingRequest(reader.ReadByte());
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //                     SERVER → CLIENT
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Server → Client: Movement accepted/corrected.
    /// </summary>
    [EstimatedSize(16)]
    public readonly struct MovementConfirm : IPacket<MovementConfirm>, IServerPacket
    {
        public static ushort PacketId => 0x0110;
        public static ChannelType Channel => ChannelType.UnreliableSequenced;

        /// <summary>
        /// Last processed input sequence.
        /// </summary>
        public readonly uint LastInputSequence;

        /// <summary>
        /// Authoritative position.
        /// </summary>
        public readonly ushort X;
        public readonly ushort Y;
        public readonly sbyte Z;

        /// <summary>
        /// Whether this is a correction (client was wrong).
        /// </summary>
        public readonly bool IsCorrection;

        public MovementConfirm(uint lastInputSequence, ushort x, ushort y, sbyte z, bool isCorrection)
        {
            LastInputSequence = lastInputSequence;
            X = x;
            Y = y;
            Z = z;
            IsCorrection = isCorrection;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(LastInputSequence);
            writer.WriteUInt16(X);
            writer.WriteUInt16(Y);
            writer.WriteSByte(Z);
            writer.WriteBit(IsCorrection);
            writer.AlignToByte();
        }

        public static MovementConfirm Deserialize(ref BitReader reader)
        {
            var seq = reader.ReadUInt32();
            var x = reader.ReadUInt16();
            var y = reader.ReadUInt16();
            var z = reader.ReadSByte();
            var correction = reader.ReadBit();
            reader.AlignToByte();

            return new MovementConfirm(seq, x, y, z, correction);
        }
    }

    /// <summary>
    /// Server → Client: Another entity's position update (not self).
    /// </summary>
    [EstimatedSize(16)]
    public readonly struct EntityPosition : IPacket<EntityPosition>, IServerPacket
    {
        public static ushort PacketId => 0x0111;
        public static ChannelType Channel => ChannelType.UnreliableSequenced;

        public readonly uint Serial;
        public readonly ushort X;
        public readonly ushort Y;
        public readonly sbyte Z;
        public readonly byte Direction;
        public readonly MoveFlags Flags;

        public EntityPosition(uint serial, ushort x, ushort y, sbyte z, byte direction, MoveFlags flags)
        {
            Serial = serial;
            X = x;
            Y = y;
            Z = z;
            Direction = direction;
            Flags = flags;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(Serial);
            writer.WriteUInt16(X);
            writer.WriteUInt16(Y);
            writer.WriteSByte(Z);
            writer.WriteBits(Direction, 3);
            writer.WriteBits((uint)Flags, 5);
            writer.AlignToByte();
        }

        public static EntityPosition Deserialize(ref BitReader reader)
        {
            var serial = reader.ReadUInt32();
            var x = reader.ReadUInt16();
            var y = reader.ReadUInt16();
            var z = reader.ReadSByte();
            var dir = (byte)reader.ReadBits(3);
            var flags = (MoveFlags)reader.ReadBits(5);
            reader.AlignToByte();

            return new EntityPosition(serial, x, y, z, dir, flags);
        }
    }

    /// <summary>
    /// Server → Client: Batch position update for multiple entities.
    /// More efficient than individual packets for crowded areas.
    /// </summary>
    [EstimatedSize(256)]
    public readonly struct EntityPositionBatch : IPacket<EntityPositionBatch>, IServerPacket
    {
        public static ushort PacketId => 0x0112;
        public static ChannelType Channel => ChannelType.UnreliableSequenced;

        // Maximum entities per batch
        public const int MaxEntities = 32;

        public readonly byte Count;
        public readonly EntityPositionData[] Entities;

        public EntityPositionBatch(EntityPositionData[] entities)
        {
            Count = (byte)Math.Min(entities.Length, MaxEntities);
            Entities = entities;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteByte(Count);

            for (int i = 0; i < Count; i++)
            {
                ref readonly var e = ref Entities[i];
                writer.WriteUInt32(e.Serial);
                writer.WriteUInt16(e.X);
                writer.WriteUInt16(e.Y);
                writer.WriteSByte(e.Z);
                writer.WriteByte((byte)(e.Direction | ((byte)e.Flags << 3)));
            }
        }

        public static EntityPositionBatch Deserialize(ref BitReader reader)
        {
            byte count = reader.ReadByte();
            var entities = new EntityPositionData[count];

            for (int i = 0; i < count; i++)
            {
                var serial = reader.ReadUInt32();
                var x = reader.ReadUInt16();
                var y = reader.ReadUInt16();
                var z = reader.ReadSByte();
                var packed = reader.ReadByte();

                entities[i] = new EntityPositionData(
                    serial, x, y, z,
                    (byte)(packed & 0x07),
                    (MoveFlags)(packed >> 3)
                );
            }

            return new EntityPositionBatch(entities);
        }
    }

    /// <summary>
    /// Position data for batch updates.
    /// </summary>
    public readonly struct EntityPositionData
    {
        public readonly uint Serial;
        public readonly ushort X;
        public readonly ushort Y;
        public readonly sbyte Z;
        public readonly byte Direction;
        public readonly MoveFlags Flags;

        public EntityPositionData(uint serial, ushort x, ushort y, sbyte z, byte direction, MoveFlags flags)
        {
            Serial = serial;
            X = x;
            Y = y;
            Z = z;
            Direction = direction;
            Flags = flags;
        }
    }

    /// <summary>
    /// Server → Client: Teleport entity to new location.
    /// </summary>
    [EstimatedSize(16)]
    public readonly struct EntityTeleport : IPacket<EntityTeleport>, IServerPacket
    {
        public static ushort PacketId => 0x0113;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly uint Serial;
        public readonly ushort X;
        public readonly ushort Y;
        public readonly sbyte Z;
        public readonly byte MapId;
        public readonly byte Direction;

        public EntityTeleport(uint serial, ushort x, ushort y, sbyte z, byte mapId, byte direction)
        {
            Serial = serial;
            X = x;
            Y = y;
            Z = z;
            MapId = mapId;
            Direction = direction;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(Serial);
            writer.WriteUInt16(X);
            writer.WriteUInt16(Y);
            writer.WriteSByte(Z);
            writer.WriteByte(MapId);
            writer.WriteByte(Direction);
        }

        public static EntityTeleport Deserialize(ref BitReader reader)
        {
            return new EntityTeleport(
                reader.ReadUInt32(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadSByte(),
                reader.ReadByte(),
                reader.ReadByte()
            );
        }
    }

    /// <summary>
    /// Server → Client: Movement rejected (blocked, etc).
    /// </summary>
    [EstimatedSize(12)]
    public readonly struct MovementRejected : IPacket<MovementRejected>, IServerPacket
    {
        public static ushort PacketId => 0x0114;
        public static ChannelType Channel => ChannelType.ReliableUnordered;

        public readonly uint InputSequence;
        public readonly MoveRejectReason Reason;

        /// <summary>
        /// Authoritative position to reset to.
        /// </summary>
        public readonly ushort X;
        public readonly ushort Y;
        public readonly sbyte Z;

        public MovementRejected(uint inputSequence, MoveRejectReason reason, ushort x, ushort y, sbyte z)
        {
            InputSequence = inputSequence;
            Reason = reason;
            X = x;
            Y = y;
            Z = z;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(InputSequence);
            writer.WriteByte((byte)Reason);
            writer.WriteUInt16(X);
            writer.WriteUInt16(Y);
            writer.WriteSByte(Z);
        }

        public static MovementRejected Deserialize(ref BitReader reader)
        {
            return new MovementRejected(
                reader.ReadUInt32(),
                (MoveRejectReason)reader.ReadByte(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadSByte()
            );
        }
    }

    public enum MoveRejectReason : byte
    {
        Unknown = 0,
        Blocked = 1,
        TooFar = 2,
        Dead = 3,
        Frozen = 4,
        Overweight = 5,
        NoStamina = 6,
        Mounted = 7
    }
}
