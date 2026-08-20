namespace UltraSim.ECS
{
    public struct SystemSchedule
    {
        public bool Enabled;
        public int PeriodTicks;
        public int StartOffset;
        public SystemStage Stage;
        public int Priority;

        public static SystemSchedule FromDescriptor(in SystemDescriptor descriptor)
        {
            return new SystemSchedule
            {
                Enabled = true,
                PeriodTicks = descriptor.DefaultPeriodTicks,
                StartOffset = descriptor.DefaultStartOffset,
                Stage = descriptor.Stage,
                Priority = 0
            };
        }
    }
}
