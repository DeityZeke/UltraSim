namespace UltraSim.Framework.Definitions;

/// <summary>Cold authoring owner identity; independent of runtime package implementation.</summary>
public readonly record struct DefinitionPackageId : IComparable<DefinitionPackageId>
{
    public DefinitionPackageId(string value) { DefinitionIdentity.Validate(value); Value = value; }
    public string Value { get; }
    public bool IsValid => Value is not null;
    public int CompareTo(DefinitionPackageId other) => StringComparer.Ordinal.Compare(Value, other.Value);
    public override string ToString() => Value ?? "<invalid>";
}

/// <summary>Selected behavior capability; absent optional Core never erases authored facet meaning.</summary>
public readonly record struct DefinitionCapabilityId
{
    public DefinitionCapabilityId(string value) { DefinitionIdentity.Validate(value); Value = value; }
    public string Value { get; }
    public bool IsValid => Value is not null;
    public override string ToString() => Value ?? "<invalid>";
}

/// <summary>Stable authored semantic identity; never an ECS entity, prefab or dense handle.</summary>
public readonly record struct StableDefinitionId : IComparable<StableDefinitionId>
{
    public StableDefinitionId(string value) { DefinitionIdentity.Validate(value); Value = value; }
    public string Value { get; }
    public bool IsValid => Value is not null;
    public int CompareTo(StableDefinitionId other) => StringComparer.Ordinal.Compare(Value, other.Value);
    public override string ToString() => Value ?? "<invalid>";
}

/// <summary>Stable namespace of a disclosed semantic presentation reference.</summary>
public readonly record struct DefinitionDomainId
{
    public DefinitionDomainId(string value) { DefinitionIdentity.Validate(value); Value = value; }
    public string Value { get; }
    public bool IsValid => Value is not null;
}

/// <summary>Stable semantic presentation role, not an asset path or renderer object.</summary>
public readonly record struct PresentationRoleId
{
    public PresentationRoleId(string value) { DefinitionIdentity.Validate(value); Value = value; }
    public string Value { get; }
    public bool IsValid => Value is not null;
    public static PresentationRoleId World => new("world");
    public static PresentationRoleId InventoryPortrait => new("inventory-portrait");
}

/// <summary>Public identity requested by presentation; no asset or private gameplay fields.</summary>
public readonly record struct SemanticPresentationReference(
    DefinitionDomainId Domain, StableDefinitionId Definition, PresentationRoleId Role)
{
    public bool IsValid => Domain.IsValid && Definition.IsValid && Role.IsValid;
}

/// <summary>Stable authored facet family identity.</summary>
public readonly record struct DefinitionFacetId : IComparable<DefinitionFacetId>
{
    public DefinitionFacetId(string value) { DefinitionIdentity.Validate(value); Value = value; }
    public string Value { get; }
    public bool IsValid => Value is not null;
    public int CompareTo(DefinitionFacetId other) => StringComparer.Ordinal.Compare(Value, other.Value);
}

/// <summary>Stable authored group identity; membership is resolved to dense handles on freeze.</summary>
public readonly record struct DefinitionTagId : IComparable<DefinitionTagId>
{
    public DefinitionTagId(string value) { DefinitionIdentity.Validate(value); Value = value; }
    public string Value { get; }
    public bool IsValid => Value is not null;
    public int CompareTo(DefinitionTagId other) => StringComparer.Ordinal.Compare(Value, other.Value);
}

/// <summary>Process/composition-local index. Persistence must use StableDefinitionId.</summary>
public readonly record struct DefinitionHandle(int Index, ulong Generation)
{
    public bool IsValid => Index > 0 && Generation != 0;
}

/// <summary>Typed facet registration; Core availability is distinct from authored meaning.</summary>
public sealed class DefinitionFacetFamily<T> where T : struct
{
    public DefinitionFacetFamily(DefinitionFacetId id, DefinitionPackageId contractsOwner,
        DefinitionCapabilityId runtimeCapability = default, Func<StableDefinitionId, T, bool>? validate = null)
    {
        if (!id.IsValid || !contractsOwner.IsValid ||
            runtimeCapability.Value is not null && !runtimeCapability.IsValid)
            throw new ArgumentException("Invalid facet identity, Contracts owner or runtime capability.");
        Id = id; ContractsOwner = contractsOwner; RuntimeCapability = runtimeCapability;
        Validate = validate;
    }
    public DefinitionFacetId Id { get; }
    public DefinitionPackageId ContractsOwner { get; }
    public DefinitionCapabilityId RuntimeCapability { get; }
    internal Func<StableDefinitionId, T, bool>? Validate { get; }
}

internal static class DefinitionIdentity
{
    internal static void Validate(string? value)
    {
        if (value is not { Length: >= 1 and <= 96 } || value[0] is < 'a' or > 'z' || value[^1] == '.')
            throw new ArgumentException("Identity must be 1–96 canonical ASCII characters.");
        char previous = '\0';
        foreach (char c in value)
        {
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_') ||
                c == '.' && previous == '.')
                throw new ArgumentException("Identity contains noncanonical characters.");
            previous = c;
        }
    }
}
