using System;

namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// Marks a component or system type with a stable, explicit persistence identity.
    /// The id must be unique across all registered persistent types and must never change
    /// once assigned — it is the stable key written to and read from save files.
    /// Do NOT use CLR type names or runtime ids as persistence identities.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class PersistenceIdAttribute : Attribute
    {
        public uint Id { get; }

        public PersistenceIdAttribute(uint id)
        {
            Id = id;
        }
    }
}
