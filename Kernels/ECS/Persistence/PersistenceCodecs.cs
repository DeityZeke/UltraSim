using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Marks an <see cref="Entity"/> field/property as an ordinal-remapped snapshot reference.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class PersistenceEntityReferenceAttribute : Attribute;

/// <summary>Stable 128-bit identity of one canonical persistence schema/codec contract.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 16)]
public readonly struct PersistenceSchemaFingerprint : IEquatable<PersistenceSchemaFingerprint>
{
    public PersistenceSchemaFingerprint(ulong low, ulong high)
    {
        if ((low | high) == 0) throw new ArgumentException("A persistence fingerprint cannot be zero.");
        Low = low;
        High = high;
    }
    public ulong Low { get; }
    public ulong High { get; }
    public bool IsValid => (Low | High) != 0;
    public bool Equals(PersistenceSchemaFingerprint other) => Low == other.Low && High == other.High;
    public override bool Equals(object? obj) => obj is PersistenceSchemaFingerprint other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Low, High);
}

/// <summary>Reference remapper available to generated/custom component encoders.</summary>
public sealed class PersistenceWriteContext
{
    private readonly WorldId _worldId;
    private readonly int[] _ordinalByEntityIndex;
    private readonly uint[] _generationByEntityIndex;
    internal PersistenceWriteContext(WorldId worldId, int[] ordinalByEntityIndex, uint[] generationByEntityIndex)
    {
        _worldId = worldId;
        _ordinalByEntityIndex = ordinalByEntityIndex;
        _generationByEntityIndex = generationByEntityIndex;
    }
    public SaveEntityOrdinal EncodeEntityReference(Entity entity)
    {
        if (entity == Entity.Null) return new SaveEntityOrdinal(SaveEntityOrdinal.NullValue);
        if (entity.World != _worldId || entity.Index >= unchecked((uint)_ordinalByEntityIndex.Length) ||
            _generationByEntityIndex[entity.Index] != entity.Generation)
            throw new PersistenceValidationException("An entity-reference field contains a stale or foreign runtime entity.");
        int ordinal = _ordinalByEntityIndex[entity.Index];
        if (ordinal < 0) throw new PersistenceValidationException("An entity-reference target is outside the whole-world save set.");
        return new SaveEntityOrdinal(checked((uint)ordinal));
    }
}

/// <summary>Dense ordinal remapper available to generated/custom component readers.</summary>
public sealed class PersistenceReadContext
{
    private readonly Entity[] _entities;
    internal PersistenceReadContext(Entity[] entities) => _entities = entities;
    public Entity DecodeEntityReference(SaveEntityOrdinal ordinal)
    {
        if (ordinal.IsNull) return Entity.Null;
        if (ordinal.Value >= unchecked((uint)_entities.Length))
            throw new PersistenceValidationException("An entity-reference ordinal is outside the snapshot entity table.");
        return _entities[ordinal.Value];
    }
}

/// <summary>Fixed-width canonical codec for the current version of one component.</summary>
public interface IPersistenceComponentCodec<T>
    where T : unmanaged, IComponent<T>
{
    int EncodedSize { get; }
    void Encode(in T value, Span<byte> destination, PersistenceWriteContext context);
    void Decode(ReadOnlySpan<byte> source, ref T value, PersistenceReadContext context);
}

/// <summary>Direct historical-version-to-current reader; migration chains are not used.</summary>
public interface IPersistenceComponentReader<T>
    where T : unmanaged, IComponent<T>
{
    int EncodedSize { get; }
    void Decode(ReadOnlySpan<byte> source, ref T value, PersistenceReadContext context);
}

/// <summary>Optional generated/custom batch codec over canonical contiguous component values.</summary>
public interface IPersistenceBatchComponentCodec<T> : IPersistenceComponentCodec<T>
    where T : unmanaged, IComponent<T>
{
    void EncodeBatch(ReadOnlySpan<T> values, Span<byte> destination, PersistenceWriteContext context);
    void DecodeBatch(ReadOnlySpan<byte> source, Span<T> values, PersistenceReadContext context);
}

internal interface IBoundPersistenceCodec
{
    ComponentId ComponentId { get; }
    uint PersistenceId { get; }
    ushort CurrentVersion { get; }
    int EncodedSize { get; }
    PersistenceSchemaFingerprint Fingerprint { get; }
    void Encode(ReadOnlySpan<byte> value, Span<byte> destination, PersistenceWriteContext context);
    void Decode(ReadOnlySpan<byte> source, Span<byte> value, PersistenceReadContext context);
    void EncodeBatch(ReadOnlySpan<byte> values, int count, Span<byte> destination, PersistenceWriteContext context);
    void DecodeBatch(ReadOnlySpan<byte> source, int count, Span<byte> values, PersistenceReadContext context);
}

