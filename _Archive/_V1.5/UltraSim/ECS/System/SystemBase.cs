namespace UltraSim.ECS
{
    public abstract class SystemBase
    {
        public SystemId Id { get; internal set; } = SystemId.Invalid;
        public abstract SystemDescriptor Descriptor { get; }
        public virtual SystemSettings? Settings => null;

        public bool Enabled { get; internal set; } = true;

        public virtual void OnInitialize(World world) { }
        public abstract void Update(in SystemContext ctx);
        public virtual void OnShutdown(World world) { }

        /// <summary>
        /// Called by World.SaveAll(writer) during a world save.
        /// Write any system-owned persistent state to writer.
        /// Default implementation writes nothing.
        /// </summary>
        public virtual void Serialize(IDataWriter writer) { }

        /// <summary>
        /// Called by World.LoadAll(reader) during a world load.
        /// Read system-owned persistent state from reader in the same order as Serialize.
        /// Default implementation reads nothing.
        /// </summary>
        public virtual void Deserialize(IDataReader reader) { }
    }
}
