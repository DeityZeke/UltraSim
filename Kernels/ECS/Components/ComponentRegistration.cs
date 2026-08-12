using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// Cold schema-construction record emitted by the source generator.
/// </summary>
public readonly struct ComponentRegistration
{
    private ComponentRegistration(
        ComponentMetadata metadata,
        ulong stableKeyHash,
        string canonicalKey,
        RuntimeTypeHandle typeHandle)
    {
        Metadata = metadata;
        StableKeyHash = stableKeyHash;
        CanonicalKey = canonicalKey;
        TypeHandle = typeHandle;
    }

    public ComponentMetadata Metadata { get; }

    public ulong StableKeyHash { get; }

    public string CanonicalKey { get; }

    public RuntimeTypeHandle TypeHandle { get; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ComponentRegistration Create<T>(
        ComponentId id,
        string canonicalKey,
        ulong stableKeyHash,
        ushort version,
        ComponentFlags flags,
        uint persistenceId,
        ComponentPersistenceMode persistenceMode = ComponentPersistenceMode.Auto)
        where T : unmanaged, IComponent<T>
    {
        ArgumentNullException.ThrowIfNull(canonicalKey);

        if (persistenceMode == ComponentPersistenceMode.Auto)
        {
            persistenceMode = persistenceId == 0
                ? ComponentPersistenceMode.None
                : ComponentPersistenceMode.Value;
        }

        bool isTag = (flags & ComponentFlags.Tag) != 0;
        int size = isTag ? 0 : Unsafe.SizeOf<T>();
        byte alignment = isTag ? (byte)1 : ComponentLayout<T>.Alignment;
        var metadata = new ComponentMetadata(
            id,
            size,
            alignment,
            version,
            flags,
            persistenceId,
            persistenceMode);

        return new ComponentRegistration(
            metadata,
            stableKeyHash,
            canonicalKey,
            typeof(T).TypeHandle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AlignmentProbe<T>
        where T : unmanaged
    {
        public byte Prefix;
        public T Value;
    }

    private static class ComponentLayout<T>
        where T : unmanaged
    {
        internal static readonly byte Alignment = checked((byte)(
            Unsafe.SizeOf<AlignmentProbe<T>>() - Unsafe.SizeOf<T>()));
    }
}
