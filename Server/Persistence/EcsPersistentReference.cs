using UltraSim.ECS;
using UltraSim.Framework.Persistence;

namespace UltraSim.Server.Persistence;

/// <summary>Only this ECS bridge maps its world/entity identity into the neutral save-set reference key.</summary>
public static class EcsPersistentReference
{
    public const string Domain = "ecs.entity";
    public static PersistentReferenceKey From(PersistentEntityKey key)
    {
        if (!key.IsValid) throw new ArgumentException("Invalid ECS persistent entity key.", nameof(key));
        return new(Domain, key.World.Value, key.Entity.Value);
    }
    public static bool TryToEcs(PersistentReferenceKey key, out PersistentEntityKey entity)
    {
        if (key.IsValid && key.Domain == Domain)
        {
            entity = new(new PersistentWorldId(key.Scope), new PersistentEntityId(key.Local));
            return true;
        }
        entity = default; return false;
    }
    public static bool TryResolve(this IPersistentReferenceResolver resolver, PersistentEntityKey key, out object? value)
        => resolver.TryResolve(From(key), out value);
}
