using System.Globalization;
using UltraSim.ECS;

namespace UltraSim.Server.Inventory;

public static class InventoryEcsEntityRefCodec
{
    public const string EntityPrefix = "entity:";

    public static string Format(Entity entity)
    {
        return EntityPrefix + entity.Packed.ToString(CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string stableId, out Entity entity)
    {
        entity = Entity.Null;
        if (!stableId.StartsWith(EntityPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> packedText = stableId.AsSpan(EntityPrefix.Length);
        if (!ulong.TryParse(packedText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong packed))
        {
            return false;
        }

        entity = new Entity(packed);
        return entity.IsValid();
    }
}
