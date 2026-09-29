namespace UltraSim.Framework.RuntimeHost;

/// <summary>Stable owner identity for sparse runtime state; it is not an ECS Entity or CLR type.</summary>
public readonly record struct ExtensionOwnerId
{
    public static ExtensionOwnerId Global { get; } = new("global");
    public ExtensionOwnerId(string value) { RuntimeIdentity.Validate(value, nameof(value)); Value = value; }
    public string Value { get; }
    public bool IsValid => Value is not null;
}

/// <summary>Stable Object Extension identity, independent of its implementing package.</summary>
public readonly record struct ExtensionTypeId
{
    public ExtensionTypeId(string value) { RuntimeIdentity.Validate(value, nameof(value)); Value = value; }
    public string Value { get; }
    public bool IsValid => Value is not null;
}

/// <summary>A family can have multiple named instances on one owner. "default" is the singleton convenience key.</summary>
public readonly record struct ExtensionKey
{
    public const string DefaultInstance = "default";
    public ExtensionKey(ExtensionOwnerId owner, ExtensionTypeId type, string instance = DefaultInstance)
    {
        if (!owner.IsValid || !type.IsValid) throw new ArgumentException("Invalid Extension owner or type.");
        RuntimeIdentity.Validate(instance, nameof(instance));
        Owner = owner; Type = type; Instance = instance;
    }
    public ExtensionOwnerId Owner { get; }
    public ExtensionTypeId Type { get; }
    public string Instance { get; }
    public bool IsValid => Owner.IsValid && Type.IsValid && Instance is not null;
}

/// <summary>Cold family binding for sparse managed Object Extensions. Bulk state remains in ECS.</summary>
public sealed class ExtensionRegistry
{
    public const int MaximumExtensions = 4096;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<ExtensionTypeId, IExtensionFamily> _families = new();
    private int _count;
    public int Count { get { EnsureOwner(); return _count; } }
    internal void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Extension owner thread required.");
    }
    public ExtensionFamily<T> RegisterFamily<T>(ExtensionTypeId type) where T : class
    {
        EnsureOwner();
        if (!type.IsValid) throw new ArgumentException("Invalid Extension type.", nameof(type));
        if (_families.Count == MaximumExtensions || _families.ContainsKey(type))
            throw new InvalidOperationException("Extension family is duplicated or capacity is exhausted.");
        var family = new ExtensionFamily<T>(this, type);
        _families.Add(type, family);
        return family;
    }
    internal void EnsureCanAdd()
    {
        EnsureOwner();
        if (_count == MaximumExtensions) throw new InvalidOperationException("Extension capacity exhausted.");
    }
    internal void Added() { EnsureOwner(); _count++; }
    internal void Removed() { EnsureOwner(); _count--; }
    public void Clear()
    {
        EnsureOwner();
        foreach (IExtensionFamily family in _families.Values) family.Clear();
        _count = 0;
    }
}

internal interface IExtensionFamily { void Clear(); }

/// <summary>Retained typed family; type identity is bound once, not cast on each lookup.</summary>
public sealed class ExtensionFamily<T> : IExtensionFamily where T : class
{
    private readonly ExtensionRegistry _registry;
    private readonly Dictionary<(ExtensionOwnerId Owner, string Instance), T> _values = new();
    internal ExtensionFamily(ExtensionRegistry registry, ExtensionTypeId type) { _registry = registry; Type = type; }
    public ExtensionTypeId Type { get; }
    public int Count { get { _registry.EnsureOwner(); return _values.Count; } }
    private (ExtensionOwnerId Owner, string Instance) Validate(ExtensionKey key)
    {
        if (!key.IsValid || key.Type != Type) throw new ArgumentException("Wrong Extension family identity.", nameof(key));
        return (key.Owner, key.Instance);
    }
    public void Add(ExtensionKey key, T value)
    {
        _registry.EnsureOwner(); ArgumentNullException.ThrowIfNull(value);
        var identity = Validate(key);
        if (_values.ContainsKey(identity)) throw new InvalidOperationException("Duplicate Extension identity.");
        _registry.EnsureCanAdd();
        _values.Add(identity, value);
        _registry.Added();
    }
    public bool TryGet(ExtensionKey key, out T? value)
    { _registry.EnsureOwner(); return _values.TryGetValue(Validate(key), out value); }
    public bool Remove(ExtensionKey key)
    {
        _registry.EnsureOwner();
        if (!_values.Remove(Validate(key))) return false;
        _registry.Removed(); return true;
    }
    void IExtensionFamily.Clear() => _values.Clear();
}
