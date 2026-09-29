using System.Buffers.Binary;
using UltraSim.ECS;
using UltraSim.Server.Persistence;
using UltraSim.Framework.Persistence;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Persistence;

/// <summary>Durable save-set participant for authoritative, host-independent world time.</summary>
public sealed class WorldClockSaveSetParticipant : ISaveSetParticipant
{
    private const uint Magic = 0x4B434C55; // "ULCK"
    private const ushort LegacyFrequencyRelativeFormatVersion = 1;
    private const ushort FormatVersion = 2;
    private const int CurrentArtifactBytes = 38;
    private const int LegacyArtifactBytes = 42;
    private readonly PersistentWorldClockState? _source;

    public WorldClockSaveSetParticipant(PersistentWorldClockState source, string key = "clock")
    {
        _source = source;
        Key = ValidateKey(key);
    }

    /// <summary>Creates a restore-only clock participant.</summary>
    public WorldClockSaveSetParticipant(string key = "clock") => Key = ValidateKey(key);

    public string Key { get; }
    public long MaximumArtifactBytes => 64;

    public ICapturedSaveSetParticipant Capture(in SaveSetContext context)
    {
        if (context.Sequence == 0)
            throw new ArgumentOutOfRangeException(nameof(context));
        PersistentWorldClockState state = _source ??
            throw new InvalidOperationException("A restore-only clock participant cannot capture a save.");
        return new Captured(Key, context.Sequence, state);
    }

    public IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context)
    {
        if (context.Sequence == 0)
            throw new ArgumentOutOfRangeException(nameof(context));
        ReadOnlySpan<byte> bytes = artifact.Span;
        if (bytes.Length < 6 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic)
        {
            throw new PersistenceCompatibilityException("The world-clock artifact format is unsupported.");
        }
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        if ((version == FormatVersion && bytes.Length != CurrentArtifactBytes) ||
            (version == LegacyFrequencyRelativeFormatVersion && bytes.Length != LegacyArtifactBytes) ||
            version is not (FormatVersion or LegacyFrequencyRelativeFormatVersion))
        {
            throw new PersistenceCompatibilityException("The world-clock artifact format is unsupported.");
        }
        if (BinaryPrimitives.ReadUInt64LittleEndian(bytes[6..]) != context.Sequence)
            throw new PersistenceValidationException("World-clock artifact sequence disagrees with the save set.");

        PersistentWorldClockState state;
        try
        {
            state = version == FormatVersion
                ? new PersistentWorldClockState(
                    BinaryPrimitives.ReadInt32LittleEndian(bytes[14..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(bytes[18..]),
                    BinaryPrimitives.ReadInt64LittleEndian(bytes[26..]),
                    BinaryPrimitives.ReadInt32LittleEndian(bytes[34..]))
                : ReadLegacyState(bytes);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new PersistenceValidationException(
                $"The world-clock artifact contains invalid state: {exception.Message}");
        }
        return new PreparedWorldClockParticipant(Key, state);
    }

    private static PersistentWorldClockState ReadLegacyState(ReadOnlySpan<byte> bytes)
    {
        int frequency = BinaryPrimitives.ReadInt32LittleEndian(bytes[14..]);
        int remainder = BinaryPrimitives.ReadInt32LittleEndian(bytes[38..]);
        if (frequency <= 0 || remainder < 0 || remainder >= frequency)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Legacy world-clock fraction is invalid.");

        long scaledFraction = checked((long)remainder *
            WorldClockOptions.WorldTimeFractionQuantaPerMicrosecond);
        if (scaledFraction % frequency != 0)
        {
            throw new PersistenceCompatibilityException(
                "The legacy world-clock fractional remainder cannot be represented exactly by the current fixed time base.");
        }

        return new PersistentWorldClockState(
            BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[22..]),
            BinaryPrimitives.ReadInt64LittleEndian(bytes[30..]),
            checked((int)(scaledFraction / frequency)));
    }

    private static string ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("A save-set participant key is required.", nameof(key));
        return key;
    }

    private sealed class Captured : ICapturedSaveSetParticipant
    {
        private readonly ulong _sequence;
        private readonly PersistentWorldClockState _state;
        private bool _written;

        internal Captured(string key, ulong sequence, PersistentWorldClockState state)
        {
            Key = key;
            _sequence = sequence;
            _state = state;
        }

        public string Key { get; }

        public void WriteArtifact(Stream destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (_written)
                throw new InvalidOperationException("The world-clock capture was already consumed.");
            _written = true;
            Span<byte> bytes = stackalloc byte[CurrentArtifactBytes];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[4..], FormatVersion);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes[6..], _sequence);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[14..], _state.WorldTimeRate);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes[18..], _state.SimulationTick);
            BinaryPrimitives.WriteInt64LittleEndian(bytes[26..], _state.WorldTimeMicroseconds);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[34..], _state.WorldTimeFractionQuanta);
            destination.Write(bytes);
        }

        public void Dispose() => _written = true;
    }
}

/// <summary>Validated staged authoritative clock state.</summary>
public sealed class PreparedWorldClockParticipant : IPreparedLoadParticipant
{
    internal PreparedWorldClockParticipant(string key, PersistentWorldClockState state)
    {
        Key = key;
        State = state;
    }

    public string Key { get; }
    public PersistentWorldClockState State { get; }
    public void Resolve(IPersistentReferenceResolver resolver) => ArgumentNullException.ThrowIfNull(resolver);
    public void Validate() { }
    public void Dispose() { }
}
