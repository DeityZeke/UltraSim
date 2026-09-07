using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace UltraSim.UI;

/// <summary>Renderer-neutral layout topology supported by UI-0.</summary>
public enum UiLayoutKind : byte
{
    Canvas = 0,
    Vertical = 1,
    Horizontal = 2,
    Overlay = 3,
    Scroll = 4,
}

/// <summary>Child sizing mode for flow layouts.</summary>
public enum UiSizing : byte
{
    Content = 0,
    Fixed = 1,
    Fill = 2,
}

/// <summary>Renderer-neutral flow alignment.</summary>
public enum UiAlignment : byte
{
    Start = 0,
    Center = 1,
    End = 2,
    Stretch = 3,
}

/// <summary>Initial element vocabulary for UI-0.</summary>
public enum UiElementKind : byte
{
    Panel = 0,
    Text = 1,
    Image = 2,
    Button = 3,
    TextInput = 4,
    Toggle = 5,
    List = 6,
}

/// <summary>Value domain declared by one dynamic model slot.</summary>
public enum UiSlotValueKind : byte
{
    Any = 0,
    Text = 1,
    Number = 2,
    Boolean = 3,
    List = 4,
}

/// <summary>Runtime value kinds carried by scalar model fields.</summary>
public enum UiValueKind : byte
{
    Unset = 0,
    Text = 1,
    Number = 2,
    Boolean = 3,
}

/// <summary>Stable authoring identity retained in compiled metadata.</summary>
public readonly struct UiDefinitionKey : IEquatable<UiDefinitionKey>
{
    public const int MaximumLength = 256;

    public UiDefinitionKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        UiText.ValidateUnicode(normalized, nameof(value));
        if (normalized.Length > MaximumLength)
            throw new ArgumentOutOfRangeException(nameof(value), $"A UI definition key is limited to {MaximumLength} characters.");
        Value = normalized;
    }

    public string Value { get; }

    public bool IsValid => !string.IsNullOrEmpty(Value);

    public bool Equals(UiDefinitionKey other) =>
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is UiDefinitionKey other && Equals(other);

    public override int GetHashCode() => Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(UiDefinitionKey left, UiDefinitionKey right) => left.Equals(right);

    public static bool operator !=(UiDefinitionKey left, UiDefinitionKey right) => !left.Equals(right);
}

/// <summary>Prepared exact identity for one compiled definition revision.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 16)]
public readonly struct UiDefinitionHandle : IEquatable<UiDefinitionHandle>
{
    internal UiDefinitionHandle(ulong low, ulong high)
    {
        Low = low;
        High = high;
    }

    public static UiDefinitionHandle Invalid => default;

    public ulong Low { get; }

    public ulong High { get; }

    public bool IsValid => (Low | High) != 0;

    public bool Equals(UiDefinitionHandle other) => Low == other.Low && High == other.High;

    public override bool Equals(object? obj) => obj is UiDefinitionHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Low, High);

    public override string ToString() => IsValid
        ? $"UiDefinition({High:x16}{Low:x16})"
        : "UiDefinition(Invalid)";

    public static bool operator ==(UiDefinitionHandle left, UiDefinitionHandle right) => left.Equals(right);

    public static bool operator !=(UiDefinitionHandle left, UiDefinitionHandle right) => !left.Equals(right);
}

