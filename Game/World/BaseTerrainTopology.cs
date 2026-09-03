using System.Runtime.InteropServices;

namespace UltraSim.Game.World;

/// <summary>Authoritative topology result for one resident base-terrain X/Z location.</summary>
public enum BaseTerrainSurfaceStatus : byte
{
    None = 0,
    Present = 1,
    Absent = 2,
    Unavailable = 3,
}

/// <summary>
/// Complete base-surface result. Height and material are meaningful only when status is Present.
/// Default/uninitialized values fail closed and are never returned by a completed probe.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 12)]
public readonly record struct BaseTerrainSurfaceSample(
    BaseTerrainSurfaceStatus Status,
    int HeightMillimeters,
    ushort MaterialId)
{
    public bool IsPresent =>
        Status == BaseTerrainSurfaceStatus.Present && MaterialId != 0;

    public static BaseTerrainSurfaceSample Absent =>
        new(BaseTerrainSurfaceStatus.Absent, 0, 0);

    public static BaseTerrainSurfaceSample Unavailable =>
        new(BaseTerrainSurfaceStatus.Unavailable, 0, 0);
}
