using Godot;
using UltraSim.Client.Core;
using UltraSim.Game.Composition;
using UltraSim.Game.World;

namespace UltraSim.Client.Godot;

/// <summary>
/// Bounded stock pick volumes for the current primitive/public presentation vocabulary. Picking
/// only nominates a disclosed target; Game remains responsible for interaction validation.
/// </summary>
public static class GodotPublicPointerPicker
{
    public const float MaximumPickDistanceMeters = 100f;

    /// <summary>
    /// Presentation may realize equipped gear as a public visual, but only an independently
    /// grounded item is a world-item pointer target. Other admitted object kinds retain their
    /// existing visible-target behavior.
    /// </summary>
    public static bool IsPointerTarget(uint prefabId, WorldPublicStateFlags stateFlags) =>
        (stateFlags & WorldPublicStateFlags.Visible) != 0 &&
        (prefabId != GameWorldPublicPrefabIds.IronSword ||
         (stateFlags & WorldPublicStateFlags.GroundItem) != 0);

    public static bool TryIntersect(
        uint prefabId,
        in Vector3 rayOrigin,
        in Vector3 rayDirection,
        in Vector3 basePosition,
        out float distance)
    {
        if (!HasKnownPickVolume(prefabId))
        {
            distance = 0;
            return false;
        }
        ClientPrimitivePresentation bounds = ClientPrimitivePresentationCatalog.ForPublic(prefabId);
        Vector3 minimum = basePosition + new Vector3(
            bounds.MinimumXMillimeters, bounds.MinimumYMillimeters,
            bounds.MinimumZMillimeters) * 0.001f;
        Vector3 maximum = basePosition + new Vector3(
            bounds.MaximumXMillimeters, bounds.MaximumYMillimeters,
            bounds.MaximumZMillimeters) * 0.001f;
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

    private static bool HasKnownPickVolume(uint prefabId)
    {
        return prefabId is GameWorldPublicPrefabIds.CabinDoor or
            GameWorldPublicPrefabIds.WorldChest or
            GameWorldPublicPrefabIds.WoodlandAnimal or
            GameWorldPublicPrefabIds.SkyTelepad or
            GameWorldPublicPrefabIds.FarGate or
            GameWorldPublicPrefabIds.InformationalSign or
            GameWorldPublicPrefabIds.IronSword;
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
