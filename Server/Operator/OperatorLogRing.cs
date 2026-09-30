namespace UltraSim.Server.Operator;

public enum OperatorLogSeverity : byte { Information, Warning, Error }
public enum OperatorLogCategory : byte { Lifecycle, Persistence, Backup, Sessions }

public readonly record struct OperatorLogEntry(ulong Sequence, DateTimeOffset Time,
    OperatorLogSeverity Severity, OperatorLogCategory Category, string Message);

/// <summary>Bounded owner-local operational events. Audit of action requests remains separate.</summary>
public sealed class OperatorLogRing
{
    public const int Capacity = 256;
    private readonly OperatorLogEntry[] _entries = new OperatorLogEntry[Capacity];
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private int _next;
    private int _count;
    private ulong _sequence;
    private ulong _dropped;

    public ulong Sequence { get { EnsureOwner(); return _sequence; } }
    public int Count { get { EnsureOwner(); return _count; } }
    public ulong Dropped { get { EnsureOwner(); return _dropped; } }

    public void Write(OperatorLogSeverity severity, OperatorLogCategory category,
        string message, DateTimeOffset? time = null)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(message);
        if (_sequence == ulong.MaxValue)
        {
            if (_dropped != ulong.MaxValue) _dropped++;
            return;
        }
        string bounded = message.Length <= 256 ? message : message[..256];
        _entries[_next] = new(++_sequence, time ?? DateTimeOffset.UtcNow,
            severity, category, bounded);
        _next = (_next + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    public int CopyTo(Span<OperatorLogEntry> destination)
    {
        EnsureOwner();
        if (destination.Length < _count) return -1;
        int first = (_next - _count + Capacity) % Capacity;
        for (int index = 0; index < _count; index++)
            destination[index] = _entries[(first + index) % Capacity];
        return _count;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Operator log is owner-thread state.");
    }
}
