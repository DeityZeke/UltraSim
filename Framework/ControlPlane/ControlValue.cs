using System.Collections.ObjectModel;

namespace UltraSim.Framework.ControlPlane;

public enum ControlValueKind : byte { Invalid, Bool, Int64, UInt64, Float64, Text, Option }

/// <summary>Closed immutable scalar; no object, JSON or reflection escape hatch.</summary>
public readonly record struct ControlValue
{
    private ControlValue(ControlValueKind kind, long signed = 0, ulong unsigned = 0,
        double number = 0, string? text = null)
    { Kind = kind; Signed = signed; Unsigned = unsigned; Number = number; String = text; }
    public ControlValueKind Kind { get; }
    private long Signed { get; }
    private ulong Unsigned { get; }
    private double Number { get; }
    private string? String { get; }
    public bool IsValid => Kind != ControlValueKind.Invalid;
    public static ControlValue Bool(bool value) => new(ControlValueKind.Bool, signed: value ? 1 : 0);
    public static ControlValue Int64(long value) => new(ControlValueKind.Int64, signed: value);
    public static ControlValue UInt64(ulong value) => new(ControlValueKind.UInt64, unsigned: value);
    public static ControlValue Float64(double value) => double.IsFinite(value)
        ? new(ControlValueKind.Float64, number: value == 0 ? 0 : value)
        : throw new ArgumentOutOfRangeException(nameof(value));
    public static ControlValue Text(string value) =>
        new(ControlValueKind.Text, text: ControlLimits.Bounded(value, ControlLimits.Text));
    public static ControlValue Option(ControlOptionId value) => value.IsValid
        ? new(ControlValueKind.Option, text: value.Value) : throw new ArgumentException("Invalid option.");
    public bool AsBool() => Kind == ControlValueKind.Bool ? Signed != 0 : throw WrongKind();
    public long AsInt64() => Kind == ControlValueKind.Int64 ? Signed : throw WrongKind();
    public ulong AsUInt64() => Kind == ControlValueKind.UInt64 ? Unsigned : throw WrongKind();
    public double AsFloat64() => Kind == ControlValueKind.Float64 ? Number : throw WrongKind();
    public string AsText() => Kind == ControlValueKind.Text ? String! : throw WrongKind();
    public ControlOptionId AsOption() => Kind == ControlValueKind.Option ? new(String!) : throw WrongKind();
    private static InvalidOperationException WrongKind() => new("ControlValue kind mismatch.");
}

/// <summary>Exact typed bounds: integer validation does not round through double.</summary>
public sealed class ControlValueSchema
{
    public ControlValueSchema(ControlValueKind kind, ControlValue? minimum = null,
        ControlValue? maximum = null, int maximumText = ControlLimits.Text,
        IEnumerable<ControlOptionId>? options = null)
    {
        if (kind is < ControlValueKind.Bool or > ControlValueKind.Option ||
            maximumText < 0 || maximumText > ControlLimits.Text)
            throw new ArgumentException("Invalid scalar schema.");
        Kind = kind; Minimum = minimum; Maximum = maximum; MaximumText = maximumText;
        Options = Array.AsReadOnly(ControlLimits.Copy(options ?? [], ControlLimits.Options));
        if (minimum.HasValue != maximum.HasValue ||
            minimum is { } min && (min.Kind != kind || maximum!.Value.Kind != kind ||
                kind is not (ControlValueKind.Int64 or ControlValueKind.UInt64 or ControlValueKind.Float64) ||
                Compare(min, maximum.Value) > 0) ||
            kind == ControlValueKind.Option && (Options.Count == 0 ||
                Options.Any(o => !o.IsValid) || Options.Distinct().Count() != Options.Count) ||
            kind != ControlValueKind.Option && Options.Count != 0)
            throw new ArgumentException("Invalid bounds/options.");
    }
    public ControlValueKind Kind { get; }
    public ControlValue? Minimum { get; }
    public ControlValue? Maximum { get; }
    public int MaximumText { get; }
    public ReadOnlyCollection<ControlOptionId> Options { get; }
    public bool Accepts(ControlValue value) => value.Kind == Kind &&
        (Minimum is not { } min || Compare(value, min) >= 0 && Compare(value, Maximum!.Value) <= 0) &&
        (Kind != ControlValueKind.Text || value.AsText().Length <= MaximumText) &&
        (Kind != ControlValueKind.Option || Options.Contains(value.AsOption()));
    private static int Compare(ControlValue a, ControlValue b) => a.Kind switch
    {
        ControlValueKind.Int64 => a.AsInt64().CompareTo(b.AsInt64()),
        ControlValueKind.UInt64 => a.AsUInt64().CompareTo(b.AsUInt64()),
        ControlValueKind.Float64 => a.AsFloat64().CompareTo(b.AsFloat64()),
        _ => throw new ArgumentException("Non-numeric bound.")
    };
}
