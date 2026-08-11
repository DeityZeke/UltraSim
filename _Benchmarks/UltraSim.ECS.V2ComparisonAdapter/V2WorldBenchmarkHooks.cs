namespace UltraSim.ECS;

public sealed partial class World
{
    internal void ConfigureComparisonWorkerCount(int backgroundWorkers) =>
        _systems.ConfigureWorkerCountOverride(backgroundWorkers);
}