/// <summary>
/// Definition-local authoring reference for one declared model slot.
/// </summary>
/// <remarks>
/// Retain this value from <c>DeclareSlot</c>, use it while authoring elements,
/// then prepare it against the compiled definition. This avoids handwritten
/// binding keys in normal game code. It is not a runtime model slot ID.
/// </remarks>
public readonly struct UiSlotReference : IEquatable<UiSlotReference>
{
    private readonly object? _owner;

    internal UiSlotReference(object owner, ushort ordinal, string key, UiSlotValueKind valueKind)
    {
        _owner = owner;
        Ordinal = ordinal;
        Key = key;
        ValueKind = valueKind;
    }

    internal object? Owner => _owner;

    internal ushort Ordinal { get; }

    public string? Key { get; }

    public UiSlotValueKind ValueKind { get; }

    public bool IsValid => _owner is not null && Key is not null;

    public bool Equals(UiSlotReference other) =>
        ReferenceEquals(_owner, other._owner) && Ordinal == other.Ordinal;

    public override bool Equals(object? obj) => obj is UiSlotReference other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        _owner is null ? 0 : RuntimeHelpers.GetHashCode(_owner), Ordinal);

    public static bool operator ==(UiSlotReference left, UiSlotReference right) => left.Equals(right);

    public static bool operator !=(UiSlotReference left, UiSlotReference right) => !left.Equals(right);
}

/// <summary>
/// Definition-local authoring reference for one declared action contract.
/// </summary>
public readonly struct UiActionReference : IEquatable<UiActionReference>
{
    private readonly object? _owner;

    internal UiActionReference(
        object owner,
        ushort ordinal,
        string key,
        bool acceptsRowContext,
        UiValueKind inputKind)
    {
        _owner = owner;
        Ordinal = ordinal;
        Key = key;
        AcceptsRowContext = acceptsRowContext;
        InputKind = inputKind;
    }

    internal object? Owner => _owner;

    internal ushort Ordinal { get; }

    public string? Key { get; }

    public bool AcceptsRowContext { get; }

    public UiValueKind InputKind { get; }

    public bool IsValid => _owner is not null && Key is not null;

    public bool Equals(UiActionReference other) =>
        ReferenceEquals(_owner, other._owner) && Ordinal == other.Ordinal;

    public override bool Equals(object? obj) => obj is UiActionReference other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        _owner is null ? 0 : RuntimeHelpers.GetHashCode(_owner), Ordinal);

    public static bool operator ==(UiActionReference left, UiActionReference right) => left.Equals(right);

    public static bool operator !=(UiActionReference left, UiActionReference right) => !left.Equals(right);
}