internal sealed class BoundPersistenceCodec<T> : IBoundPersistenceCodec
    where T : unmanaged, IComponent<T>
{
    private readonly IPersistenceComponentCodec<T> _codec;
    internal BoundPersistenceCodec(ComponentMetadata metadata, PersistenceSchemaFingerprint fingerprint, IPersistenceComponentCodec<T> codec)
    {
        ComponentId = metadata.Id;
        PersistenceId = metadata.PersistenceId;
        CurrentVersion = metadata.Version;
        Fingerprint = fingerprint;
        _codec = codec;
        EncodedSize = codec.EncodedSize;
        if (EncodedSize <= 0) throw new ArgumentOutOfRangeException(nameof(codec), "Value-component encoded size must be positive.");
    }
    public ComponentId ComponentId { get; }
    public uint PersistenceId { get; }
    public ushort CurrentVersion { get; }
    public int EncodedSize { get; }
    public PersistenceSchemaFingerprint Fingerprint { get; }
    public void Encode(ReadOnlySpan<byte> value, Span<byte> destination, PersistenceWriteContext context)
    {
        T typed = MemoryMarshal.Read<T>(value);
        _codec.Encode(typed, destination, context);
    }
    public void Decode(ReadOnlySpan<byte> source, Span<byte> value, PersistenceReadContext context)
    {
        T typed = default;
        _codec.Decode(source, ref typed, context);
        MemoryMarshal.Write(value, in typed);
    }
    public void EncodeBatch(ReadOnlySpan<byte> values, int count, Span<byte> destination, PersistenceWriteContext context)
    {
        ReadOnlySpan<T> typed = MemoryMarshal.Cast<byte, T>(values);
        if (typed.Length != count || destination.Length != checked(count * EncodedSize))
            throw new ArgumentException("Persistence batch dimensions are inconsistent.");
        if (_codec is IPersistenceBatchComponentCodec<T> batch)
        {
            batch.EncodeBatch(typed, destination, context);
            return;
        }
        for (int index = 0; index < count; index++) _codec.Encode(typed[index], destination.Slice(index * EncodedSize, EncodedSize), context);
    }
    public void DecodeBatch(ReadOnlySpan<byte> source, int count, Span<byte> values, PersistenceReadContext context)
    {
        Span<T> typed = MemoryMarshal.Cast<byte, T>(values);
        if (typed.Length != count || source.Length != checked(count * EncodedSize))
            throw new ArgumentException("Persistence batch dimensions are inconsistent.");
        if (_codec is IPersistenceBatchComponentCodec<T> batch)
        {
            batch.DecodeBatch(source, typed, context);
            return;
        }
        for (int index = 0; index < count; index++) _codec.Decode(source.Slice(index * EncodedSize, EncodedSize), ref typed[index], context);
    }
}

internal sealed class BoundHistoricalReader<T> : IBoundPersistenceCodec
    where T : unmanaged, IComponent<T>
{
    private readonly IPersistenceComponentReader<T> _reader;
    internal BoundHistoricalReader(ComponentMetadata metadata, ushort savedVersion, int encodedSize,
        PersistenceSchemaFingerprint fingerprint, IPersistenceComponentReader<T> reader)
    {
        ComponentId = metadata.Id;
        PersistenceId = metadata.PersistenceId;
        CurrentVersion = savedVersion;
        EncodedSize = encodedSize;
        if (EncodedSize <= 0) throw new ArgumentOutOfRangeException(nameof(encodedSize), "Historical encoded size must be positive.");
        Fingerprint = fingerprint;
        _reader = reader;
    }
    public ComponentId ComponentId { get; }
    public uint PersistenceId { get; }
    public ushort CurrentVersion { get; }
    public int EncodedSize { get; }
    public PersistenceSchemaFingerprint Fingerprint { get; }
    public void Encode(ReadOnlySpan<byte> value, Span<byte> destination, PersistenceWriteContext context) =>
        throw new NotSupportedException("Historical readers do not encode current state.");
    public void Decode(ReadOnlySpan<byte> source, Span<byte> value, PersistenceReadContext context)
    {
        T typed = default;
        _reader.Decode(source, ref typed, context);
        MemoryMarshal.Write(value, in typed);
    }
    public void EncodeBatch(ReadOnlySpan<byte> values, int count, Span<byte> destination, PersistenceWriteContext context) =>
        throw new NotSupportedException();
    public void DecodeBatch(ReadOnlySpan<byte> source, int count, Span<byte> values, PersistenceReadContext context)
    {
        int runtimeSize = values.Length / count;
        for (int index = 0; index < count; index++)
            Decode(source.Slice(index * EncodedSize, EncodedSize), values.Slice(index * runtimeSize, runtimeSize), context);
    }
}
