namespace UltraSim.ECS;

/// <summary>Marks one explicitly registered concrete authoring class as spawnable content.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ObjectScriptAttribute : Attribute
{
    public ObjectScriptAttribute(string key) => Key = new PrefabKey(key).Value;

    public string Key { get; }
}