/// <summary>Prepared numeric model slot identity.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 2)]
public readonly struct UiSlotId : IEquatable<UiSlotId>, IComparable<UiSlotId>
{
    internal UiSlotId(ushort value) => Value = value;

    public static UiSlotId Invalid => new(ushort.MaxValue);

    public ushort Value { get; }

    public bool IsValid => Value != ushort.MaxValue;

    public int CompareTo(UiSlotId other) => Value.CompareTo(other.Value);

    public bool Equals(UiSlotId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is UiSlotId other && Equals(other);

    public override int GetHashCode() => Value;

    public override string ToString() => IsValid ? Value.ToString(CultureInfo.InvariantCulture) : "Invalid";

    public static bool operator ==(UiSlotId left, UiSlotId right) => left.Equals(right);

    public static bool operator !=(UiSlotId left, UiSlotId right) => !left.Equals(right);
}

/// <summary>Prepared numeric action identity.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 2)]
public readonly struct UiActionId : IEquatable<UiActionId>, IComparable<UiActionId>
{
    internal UiActionId(ushort value) => Value = value;

    public static UiActionId Invalid => new(ushort.MaxValue);

    public ushort Value { get; }

    public bool IsValid => Value != ushort.MaxValue;

    public int CompareTo(UiActionId other) => Value.CompareTo(other.Value);

    public bool Equals(UiActionId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is UiActionId other && Equals(other);

    public override int GetHashCode() => Value;

    public override string ToString() => IsValid ? Value.ToString(CultureInfo.InvariantCulture) : "Invalid";

    public static bool operator ==(UiActionId left, UiActionId right) => left.Equals(right);

    public static bool operator !=(UiActionId left, UiActionId right) => !left.Equals(right);
}

/// <summary>Prepared numeric layout-node identity.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 4)]
public readonly struct UiLayoutNodeId : IEquatable<UiLayoutNodeId>
{
    internal UiLayoutNodeId(int value) => Value = value;

    public static UiLayoutNodeId Invalid => new(-1);

    public int Value { get; }

    public bool IsValid => Value >= 0;

    public bool Equals(UiLayoutNodeId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is UiLayoutNodeId other && Equals(other);

    public override int GetHashCode() => Value;

    public static bool operator ==(UiLayoutNodeId left, UiLayoutNodeId right) => left.Equals(right);

    public static bool operator !=(UiLayoutNodeId left, UiLayoutNodeId right) => !left.Equals(right);
}

/// <summary>Prepared numeric element identity.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 2)]
public readonly struct UiElementId : IEquatable<UiElementId>
{
    internal UiElementId(ushort value) => Value = value;

    public static UiElementId Invalid => new(ushort.MaxValue);

    public ushort Value { get; }

    public bool IsValid => Value != ushort.MaxValue;

    public bool Equals(UiElementId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is UiElementId other && Equals(other);

    public override int GetHashCode() => Value;

    public static bool operator ==(UiElementId left, UiElementId right) => left.Equals(right);

    public static bool operator !=(UiElementId left, UiElementId right) => !left.Equals(right);
}

/// <summary>Numeric identity for one open view instance.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 8)]
public readonly struct UiViewInstanceId : IEquatable<UiViewInstanceId>
{
    public UiViewInstanceId(ulong value)
    {
        if (value == 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public static UiViewInstanceId Invalid => default;

    public ulong Value { get; }

    public bool IsValid => Value != 0;

    public bool Equals(UiViewInstanceId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is UiViewInstanceId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(UiViewInstanceId left, UiViewInstanceId right) => left.Equals(right);

    public static bool operator !=(UiViewInstanceId left, UiViewInstanceId right) => !left.Equals(right);
}

/// <summary>Numeric identity for a dynamic repeater row.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 8)]
public readonly struct UiRowId : IEquatable<UiRowId>
{
    public UiRowId(ulong value)
    {
        if (value == 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public static UiRowId Invalid => default;

    public ulong Value { get; }

    public bool IsValid => Value != 0;

    public bool Equals(UiRowId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is UiRowId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(UiRowId left, UiRowId right) => left.Equals(right);

    public static bool operator !=(UiRowId left, UiRowId right) => !left.Equals(right);
}

/// <summary>Normalized padding used by flow containers.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct UiPadding : IEquatable<UiPadding>
{
    public UiPadding(float left, float top, float right, float bottom)
    {
        Validate(left, nameof(left));
        Validate(top, nameof(top));
        Validate(right, nameof(right));
        Validate(bottom, nameof(bottom));
        Left = UiFloat.Canonicalize(left);
        Top = UiFloat.Canonicalize(top);
        Right = UiFloat.Canonicalize(right);
        Bottom = UiFloat.Canonicalize(bottom);
    }

    public float Left { get; }
    public float Top { get; }
    public float Right { get; }
    public float Bottom { get; }

    public bool Equals(UiPadding other) =>
        Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

    public override bool Equals(object? obj) => obj is UiPadding other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    private static void Validate(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>Flow-container policy kept deliberately smaller than engine layout APIs.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct UiFlowOptions : IEquatable<UiFlowOptions>
{
    public UiFlowOptions(
        float spacing = 0,
        UiPadding padding = default,
        UiSizing sizing = UiSizing.Content,
        UiAlignment alignment = UiAlignment.Start)
    {
        if (!float.IsFinite(spacing) || spacing < 0)
            throw new ArgumentOutOfRangeException(nameof(spacing));
        if ((uint)sizing > (uint)UiSizing.Fill)
            throw new ArgumentOutOfRangeException(nameof(sizing));
        if (sizing == UiSizing.Fixed)
            throw new ArgumentException(
                "Fixed flow sizing requires explicit dimensions and is not part of UI-0; use Canvas placement.",
                nameof(sizing));
        if ((uint)alignment > (uint)UiAlignment.Stretch)
            throw new ArgumentOutOfRangeException(nameof(alignment));
        Spacing = UiFloat.Canonicalize(spacing);
        Padding = padding;
        Sizing = sizing;
        Alignment = alignment;
    }

    public float Spacing { get; }
    public UiPadding Padding { get; }
    public UiSizing Sizing { get; }
    public UiAlignment Alignment { get; }

    public bool Equals(UiFlowOptions other) =>
        Spacing == other.Spacing && Padding.Equals(other.Padding) &&
        Sizing == other.Sizing && Alignment == other.Alignment;

    public override bool Equals(object? obj) => obj is UiFlowOptions other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Spacing, Padding, Sizing, Alignment);
}

/// <summary>Logical freeform placement for a child of a Canvas.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct UiCanvasPlacement : IEquatable<UiCanvasPlacement>
{
    public UiCanvasPlacement(
        float x,
        float y,
        float width,
        float height,
        float anchorMinX = 0,
        float anchorMinY = 0,
        float anchorMaxX = 0,
        float anchorMaxY = 0,
        float pivotX = 0,
        float pivotY = 0,
        int zOrder = 0)
    {
        ValidateFinite(x, nameof(x));
        ValidateFinite(y, nameof(y));
        ValidatePositive(width, nameof(width));
        ValidatePositive(height, nameof(height));
        ValidateNormalized(anchorMinX, nameof(anchorMinX));
        ValidateNormalized(anchorMinY, nameof(anchorMinY));
        ValidateNormalized(anchorMaxX, nameof(anchorMaxX));
        ValidateNormalized(anchorMaxY, nameof(anchorMaxY));
        ValidateNormalized(pivotX, nameof(pivotX));
        ValidateNormalized(pivotY, nameof(pivotY));
        if (anchorMinX > anchorMaxX)
            throw new ArgumentOutOfRangeException(nameof(anchorMinX), "The minimum X anchor cannot exceed the maximum X anchor.");
        if (anchorMinY > anchorMaxY)
            throw new ArgumentOutOfRangeException(nameof(anchorMinY), "The minimum Y anchor cannot exceed the maximum Y anchor.");
        X = UiFloat.Canonicalize(x);
        Y = UiFloat.Canonicalize(y);
        Width = UiFloat.Canonicalize(width);
        Height = UiFloat.Canonicalize(height);
        AnchorMinX = UiFloat.Canonicalize(anchorMinX);
        AnchorMinY = UiFloat.Canonicalize(anchorMinY);
        AnchorMaxX = UiFloat.Canonicalize(anchorMaxX);
        AnchorMaxY = UiFloat.Canonicalize(anchorMaxY);
        PivotX = UiFloat.Canonicalize(pivotX);
        PivotY = UiFloat.Canonicalize(pivotY);
        ZOrder = zOrder;
    }

    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
    public float AnchorMinX { get; }
    public float AnchorMinY { get; }
    public float AnchorMaxX { get; }
    public float AnchorMaxY { get; }
    public float PivotX { get; }
    public float PivotY { get; }
    public int ZOrder { get; }

    public bool Equals(UiCanvasPlacement other) =>
        X == other.X && Y == other.Y && Width == other.Width && Height == other.Height &&
        AnchorMinX == other.AnchorMinX && AnchorMinY == other.AnchorMinY &&
        AnchorMaxX == other.AnchorMaxX && AnchorMaxY == other.AnchorMaxY &&
        PivotX == other.PivotX && PivotY == other.PivotY && ZOrder == other.ZOrder;

    public override bool Equals(object? obj) => obj is UiCanvasPlacement other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(X);
        hash.Add(Y);
        hash.Add(Width);
        hash.Add(Height);
        hash.Add(AnchorMinX);
        hash.Add(AnchorMinY);
        hash.Add(AnchorMaxX);
        hash.Add(AnchorMaxY);
        hash.Add(PivotX);
        hash.Add(PivotY);
        hash.Add(ZOrder);
        return hash.ToHashCode();
    }

    private static void ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidatePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateNormalized(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>Placement policy relative to a containing layout.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct UiChildLayout : IEquatable<UiChildLayout>
{
    private UiChildLayout(UiSizing sizing, UiAlignment alignment, bool hasCanvas, UiCanvasPlacement canvas)
    {
        Sizing = sizing;
        Alignment = alignment;
        HasCanvasPlacement = hasCanvas;
        CanvasPlacement = canvas;
    }

    public UiSizing Sizing { get; }
    public UiAlignment Alignment { get; }
    public bool HasCanvasPlacement { get; }
    public UiCanvasPlacement CanvasPlacement { get; }

    public static UiChildLayout Flow(
        UiSizing sizing = UiSizing.Content,
        UiAlignment alignment = UiAlignment.Start)
    {
        if ((uint)sizing > (uint)UiSizing.Fill)
            throw new ArgumentOutOfRangeException(nameof(sizing));
        if (sizing == UiSizing.Fixed)
            throw new ArgumentException(
                "Fixed flow sizing requires explicit dimensions and is not part of UI-0; use Canvas placement.",
                nameof(sizing));
        if ((uint)alignment > (uint)UiAlignment.Stretch)
            throw new ArgumentOutOfRangeException(nameof(alignment));
        return new UiChildLayout(sizing, alignment, false, default);
    }

    public static UiChildLayout Canvas(UiCanvasPlacement placement) =>
        new(UiSizing.Fixed, UiAlignment.Start, true, placement);

    public bool Equals(UiChildLayout other) =>
        Sizing == other.Sizing && Alignment == other.Alignment &&
        HasCanvasPlacement == other.HasCanvasPlacement &&
        (!HasCanvasPlacement || CanvasPlacement.Equals(other.CanvasPlacement));

    public override bool Equals(object? obj) => obj is UiChildLayout other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Sizing, Alignment, HasCanvasPlacement, CanvasPlacement);
}

/// <summary>Logical dimensions of a Canvas root.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct UiCanvasOptions : IEquatable<UiCanvasOptions>
{
    public UiCanvasOptions(float width, float height)
    {
        if (!float.IsFinite(width) || width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!float.IsFinite(height) || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        Width = UiFloat.Canonicalize(width);
        Height = UiFloat.Canonicalize(height);
    }

    public static UiCanvasOptions Default => new(800, 600);

    public float Width { get; }
    public float Height { get; }

    public bool Equals(UiCanvasOptions other) => Width == other.Width && Height == other.Height;

    public override bool Equals(object? obj) => obj is UiCanvasOptions other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Width, Height);
}

/// <summary>Immutable scalar model value.</summary>
public readonly struct UiValue : IEquatable<UiValue>
{
    public const int MaximumTextCharacters = 1_048_576;
    private UiValue(UiValueKind kind, string? text, double number, bool boolean)
    {
        Kind = kind;
        Text = text;
        Number = number;
        Boolean = boolean;
    }

    public static UiValue Unset => default;

    public UiValueKind Kind { get; }
    public string? Text { get; }
    public double Number { get; }
    public bool Boolean { get; }

    public static UiValue FromText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaximumTextCharacters)
            throw new ArgumentOutOfRangeException(nameof(value));
        UiText.ValidateUnicode(value, nameof(value));
        return new UiValue(UiValueKind.Text, value, 0, false);
    }

    public static UiValue FromNumber(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        return new UiValue(UiValueKind.Number, null, value, false);
    }

    public static UiValue FromBoolean(bool value) => new(UiValueKind.Boolean, null, 0, value);

    public bool Equals(UiValue other) =>
        Kind == other.Kind &&
        string.Equals(Text, other.Text, StringComparison.Ordinal) &&
        Number == other.Number && Boolean == other.Boolean;

    public override bool Equals(object? obj) => obj is UiValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Text, Number, Boolean);
}

/// <summary>Immutable dynamic row for a List/Repeater model slot.</summary>
public readonly struct UiListRow : IEquatable<UiListRow>
{
    public const int MaximumValues = 256;
    private readonly UiValue[] _values;

    public UiListRow(UiRowId id, ReadOnlySpan<UiValue> values)
    {
        if (!id.IsValid)
            throw new ArgumentException("A list row requires a valid row ID.", nameof(id));
        if (values.Length > MaximumValues)
            throw new ArgumentOutOfRangeException(nameof(values));
        _values = values.ToArray();
        Id = id;
    }

    public UiRowId Id { get; }

    public ReadOnlySpan<UiValue> Values => _values ?? ReadOnlySpan<UiValue>.Empty;

    // Rows are immutable: the backing cell array is private and exposed only
    // through ReadOnlySpan, so snapshots can structurally share it safely.
    public UiListRow Clone() => this;

    public bool Equals(UiListRow other)
    {
        if (Id != other.Id || Values.Length != other.Values.Length)
            return false;
        for (int index = 0; index < Values.Length; index++)
        {
            if (!Values[index].Equals(other.Values[index]))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is UiListRow other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, Values.Length);
}

/// <summary>Dynamic action availability/reason state.</summary>
public readonly struct UiActionState : IEquatable<UiActionState>
{
    public UiActionState(bool enabled, string? unavailableReason = null)
    {
        if (enabled && !string.IsNullOrEmpty(unavailableReason))
            throw new ArgumentException("An enabled action cannot have an unavailable reason.", nameof(unavailableReason));
        if (unavailableReason is not null)
        {
            if (unavailableReason.Length > UiValue.MaximumTextCharacters)
                throw new ArgumentOutOfRangeException(nameof(unavailableReason));
            UiText.ValidateUnicode(unavailableReason, nameof(unavailableReason));
        }
        Enabled = enabled;
        UnavailableReason = unavailableReason;
    }

    public static UiActionState Default => new(true);

    public bool Enabled { get; }

    public string? UnavailableReason { get; }

    public bool Equals(UiActionState other) =>
        Enabled == other.Enabled &&
        string.Equals(UnavailableReason, other.UnavailableReason, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is UiActionState other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Enabled, UnavailableReason);
}

/// <summary>Transient user intent routed through a prepared action ID.</summary>
public readonly struct UiActionRequest
{
    public UiActionRequest(
        UiViewInstanceId instance,
        UiDefinitionHandle definition,
        UiActionId action,
        UiRowId row = default,
        UiValue input = default,
        ulong clientSequence = 0,
        UiElementId? source = null)
    {
        Instance = instance;
        Definition = definition;
        Action = action;
        Row = row;
        Input = input;
        ClientSequence = clientSequence;
        Source = source ?? UiElementId.Invalid;
    }

    public UiViewInstanceId Instance { get; }
    public UiDefinitionHandle Definition { get; }
    public UiActionId Action { get; }
    public UiRowId Row { get; }
    public UiValue Input { get; }
    public ulong ClientSequence { get; }
    public UiElementId Source { get; }
}

internal static class UiText
{
    internal static void ValidateUnicode(string value, string parameterName)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (char.IsHighSurrogate(current))
            {
                if (++index >= value.Length || !char.IsLowSurrogate(value[index]))
                    throw new ArgumentException("Text contains an unpaired UTF-16 surrogate.", parameterName);
            }
            else if (char.IsLowSurrogate(current))
            {
                throw new ArgumentException("Text contains an unpaired UTF-16 surrogate.", parameterName);
            }
        }
    }
}

/// <summary>Immutable validation/allocation limits for untrusted definitions/models.</summary>
public readonly struct UiCompileLimits : IEquatable<UiCompileLimits>
{
    public UiCompileLimits(
        int maximumDepth = 32,
        int maximumLayoutNodes = 256,
        int maximumElements = 512,
        int maximumSlots = 256,
        int maximumActions = 256,
        int maximumTextCharacters = 16_384,
        int maximumRowsPerList = 1_024)
    {
        if (maximumDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        if (maximumLayoutNodes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumLayoutNodes));
        if (maximumElements <= 0 || maximumElements > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maximumElements));
        if (maximumSlots <= 0 || maximumSlots > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(maximumSlots));
        if (maximumActions <= 0 || maximumActions > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(maximumActions));
        if (maximumTextCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(maximumTextCharacters));
        if (maximumRowsPerList <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRowsPerList));
        MaximumDepth = maximumDepth;
        MaximumLayoutNodes = maximumLayoutNodes;
        MaximumElements = maximumElements;
        MaximumSlots = maximumSlots;
        MaximumActions = maximumActions;
        MaximumTextCharacters = maximumTextCharacters;
        MaximumRowsPerList = maximumRowsPerList;
    }

    public static UiCompileLimits Default => new(
        maximumDepth: 32,
        maximumLayoutNodes: 256,
        maximumElements: 512,
        maximumSlots: 256,
        maximumActions: 256,
        maximumTextCharacters: 16_384,
        maximumRowsPerList: 1_024);
    public int MaximumDepth { get; }
    public int MaximumLayoutNodes { get; }
    public int MaximumElements { get; }
    public int MaximumSlots { get; }
    public int MaximumActions { get; }
    public int MaximumTextCharacters { get; }
    public int MaximumRowsPerList { get; }

    public bool Equals(UiCompileLimits other) =>
        MaximumDepth == other.MaximumDepth && MaximumLayoutNodes == other.MaximumLayoutNodes &&
        MaximumElements == other.MaximumElements && MaximumSlots == other.MaximumSlots &&
        MaximumActions == other.MaximumActions && MaximumTextCharacters == other.MaximumTextCharacters &&
        MaximumRowsPerList == other.MaximumRowsPerList;

    public override bool Equals(object? obj) => obj is UiCompileLimits other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        MaximumDepth, MaximumLayoutNodes, MaximumElements, MaximumSlots,
        MaximumActions, MaximumTextCharacters, MaximumRowsPerList);
}

/// <summary>Canonical 128-bit identity of immutable compiled definition structure.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 16)]
public readonly struct UiDefinitionHash : IEquatable<UiDefinitionHash>
{
    internal UiDefinitionHash(ulong low, ulong high)
    {
        Low = low;
        High = high;
    }

    public ulong Low { get; }
    public ulong High { get; }

    public bool IsValid => (Low | High) != 0;

    public bool Equals(UiDefinitionHash other) => Low == other.Low && High == other.High;

    public override bool Equals(object? obj) => obj is UiDefinitionHash other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Low, High);

    public override string ToString() => string.Concat(
        High.ToString("x16", CultureInfo.InvariantCulture),
        Low.ToString("x16", CultureInfo.InvariantCulture));

    public static bool operator ==(UiDefinitionHash left, UiDefinitionHash right) => left.Equals(right);

    public static bool operator !=(UiDefinitionHash left, UiDefinitionHash right) => !left.Equals(right);

    internal static UiDefinitionHash FromDigest(ReadOnlySpan<byte> digest) => new(
        BinaryPrimitives.ReadUInt64LittleEndian(digest),
        BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]));
}

/// <summary>Exact typed identity of one immutable compiled UI definition.</summary>
public readonly record struct UiDefinitionIdentity(
    UiDefinitionKey Key,
    ushort Version,
    UiDefinitionHash Hash)
{
    public bool IsValid => Key.IsValid && Version != 0 && Hash.IsValid;

    public static UiDefinitionIdentity From(CompiledUiDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new UiDefinitionIdentity(definition.Key, definition.Version, definition.Hash);
    }
}

internal static class UiFloat
{
    internal static float Canonicalize(float value) => value == 0 ? 0 : value;
}
