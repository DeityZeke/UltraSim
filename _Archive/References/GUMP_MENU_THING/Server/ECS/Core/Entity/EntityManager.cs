
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Godot;

public class EntityManager : BaseSystem
{
    public static void Initialize()
    {
        SystemManager.RegisterSystem<EntityManager>();
    }

    public static bool Running => SystemManager.IsSystemEnabled<EntityManager>();

    private Dictionary<EntityType, Dictionary<EntityID, BaseEntity>> m_EntityRegistry = new();
    private Dictionary<EntityType, int> m_NextIdByType = new();

    public override string Name => "Entity Manager";

    public override void Enable()
    {
        //Console.WriteLine($"{Name} has just been enabled!");
        //Debug.WriteLine($"{Name} Enabled!");
    }

    public override void Disable()
    {
        //Console.WriteLine($"{Name} has just been disabled!");
        //Debug.WriteLine($"{Name} Disabled!");
    }

    public override void Update(double delta)
    {
        // Optionally update all entities each tick (rare in pure ECS)
    }

    public override void Save()
    {
        // TODO: Serialize all entities to disk or memory
    }

    public override void Load()
    {
        // TODO: Deserialize and rebuild all entities from disk or memory
    }

    public BaseEntity CreateEntity(EntityType type)
    {
        if (!m_NextIdByType.ContainsKey(type))
            m_NextIdByType[type] = 1;

        var id = new EntityID(type, m_NextIdByType[type]++);
        var entity = new BaseEntity(id);

        if (!m_EntityRegistry.TryGetValue(type, out var dict))
        {
            dict = new Dictionary<EntityID, BaseEntity>();
            m_EntityRegistry[type] = dict;
        }
        dict[id] = entity;
        return entity;
    }

    public bool RemoveEntity(EntityID id)
    {
        if (m_EntityRegistry.TryGetValue(id.Type, out var dict))
            return dict.Remove(id);
        return false;
    }

    public BaseEntity GetEntity(EntityID id)
    {
        if (m_EntityRegistry.TryGetValue(id.Type, out var dict) && dict.TryGetValue(id, out var entity))
            return entity;
        return null;
    }

    public IEnumerable<BaseEntity> GetEntitiesOfType(EntityType type)
    {
        return m_EntityRegistry.TryGetValue(type, out var dict) ? dict.Values : Enumerable.Empty<BaseEntity>();
    }
}