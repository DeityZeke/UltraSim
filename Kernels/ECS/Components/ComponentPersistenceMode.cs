namespace UltraSim.ECS;

/// <summary>Cold persistence policy for one component type.</summary>
public enum ComponentPersistenceMode : byte
{
    /// <summary>Infer Value for a nonzero persistence ID; otherwise None.</summary>
    Auto = 0,
    /// <summary>The component cannot appear in a reconstructible saved shape.</summary>
    None = 1,
    /// <summary>Save membership and authoritative component payload.</summary>
    Value = 2,
    /// <summary>Save membership and enable state but reconstruct the default value.</summary>
    MembershipOnly = 3,
    /// <summary>Save membership; a registered reconstructor must derive the value.</summary>
    Derived = 4,
}
