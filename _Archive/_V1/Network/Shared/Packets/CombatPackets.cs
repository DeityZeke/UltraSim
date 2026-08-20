// Shared/Network/Packets/CombatPackets.cs
// Combat and damage related packets

namespace Network.Shared.Network.Packets
{
    // ═══════════════════════════════════════════════════════════════════════
    //                     CLIENT → SERVER
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Client → Server: Request to attack a target.
    /// </summary>
    [EstimatedSize(8)]
    public readonly struct AttackRequest : IPacket<AttackRequest>, IClientPacket
    {
        public static ushort PacketId => 0x0201;
        public static ChannelType Channel => ChannelType.ReliableUnordered;

        public readonly uint TargetSerial;

        public AttackRequest(uint targetSerial)
        {
            TargetSerial = targetSerial;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(TargetSerial);
        }

        public static AttackRequest Deserialize(ref BitReader reader)
        {
            return new AttackRequest(reader.ReadUInt32());
        }
    }

    /// <summary>
    /// Client → Server: Stop attacking.
    /// </summary>
    [EstimatedSize(4)]
    public readonly struct AttackStop : IPacket<AttackStop>, IClientPacket
    {
        public static ushort PacketId => 0x0202;
        public static ChannelType Channel => ChannelType.ReliableUnordered;

        public void Serialize(ref BitWriter writer)
        {
            // Empty packet
        }

        public static AttackStop Deserialize(ref BitReader reader)
        {
            return new AttackStop();
        }
    }

