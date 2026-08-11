namespace UltraSim.ECS;

/// <summary>
/// Internal structural executor policy. Reference executors exist only for
/// differential verification and same-process measurement.
/// </summary>
internal enum StructuralPlaybackExecutor : byte
{
    Production = 0,
    PackedOracle = 1,
    ScalarOracle = 2,
}
