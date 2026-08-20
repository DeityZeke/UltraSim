namespace UltraSim.ECS
{
    public abstract class SystemSettings
    {
        public virtual void Apply(ref SystemSchedule schedule)
        {
        }
    }
}
