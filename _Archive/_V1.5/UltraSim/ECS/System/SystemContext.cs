namespace UltraSim.ECS
{
    public readonly ref struct SystemContext
    {
        public readonly World World;
        public readonly uint Tick;
        public readonly float DeltaSeconds;
        public readonly IStructuralCommands Commands;

        public SystemContext(World world, uint tick, float deltaSeconds, IStructuralCommands commands)
        {
            World = world;
            Tick = tick;
            DeltaSeconds = deltaSeconds;
            Commands = commands;
        }
    }
}
