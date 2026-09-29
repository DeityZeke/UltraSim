namespace UltraSim.Framework.RuntimeHost;

/// <summary>Stable owner identity for sparse runtime state; it is not an ECS Entity or CLR type.</summary>
public readonly record struct SidecarOwnerId
{
    public static SidecarOwnerId Global { get; } = new("global");
    public SidecarOwnerId(string value) { ModuleIdentity.Validate(value, nameof(value)); Value = value; }
    public string Value { get; }
    public bool IsValid => Value is not null;
}

/// <summary>A family can have multiple named instances on one owner. "default" is the singleton convenience key.</summary>
public readonly record struct SidecarKey
{
    public const string DefaultInstance = "default";
    public SidecarKey(SidecarOwnerId owner, ModulePackageId family, string instance = DefaultInstance)
    {
        if (!owner.IsValid || !family.IsValid) throw new ArgumentException("Invalid sidecar owner or family.");
        ModuleIdentity.Validate(instance, nameof(instance));
        Owner = owner; Family = family; Instance = instance;
    }
    public SidecarOwnerId Owner { get; }
    public ModulePackageId Family { get; }
    public string Instance { get; }
    public bool IsValid => Owner.IsValid && Family.IsValid && Instance is not null;
}

/// <summary>Cold, bounded sparse OOP storage. Bulk high-cardinality simulation remains an ECS concern.</summary>
public sealed class RuntimeSidecars
{
    public const int MaximumSidecars = 4096;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<SidecarKey, object> _values = new();
    public int Count { get { EnsureOwner(); return _values.Count; } }
    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Sidecar owner thread required.");
    }
    public void Add<T>(SidecarKey key, T value) where T : class
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(value);
        if (!key.IsValid) throw new ArgumentException("Invalid sidecar identity.", nameof(key));
        if (_values.Count == MaximumSidecars) throw new InvalidOperationException("Sidecar capacity exhausted.");
        if (!_values.TryAdd(key, value)) throw new InvalidOperationException("Duplicate sidecar identity.");
    }
    public bool TryGet<T>(SidecarKey key, out T? value) where T : class
    {
        EnsureOwner();
        if (!key.IsValid) throw new ArgumentException("Invalid sidecar identity.", nameof(key));
        if (_values.TryGetValue(key, out object? stored))
        {
            value = stored as T;
            if (value is null) throw new InvalidOperationException("Sidecar identity was requested with the wrong contract type.");
            return true;
        }
        value = null; return false;
    }
    public bool Remove(SidecarKey key)
    { EnsureOwner(); if (!key.IsValid) throw new ArgumentException("Invalid sidecar identity.", nameof(key)); return _values.Remove(key); }
    public void Clear() { EnsureOwner(); _values.Clear(); }
}
