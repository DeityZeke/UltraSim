
using System;

using Godot;


public class BaseEntity
{
    public EntityID Id { get; }

    public BaseEntity(EntityID id)
    {
        Id = id;
    }

    public virtual void Save(){}

    public virtual void Load() { }
}