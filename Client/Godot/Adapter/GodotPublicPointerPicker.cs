using Godot;
using UltraSim.Game.Composition;

namespace UltraSim.Client.Godot;

/// <summary>
/// Bounded stock pick volumes for the current primitive/public presentation vocabulary. Picking
/// only nominates a disclosed target; Game remains responsible for interaction validation.
/// </summary>
public static class GodotPublicPointerPicker
{
    public const float MaximumPickDistanceMeters = 100f;

    public static bool TryIntersect(
        uint prefabId,
        in Vector3 rayOrigin,
        in Vector3 rayDirection,
        in Vector3 basePosition,
        out float distance)
    {
        if (!TryGetBounds(prefabId, out Vector3 halfExtents, out float height))
        {
            distance = 0;
            return false;
        }

        Vector3 minimum = basePosition - new Vector3(halfExtents.X, 0, halfExtents.Z);
        Vector3 maximum = basePosition + new Vector3(halfExtents.X, height, halfExtents.Z);
        float near = 0;
        float far = MaximumPickDistanceMeters;
        if (!ClipAxis(rayOrigin.X, rayDirection.X, minimum.X, maximum.X, ref near, ref far) ||
            !ClipAxis(rayOrigin.Y, rayDirection.Y, minimum.Y, maximum.Y, ref near, ref far) ||
            !ClipAxis(rayOrigin.Z, rayDirection.Z, minimum.Z, maximum.Z, ref near, ref far))
        {
            distance = 0;
            return false;
        }

        distance = near;
        return true;
    }

    private static bool TryGetBounds(uint prefabId, out Vector3 halfExtents, out float height)
    {
        (halfExtents, height) = prefabId switch
        {
            GameWorldPublicPrefabIds.CabinDoor => (new Vector3(0.9f, 0, 0.35f), 2.1f),
            GameWorldPublicPrefabIds.WorldChest => (new Vector3(0.7f, 0, 0.5f), 0.8f),
            GameWorldPublicPrefabIds.WoodlandAnimal => (new Vector3(0.65f, 0, 0.4f), 1.15f),
            GameWorldPublicPrefabIds.SkyTelepad => (new Vector3(1f, 0, 1f), 0.25f),
            GameWorldPublicPrefabIds.FarGate => (new Vector3(0.85f, 0, 0.4f), 2.8f),
            GameWorldPublicPrefabIds.InformationalSign => (new Vector3(0.7f, 0, 0.25f), 1.65f),
            _ => default,
        };
        return height > 0;
    }

    private static bool ClipAxis(
        float origin,
        float direction,
        float minimum,
        float maximum,
        ref float near,
        ref float far)
    {
        if (MathF.Abs(direction) < 0.000001f)
            return origin >= minimum && origin <= maximum;
        float first = (minimum - origin) / direction;
        float second = (maximum - origin) / direction;
        if (first > second) (first, second) = (second, first);
        near = MathF.Max(near, first);
        far = MathF.Min(far, second);
        return near <= far && far >= 0;
    }
}
