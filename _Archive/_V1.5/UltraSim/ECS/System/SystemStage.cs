namespace UltraSim.ECS
{
    public enum SystemStage : byte
    {
        Input = 0,
        Movement = 1,
        SpatialIndex = 2,
        Perception = 3,
        Combat = 4,
        AI = 5,
        WorldSim = 6,
        Networking = 7,
        Cleanup = 8,
        Count = 9
    }
}
