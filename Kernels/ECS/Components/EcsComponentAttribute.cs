namespace UltraSim.ECS;

/// <summary>
/// Declares the stable identity and storage traits of an ECS component.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class EcsComponentAttribute : Attribute
{
    public EcsComponentAttribute(string canonicalKey)
    {
        CanonicalKey = canonicalKey;
    }

    public string CanonicalKey { get; }

    public ushort Version { get; init; } = 1;

    public bool IsTag { get; init; }

    public bool IsEnableable { get; init; }

    /// <summary>
    /// Stable persistence identity. Zero means that the component is transient.
    /// </summary>
    public uint PersistenceId { get; init; }
}
