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
    /// Stable persistence identity used for saved shape membership and, when
    /// <see cref="PersistenceMode"/> is <see cref="ComponentPersistenceMode.Value"/>,
    /// the component payload. Zero makes the component unavailable to full-world
    /// persistence.
    /// </summary>
    public uint PersistenceId { get; init; }

    /// <summary>
    /// Controls whether the component contributes payload, membership only, or
    /// is reconstructed as derived state. Auto preserves the pre-3.4 behavior:
    /// a nonzero persistence ID means Value and zero means None.
    /// </summary>
    public ComponentPersistenceMode PersistenceMode { get; init; }

    /// <summary>Persists this enableable component's durable enabled state.</summary>
    public bool PersistEnableState { get; init; }
}
