using System.Buffers.Binary;
using UltraSim.ECS;

namespace UltraSim.Game.Inventory;

/// <summary>Small deterministic item vocabulary used by the S0.7 fixture.</summary>
public enum InventoryItemKind : byte
{
    Bread = 1,
    IronSword = 2,
}

/// <summary>Equipment slots represented by an item's ECS relationship.</summary>
public enum InventoryEquipSlot : byte
{
    None = 0,
    MainHand = 1,
}

/// <summary>Persistent marker for a playable character entity.</summary>
public struct InventoryCharacter : IComponent<InventoryCharacter>
{
    public int Marker;
}

/// <summary>
/// Persistent container truth. Items reference this entity while contained;
/// the index is rebuilt from these ECS relationships.
/// </summary>
public struct InventoryContainer : IComponent<InventoryContainer>
{
    [PersistenceEntityReference]
    public Entity Owner;
    public int Capacity;
}

/// <summary>
/// Persistent item truth. Parent is either an InventoryContainer or a
/// character entity when the item is equipped.
/// </summary>
public struct InventoryItem : IComponent<InventoryItem>
{
    [PersistenceEntityReference]
    public Entity Parent;
    public InventoryItemKind Kind;
    public InventoryEquipSlot EquipSlot;
    public int Quantity;
}

/// <summary>Stable component identities for the game-owned inventory schema.</summary>
public static class InventoryComponentIds
{
    public static readonly ComponentId Character = new(0);
    public static readonly ComponentId Container = new(1);
    public static readonly ComponentId Item = new(2);
}

/// <summary>Creates the bounded S0.7 schema and its custom persistence catalog.</summary>
public static class InventorySchema
{
    /// <summary>
    /// Returns the stable inventory registrations so a real game schema can
    /// compose them with other game-owned component registrations.
    /// </summary>
    public static ComponentRegistration[] CreateRegistrations() =>
        [
            ComponentRegistration.Create<InventoryCharacter>(
                InventoryComponentIds.Character,
                "game.inventory.character",
                StableHash64.Compute("game.inventory.character"),
                version: 1,
                ComponentFlags.Persistent,
                persistenceId: 700701,
                ComponentPersistenceMode.Value),
            ComponentRegistration.Create<InventoryContainer>(
                InventoryComponentIds.Container,
                "game.inventory.container",
                StableHash64.Compute("game.inventory.container"),
                version: 1,
                ComponentFlags.Persistent,
                persistenceId: 700702,
                ComponentPersistenceMode.Value),
            ComponentRegistration.Create<InventoryItem>(
                InventoryComponentIds.Item,
                "game.inventory.item",
                StableHash64.Compute("game.inventory.item"),
                version: 1,
                ComponentFlags.Persistent,
                persistenceId: 700703,
                ComponentPersistenceMode.Value),
        ];

    public static ComponentSchema CreateSchema() => ComponentSchema.Create(
        new SchemaId(0x70, 0x07),
        CreateRegistrations());

    public static PersistenceCatalog CreateCatalog(ComponentSchema schema) =>
        new PersistenceCatalogBuilder(schema)
            .Register<InventoryCharacter>(
                new PersistenceSchemaFingerprint(0x7007, 0x0701),
                new InventoryCharacterCodec())
            .Register<InventoryContainer>(
                new PersistenceSchemaFingerprint(0x7007, 0x0702),
                new InventoryContainerCodec())
            .Register<InventoryItem>(
                new PersistenceSchemaFingerprint(0x7007, 0x0703),
                new InventoryItemCodec())
            .Build();
}

internal sealed class InventoryCharacterCodec : IPersistenceComponentCodec<InventoryCharacter>
{
    public int EncodedSize => sizeof(int);

    public void Encode(in InventoryCharacter value, Span<byte> destination, PersistenceWriteContext context) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, value.Marker);

    public void Decode(ReadOnlySpan<byte> source, ref InventoryCharacter value, PersistenceReadContext context) =>
        value.Marker = BinaryPrimitives.ReadInt32LittleEndian(source);
}

internal sealed class InventoryContainerCodec : IPersistenceComponentCodec<InventoryContainer>
{
    public int EncodedSize => sizeof(uint) + sizeof(int);

    public void Encode(in InventoryContainer value, Span<byte> destination, PersistenceWriteContext context)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, context.EncodeEntityReference(value.Owner).Value);
        BinaryPrimitives.WriteInt32LittleEndian(destination[sizeof(uint)..], value.Capacity);
    }

    public void Decode(ReadOnlySpan<byte> source, ref InventoryContainer value, PersistenceReadContext context)
    {
        value.Owner = context.DecodeEntityReference(new SaveEntityOrdinal(
            BinaryPrimitives.ReadUInt32LittleEndian(source)));
        value.Capacity = BinaryPrimitives.ReadInt32LittleEndian(source[sizeof(uint)..]);
    }
}

internal sealed class InventoryItemCodec : IPersistenceComponentCodec<InventoryItem>
{
    public int EncodedSize => sizeof(uint) + sizeof(byte) + sizeof(byte) + sizeof(int);

    public void Encode(in InventoryItem value, Span<byte> destination, PersistenceWriteContext context)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, context.EncodeEntityReference(value.Parent).Value);
        destination[sizeof(uint)] = (byte)value.Kind;
        destination[sizeof(uint) + sizeof(byte)] = (byte)value.EquipSlot;
        BinaryPrimitives.WriteInt32LittleEndian(
            destination[(sizeof(uint) + sizeof(byte) + sizeof(byte))..], value.Quantity);
    }

    public void Decode(ReadOnlySpan<byte> source, ref InventoryItem value, PersistenceReadContext context)
    {
        value.Parent = context.DecodeEntityReference(new SaveEntityOrdinal(
            BinaryPrimitives.ReadUInt32LittleEndian(source)));
        value.Kind = (InventoryItemKind)source[sizeof(uint)];
        value.EquipSlot = (InventoryEquipSlot)source[sizeof(uint) + sizeof(byte)];
        value.Quantity = BinaryPrimitives.ReadInt32LittleEndian(
            source[(sizeof(uint) + sizeof(byte) + sizeof(byte))..]);
    }
}