    /// <summary>
    /// Client → Server: Use an ability/skill.
    /// </summary>
    [EstimatedSize(16)]
    public readonly struct UseAbility : IPacket<UseAbility>, IClientPacket
    {
        public static ushort PacketId => 0x0203;
        public static ChannelType Channel => ChannelType.ReliableUnordered;

        public readonly ushort AbilityId;
        public readonly uint TargetSerial;  // 0 = self/no target
        public readonly ushort TargetX;     // For ground-targeted abilities
        public readonly ushort TargetY;
        public readonly sbyte TargetZ;

        public UseAbility(ushort abilityId, uint targetSerial = 0,
            ushort targetX = 0, ushort targetY = 0, sbyte targetZ = 0)
        {
            AbilityId = abilityId;
            TargetSerial = targetSerial;
            TargetX = targetX;
            TargetY = targetY;
            TargetZ = targetZ;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt16(AbilityId);
            writer.WriteUInt32(TargetSerial);
            writer.WriteUInt16(TargetX);
            writer.WriteUInt16(TargetY);
            writer.WriteSByte(TargetZ);
        }

        public static UseAbility Deserialize(ref BitReader reader)
        {
            return new UseAbility(
                reader.ReadUInt16(),
                reader.ReadUInt32(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadSByte()
            );
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //                     SERVER → CLIENT
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Server → Client: Combat swing started.
    /// </summary>
    [EstimatedSize(12)]
    public readonly struct SwingStart : IPacket<SwingStart>, IServerPacket
    {
        public static ushort PacketId => 0x0210;
        public static ChannelType Channel => ChannelType.ReliableUnordered;

        public readonly uint AttackerSerial;
        public readonly uint DefenderSerial;

        public SwingStart(uint attackerSerial, uint defenderSerial)
        {
            AttackerSerial = attackerSerial;
            DefenderSerial = defenderSerial;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(AttackerSerial);
            writer.WriteUInt32(DefenderSerial);
        }

        public static SwingStart Deserialize(ref BitReader reader)
        {
            return new SwingStart(
                reader.ReadUInt32(),
                reader.ReadUInt32()
            );
        }
    }

    /// <summary>
    /// Server → Client: Damage dealt to an entity.
    /// </summary>
    [EstimatedSize(16)]
    public readonly struct DamageDealt : IPacket<DamageDealt>, IServerPacket
    {
        public static ushort PacketId => 0x0211;
        public static ChannelType Channel => ChannelType.ReliableUnordered;

        public readonly uint TargetSerial;
        public readonly uint SourceSerial;
        public readonly ushort Amount;
        public readonly DamageType Type;
        public readonly DamageFlags Flags;

        public DamageDealt(uint targetSerial, uint sourceSerial, ushort amount,
            DamageType type, DamageFlags flags)
        {
            TargetSerial = targetSerial;
            SourceSerial = sourceSerial;
            Amount = amount;
            Type = type;
            Flags = flags;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(TargetSerial);
            writer.WriteUInt32(SourceSerial);
            writer.WriteUInt16(Amount);
            writer.WriteByte((byte)Type);
            writer.WriteByte((byte)Flags);
        }

        public static DamageDealt Deserialize(ref BitReader reader)
        {
            return new DamageDealt(
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt16(),
                (DamageType)reader.ReadByte(),
                (DamageFlags)reader.ReadByte()
            );
        }
    }

    public enum DamageType : byte
    {
        Physical = 0,
        Fire = 1,
        Cold = 2,
        Poison = 3,
        Energy = 4,
        Chaos = 5,
        Direct = 6,  // Bypasses armor
        Healing = 7  // Negative damage
    }

    [Flags]
    public enum DamageFlags : byte
    {
        None = 0,
        Critical = 1 << 0,
        Blocked = 1 << 1,
        Parried = 1 << 2,
        Dodged = 1 << 3,
        Absorbed = 1 << 4,
        OverTime = 1 << 5,
        Reflected = 1 << 6
    }

    /// <summary>
    /// Server → Client: Visual damage number (cosmetic, can be missed).
    /// </summary>
    [EstimatedSize(12)]
    public readonly struct DamageNumber : IPacket<DamageNumber>, IServerPacket
    {
        public static ushort PacketId => 0x0212;
        public static ChannelType Channel => ChannelType.Unreliable;

        public readonly uint TargetSerial;
        public readonly ushort Amount;
        public readonly DamageType Type;
        public readonly DamageFlags Flags;

        public DamageNumber(uint targetSerial, ushort amount, DamageType type, DamageFlags flags)
        {
            TargetSerial = targetSerial;
            Amount = amount;
            Type = type;
            Flags = flags;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(TargetSerial);
            writer.WriteUInt16(Amount);
            writer.WriteByte((byte)Type);
            writer.WriteByte((byte)Flags);
        }

        public static DamageNumber Deserialize(ref BitReader reader)
        {
            return new DamageNumber(
                reader.ReadUInt32(),
                reader.ReadUInt16(),
                (DamageType)reader.ReadByte(),
                (DamageFlags)reader.ReadByte()
            );
        }
    }

    /// <summary>
    /// Server → Client: Entity health update.
    /// </summary>
    [EstimatedSize(12)]
    public readonly struct HealthUpdate : IPacket<HealthUpdate>, IServerPacket
    {
        public static ushort PacketId => 0x0213;
        public static ChannelType Channel => ChannelType.ReliableUnordered;

        public readonly uint Serial;
        public readonly ushort CurrentHealth;
        public readonly ushort MaxHealth;

        public HealthUpdate(uint serial, ushort currentHealth, ushort maxHealth)
        {
            Serial = serial;
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(Serial);
            writer.WriteUInt16(CurrentHealth);
            writer.WriteUInt16(MaxHealth);
        }

        public static HealthUpdate Deserialize(ref BitReader reader)
        {
            return new HealthUpdate(
                reader.ReadUInt32(),
                reader.ReadUInt16(),
                reader.ReadUInt16()
            );
        }
    }

    /// <summary>
    /// Server → Client: Entity died.
    /// </summary>
    [EstimatedSize(12)]
    public readonly struct EntityDeath : IPacket<EntityDeath>, IServerPacket
    {
        public static ushort PacketId => 0x0214;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly uint Serial;
        public readonly uint KillerSerial;  // 0 = environmental/unknown
        public readonly DeathType Type;

        public EntityDeath(uint serial, uint killerSerial, DeathType type)
        {
            Serial = serial;
            KillerSerial = killerSerial;
            Type = type;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(Serial);
            writer.WriteUInt32(KillerSerial);
            writer.WriteByte((byte)Type);
        }

        public static EntityDeath Deserialize(ref BitReader reader)
        {
            return new EntityDeath(
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                (DeathType)reader.ReadByte()
            );
        }
    }

    public enum DeathType : byte
    {
        Normal = 0,
        Murder = 1,
        Suicide = 2,
        Environmental = 3,
        PvP = 4,
        Guard = 5
    }

    /// <summary>
    /// Server → Client: Entity resurrected.
    /// </summary>
    [EstimatedSize(12)]
    public readonly struct EntityResurrect : IPacket<EntityResurrect>, IServerPacket
    {
        public static ushort PacketId => 0x0215;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly uint Serial;
        public readonly ushort X;
        public readonly ushort Y;
        public readonly sbyte Z;

        public EntityResurrect(uint serial, ushort x, ushort y, sbyte z)
        {
            Serial = serial;
            X = x;
            Y = y;
            Z = z;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(Serial);
            writer.WriteUInt16(X);
            writer.WriteUInt16(Y);
            writer.WriteSByte(Z);
        }

        public static EntityResurrect Deserialize(ref BitReader reader)
        {
            return new EntityResurrect(
                reader.ReadUInt32(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadSByte()
            );
        }
    }

    /// <summary>
    /// Server → Client: Ability cooldown started.
    /// </summary>
    [EstimatedSize(8)]
    public readonly struct AbilityCooldown : IPacket<AbilityCooldown>, IServerPacket
    {
        public static ushort PacketId => 0x0216;
        public static ChannelType Channel => ChannelType.ReliableUnordered;

        public readonly ushort AbilityId;
        public readonly ushort CooldownMs;  // Duration in milliseconds

        public AbilityCooldown(ushort abilityId, ushort cooldownMs)
        {
            AbilityId = abilityId;
            CooldownMs = cooldownMs;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt16(AbilityId);
            writer.WriteUInt16(CooldownMs);
        }

        public static AbilityCooldown Deserialize(ref BitReader reader)
        {
            return new AbilityCooldown(
                reader.ReadUInt16(),
                reader.ReadUInt16()
            );
        }
    }

    /// <summary>
    /// Server → Client: Status effect applied.
    /// </summary>
    [EstimatedSize(16)]
    public readonly struct StatusEffectApplied : IPacket<StatusEffectApplied>, IServerPacket
    {
        public static ushort PacketId => 0x0217;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly uint Serial;
        public readonly ushort EffectId;
        public readonly uint SourceSerial;
        public readonly ushort Duration;  // Ticks/seconds depending on effect
        public readonly byte Stacks;

        public StatusEffectApplied(uint serial, ushort effectId, uint sourceSerial, ushort duration, byte stacks)
        {
            Serial = serial;
            EffectId = effectId;
            SourceSerial = sourceSerial;
            Duration = duration;
            Stacks = stacks;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(Serial);
            writer.WriteUInt16(EffectId);
            writer.WriteUInt32(SourceSerial);
            writer.WriteUInt16(Duration);
            writer.WriteByte(Stacks);
        }

        public static StatusEffectApplied Deserialize(ref BitReader reader)
        {
            return new StatusEffectApplied(
                reader.ReadUInt32(),
                reader.ReadUInt16(),
                reader.ReadUInt32(),
                reader.ReadUInt16(),
                reader.ReadByte()
            );
        }
    }

    /// <summary>
    /// Server → Client: Status effect removed.
    /// </summary>
    [EstimatedSize(8)]
    public readonly struct StatusEffectRemoved : IPacket<StatusEffectRemoved>, IServerPacket
    {
        public static ushort PacketId => 0x0218;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly uint Serial;
        public readonly ushort EffectId;

        public StatusEffectRemoved(uint serial, ushort effectId)
        {
            Serial = serial;
            EffectId = effectId;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(Serial);
            writer.WriteUInt16(EffectId);
        }

        public static StatusEffectRemoved Deserialize(ref BitReader reader)
        {
            return new StatusEffectRemoved(
                reader.ReadUInt32(),
                reader.ReadUInt16()
            );
        }
    }
}
