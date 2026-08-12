using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Strongly typed monotonic composition/default builder used by BaseClasses.</summary>
public readonly struct BaseClassBuilder
{
    private readonly ContentDefinitionState? _state;

    internal BaseClassBuilder(ContentDefinitionState state) => _state = state;

    /// <summary>Adds data-component membership with deterministic <c>default(T)</c>.</summary>
    public void With<T>() where T : unmanaged, IComponent<T> => State.Add<T>(default, hasDefault: false);

    /// <summary>Adds data-component membership and its compiled constant default.</summary>
    public void With<T>(in T value) where T : unmanaged, IComponent<T> => State.Add(value, hasDefault: true);

    /// <summary>Adds storage-free tag membership.</summary>
    public void Tag<T>() where T : unmanaged, IComponent<T> => State.AddTag<T>();

    /// <summary>Overrides an inherited/existing member default without changing membership.</summary>
    public void Set<T>(in T value) where T : unmanaged, IComponent<T> => State.Set(value);

    private ContentDefinitionState State => _state ??
        throw new InvalidOperationException("The BaseClass builder is invalid or default initialized.");
}

/// <summary>Concrete Object Script or direct-DOD prefab composition builder.</summary>
/// <remarks>
/// <c>With</c> adds membership, <c>Tag</c> adds storage-free membership, and
/// <c>Set</c> changes an existing member's default without changing its signature.
/// </remarks>
public readonly struct PrefabBuilder
{
    private readonly ContentDefinitionState? _state;

    internal PrefabBuilder(ContentDefinitionState state) => _state = state;

    public PrefabKey Key => State.Key;

    /// <summary>Adds data-component membership with deterministic <c>default(T)</c>.</summary>
    public void With<T>() where T : unmanaged, IComponent<T> => State.Add<T>(default, hasDefault: false);

    /// <summary>Adds data-component membership and its compiled constant default.</summary>
    public void With<T>(in T value) where T : unmanaged, IComponent<T> => State.Add(value, hasDefault: true);

    /// <summary>Adds storage-free tag membership.</summary>
    public void Tag<T>() where T : unmanaged, IComponent<T> => State.AddTag<T>();

    /// <summary>Overrides an inherited/existing member default without changing membership.</summary>
    public void Set<T>(in T value) where T : unmanaged, IComponent<T> => State.Set(value);

    /// <summary>Sets optional cold diagnostic metadata; it never affects archetype identity.</summary>
    public void DisplayName(string displayName) => State.SetDisplayName(displayName);

    private ContentDefinitionState State => _state ??
        throw new InvalidOperationException("The prefab builder is invalid or default initialized.");
}

internal sealed class ContentDefinitionState
{
    private readonly ComponentSchema _schema;
    private readonly Dictionary<ComponentId, PendingPrefabComponent> _components = [];
    private bool _closed;

    internal ContentDefinitionState(ComponentSchema schema, PrefabKey key)
    {
        _schema = schema;
        Key = key;
    }

    internal PrefabKey Key { get; }
    internal string? DisplayName { get; private set; }
    internal IReadOnlyDictionary<ComponentId, PendingPrefabComponent> Components => _components;

    internal void Add<T>(in T value, bool hasDefault)
        where T : unmanaged, IComponent<T>
    {
        EnsureOpen();
        ComponentId id = GetId<T>();
        ref readonly ComponentMetadata metadata = ref _schema.GetMetadata(id);
        if (metadata.IsTag)
        {
            if (hasDefault)
                throw new ArgumentException($"Storage-free tag {typeof(T)} cannot receive a value default.");
            AddMember(id, null);
            return;
        }

        byte[]? bytes = hasDefault ? CopyBytes(value) : null;
        AddMember(id, bytes);
    }

    internal void AddTag<T>() where T : unmanaged, IComponent<T>
    {
        EnsureOpen();
        ComponentId id = GetId<T>();
        if (!_schema.GetMetadata(id).IsTag)
            throw new ArgumentException($"Component {typeof(T)} stores data and cannot be declared through Tag<T>().");
        AddMember(id, null);
    }

    internal void Set<T>(in T value) where T : unmanaged, IComponent<T>
    {
        EnsureOpen();
        ComponentId id = GetId<T>();
        ref readonly ComponentMetadata metadata = ref _schema.GetMetadata(id);
        if (metadata.IsTag)
            throw new ArgumentException($"Storage-free tag {typeof(T)} cannot receive a value default.");
        if (!_components.ContainsKey(id))
        {
            throw new InvalidOperationException(
                $"Set<{typeof(T).Name}> requires existing prefab membership. Use With<T> to add membership.");
        }
        _components[id] = new PendingPrefabComponent(id, CopyBytes(value));
    }

    internal void SetDisplayName(string displayName)
    {
        EnsureOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
    }

    internal void Close() => _closed = true;

    private void AddMember(ComponentId id, byte[]? defaultBytes)
    {
        if (!_components.TryAdd(id, new PendingPrefabComponent(id, defaultBytes)))
        {
            throw new InvalidOperationException(
                $"Component {_schema.GetCanonicalKey(id)} was added more than once to prefab {Key}.");
        }
    }

    private ComponentId GetId<T>() where T : unmanaged, IComponent<T>
    {
        if (!_schema.TryGetComponentId<T>(out ComponentId id))
            throw new ArgumentException($"Component type {typeof(T)} is absent from this content schema.");
        return id;
    }

    private static byte[] CopyBytes<T>(in T value) where T : unmanaged
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1));
        return bytes.ToArray();
    }

    private void EnsureOpen()
    {
        if (_closed)
            throw new InvalidOperationException("Prefab definition mutation is closed after content compilation.");
    }
}

internal readonly record struct PendingPrefabComponent(ComponentId Id, byte[]? DefaultBytes);
