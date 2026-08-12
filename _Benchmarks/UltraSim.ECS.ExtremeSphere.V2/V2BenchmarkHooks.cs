namespace UltraSim.ECS;

// The V2 sources are compiled into this isolated adapter. This benchmark-only
// partial exposes V2's existing internal deterministic worker-count control.
public sealed partial class World
{
    internal void ConfigureExtremeSphereWorkerCount(int workerCount) =>
        _systems.ConfigureWorkerCountOverride(workerCount);
}
