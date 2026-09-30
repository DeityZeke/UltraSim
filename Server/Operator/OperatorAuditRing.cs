using UltraSim.Framework.ControlPlane;

namespace UltraSim.Server.Operator;

public readonly record struct OperatorAuditRow(
    ulong Sequence, DateTimeOffset Time, string Principal, string Context,
    string Provider, string Action, string Arguments, ControlResultStatus Status,
    ControlResultCode Code);

/// <summary>Owner-thread, bounded management audit. Text arguments are never retained.</summary>
public sealed class OperatorAuditRing : IControlAuditSink
{
    public const int Capacity = 256;
    private readonly OperatorAuditRow[] _rows = new OperatorAuditRow[Capacity];
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private ulong _sequence;
    private int _count;

    public int Count { get { EnsureOwner(); return _count; } }
    public ulong Sequence { get { EnsureOwner(); return _sequence; } }

    public void Observe(ControlAuditObservation observation)
    {
        EnsureOwner();
        if (_sequence == ulong.MaxValue)
            throw new InvalidOperationException("Operator audit sequence exhausted.");
        ulong sequence = ++_sequence;
        string arguments = string.Join(',', observation.Arguments.Select(static value => value.Kind switch
        {
            ControlValueKind.UInt64 => value.AsUInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ControlValueKind.Int64 => value.AsInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ControlValueKind.Bool => value.AsBool() ? "true" : "false",
            ControlValueKind.Option => value.AsOption().Value!,
            _ => "[redacted]"
        }));
        if (arguments.Length > 256) arguments = arguments[..256];
        _rows[(int)((sequence - 1) % Capacity)] = new(sequence,
            observation.Context.Timestamp ?? DateTimeOffset.UtcNow,
            observation.Context.Principal.Identity,
            observation.Context.Principal.Context,
            observation.Provider.Value!, observation.Action?.Value ?? "set",
            arguments, observation.Result.Outcome.Status, observation.Result.Outcome.Code);
        if (_count < Capacity) _count++;
    }

    public int CopyTo(Span<OperatorAuditRow> destination)
    {
        EnsureOwner();
        if (destination.Length < _count) return -1;
        ulong first = _sequence - (ulong)_count;
        for (int i = 0; i < _count; i++)
            destination[i] = _rows[(int)((first + (ulong)i) % Capacity)];
        return _count;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Operator audit owner thread required.");
    }
}
