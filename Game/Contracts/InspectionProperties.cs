using System.Globalization;
using System.Text;

namespace UltraSim.Game.Inspection;

/// <summary>Stable semantic identity of one recipient-visible inspection fact.</summary>
public readonly record struct InspectionPropertyId(ushort Value) : IComparable<InspectionPropertyId>
{
    public bool IsValid => Value != 0;
    public int CompareTo(InspectionPropertyId other) => Value.CompareTo(other.Value);
    public override string ToString() => IsValid ? $"InspectionProperty({Value})" : "InspectionProperty(Invalid)";
}

/// <summary>Stable presentation category identity. Category order is not object state.</summary>
public readonly record struct InspectionCategoryId(ushort Value) : IComparable<InspectionCategoryId>
{
    public bool IsValid => Value != 0;
    public int CompareTo(InspectionCategoryId other) => Value.CompareTo(other.Value);
    public override string ToString() => IsValid ? $"InspectionCategory({Value})" : "InspectionCategory(Invalid)";
}

/// <summary>Finite typed value grammar for the first bounded inspection-property projection.</summary>
public enum InspectionPropertyValueKind : byte
{
    Integer = 1,
    IntegerRange = 2,
    Token = 3,
    Fraction = 4,
    MassGrams = 5,
}

/// <summary>
/// One typed authoritative value. Labels, ordering, units, and rendered punctuation are deliberately
/// absent; those belong to an inspection presentation catalog/profile.
/// </summary>
public readonly record struct InspectionPropertyValue(
    InspectionPropertyValueKind Kind,
    long First,
    long Second,
    string? Token)
{
    public const int MaximumTokenUtf8Bytes = 32;

    public static InspectionPropertyValue Integer(long value) =>
        new(InspectionPropertyValueKind.Integer, value, 0, null);

    public static InspectionPropertyValue IntegerRange(long minimum, long maximum) =>
        new(InspectionPropertyValueKind.IntegerRange, minimum, maximum, null);

    public static InspectionPropertyValue Fraction(long current, long maximum) =>
        new(InspectionPropertyValueKind.Fraction, current, maximum, null);

    public static InspectionPropertyValue MassGrams(long grams) =>
        new(InspectionPropertyValueKind.MassGrams, grams, 0, null);

    public static bool TryToken(string? token, out InspectionPropertyValue value)
    {
        value = default;
        if (!IsCanonicalToken(token)) return false;
        value = new InspectionPropertyValue(InspectionPropertyValueKind.Token, 0, 0, token);
        return true;
    }

    public bool IsCanonical => Kind switch
    {
        InspectionPropertyValueKind.Integer => Second == 0 && Token is null,
        InspectionPropertyValueKind.IntegerRange => First <= Second && Token is null,
        InspectionPropertyValueKind.Token => First == 0 && Second == 0 && IsCanonicalToken(Token),
        InspectionPropertyValueKind.Fraction => First >= 0 && Second > 0 && First <= Second && Token is null,
        InspectionPropertyValueKind.MassGrams => First >= 0 && Second == 0 && Token is null,
        _ => false,
    };

    public static bool IsCanonicalToken(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumTokenUtf8Bytes) return false;
        int bytes = 0;
        foreach (char character in value)
        {
            bool admitted = character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.';
            if (!admitted) return false;
            bytes++;
        }
        return bytes <= MaximumTokenUtf8Bytes;
    }
}

/// <summary>One property identity paired with its authoritative recipient-visible value.</summary>
public readonly record struct InspectionPropertyFact(
    InspectionPropertyId Id,
    InspectionPropertyValue Value)
{
    public bool IsCanonical => Id.IsValid && Value.IsCanonical;
}

/// <summary>
/// Immutable bounded canonical property set. Facts are sorted by stable identity so contributor
/// order cannot leak into serialization, equality, or presentation.
/// </summary>
public sealed class InspectionPropertySet : IEquatable<InspectionPropertySet>
{
    public const int MaximumProperties = 16;
    private readonly InspectionPropertyFact[] _facts;

    private InspectionPropertySet(InspectionPropertyFact[] facts) => _facts = facts;

    public int Count => _facts.Length;
    public ReadOnlySpan<InspectionPropertyFact> Facts => _facts;
    public InspectionPropertyFact this[int index] => _facts[index];

    public static bool TryCreate(
        ReadOnlySpan<InspectionPropertyFact> facts,
        out InspectionPropertySet? properties)
    {
        properties = null;
        if (facts.IsEmpty || facts.Length > MaximumProperties) return false;
        var canonical = facts.ToArray();
        foreach (ref readonly InspectionPropertyFact fact in canonical.AsSpan())
            if (!fact.IsCanonical) return false;
        Array.Sort(canonical, static (left, right) => left.Id.CompareTo(right.Id));
        for (int index = 1; index < canonical.Length; index++)
            if (canonical[index - 1].Id == canonical[index].Id) return false;
        properties = new InspectionPropertySet(canonical);
        return true;
    }

    public bool Equals(InspectionPropertySet? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || _facts.Length != other._facts.Length) return false;
        return _facts.AsSpan().SequenceEqual(other._facts);
    }

    public override bool Equals(object? obj) => obj is InspectionPropertySet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (ref readonly InspectionPropertyFact fact in _facts.AsSpan()) hash.Add(fact);
        return hash.ToHashCode();
    }
}

/// <summary>Known stock inspection categories. Numeric identity does not imply display order.</summary>
public static class InspectionCategoryIds
{
    public static readonly InspectionCategoryId Stats = new(1);
    public static readonly InspectionCategoryId Attributes = new(2);
    public static readonly InspectionCategoryId Durability = new(3);
    public static readonly InspectionCategoryId Weight = new(4);
}

/// <summary>Known stock inspection properties. Numeric identity does not imply display order.</summary>
public static class InspectionPropertyIds
{
    public static readonly InspectionPropertyId Damage = new(1);
    public static readonly InspectionPropertyId Element = new(2);
    public static readonly InspectionPropertyId Quality = new(3);
    public static readonly InspectionPropertyId Durability = new(4);
    public static readonly InspectionPropertyId Weight = new(5);
}

public enum InspectionPropertyLabelMode : byte
{
    LabelAndValue = 1,
    ValueOnly = 2,
}

/// <summary>Client-facing definition of one known semantic property.</summary>
public readonly record struct InspectionPropertyDefinition(
    InspectionPropertyId Id,
    InspectionCategoryId Category,
    InspectionPropertyValueKind ValueKind,
    int DefaultOrder,
    string Label,
    InspectionPropertyLabelMode LabelMode = InspectionPropertyLabelMode.LabelAndValue)
{
    public bool IsValid => Id.IsValid && Category.IsValid && DefaultOrder >= 0 &&
        !string.IsNullOrWhiteSpace(Label) &&
        ValueKind is >= InspectionPropertyValueKind.Integer and <= InspectionPropertyValueKind.MassGrams &&
        LabelMode is InspectionPropertyLabelMode.LabelAndValue or InspectionPropertyLabelMode.ValueOnly;
}

public readonly record struct InspectionCategoryDefinition(
    InspectionCategoryId Id,
    int DefaultOrder,
    string Label)
{
    public bool IsValid => Id.IsValid && DefaultOrder >= 0 && !string.IsNullOrWhiteSpace(Label);
}

/// <summary>Immutable known-property presentation vocabulary. Unknown facts are not rendered.</summary>
public sealed class InspectionPropertyCatalog
{
    private readonly InspectionCategoryDefinition[] _categories;
    private readonly InspectionPropertyDefinition[] _properties;

    public InspectionPropertyCatalog(
        ReadOnlySpan<InspectionCategoryDefinition> categories,
        ReadOnlySpan<InspectionPropertyDefinition> properties)
    {
        if (categories.IsEmpty || properties.IsEmpty)
            throw new ArgumentException("Inspection presentation catalogs cannot be empty.");
        _categories = categories.ToArray();
        _properties = properties.ToArray();
        Array.Sort(_categories, static (left, right) => left.Id.CompareTo(right.Id));
        Array.Sort(_properties, static (left, right) => left.Id.CompareTo(right.Id));
        for (int index = 0; index < _categories.Length; index++)
        {
            if (!_categories[index].IsValid ||
                index != 0 && _categories[index - 1].Id == _categories[index].Id)
                throw new ArgumentException("Inspection categories must be valid and unique.", nameof(categories));
        }
        for (int index = 0; index < _properties.Length; index++)
        {
            if (!_properties[index].IsValid ||
                index != 0 && _properties[index - 1].Id == _properties[index].Id ||
                !TryGetCategory(_properties[index].Category, out _))
                throw new ArgumentException("Inspection properties must be valid, unique, and categorized.", nameof(properties));
        }
    }

    public bool TryGetCategory(
        InspectionCategoryId id,
        out InspectionCategoryDefinition definition)
    {
        foreach (ref readonly InspectionCategoryDefinition candidate in _categories.AsSpan())
        {
            if (candidate.Id != id) continue;
            definition = candidate;
            return true;
        }
        definition = default;
        return false;
    }

    public bool TryGetProperty(
        InspectionPropertyId id,
        out InspectionPropertyDefinition definition)
    {
        foreach (ref readonly InspectionPropertyDefinition candidate in _properties.AsSpan())
        {
            if (candidate.Id != id) continue;
            definition = candidate;
            return true;
        }
        definition = default;
        return false;
    }
}

public readonly record struct InspectionCategoryOrder(InspectionCategoryId Category, int Order)
{
    public bool IsValid => Category.IsValid && Order >= 0;
}

public readonly record struct InspectionPropertyOrder(InspectionPropertyId Property, int Order)
{
    public bool IsValid => Property.IsValid && Order >= 0;
}

/// <summary>
/// Client-facing ordering policy. Omitted known entries deterministically fall back to their
/// catalog defaults, so adding a definition does not require rewriting every profile.
/// </summary>
public sealed class InspectionPresentationProfile
{
    private readonly InspectionCategoryOrder[] _categories;
    private readonly InspectionPropertyOrder[] _properties;

    public InspectionPresentationProfile(
        ReadOnlySpan<InspectionCategoryOrder> categories,
        ReadOnlySpan<InspectionPropertyOrder> properties)
    {
        _categories = categories.ToArray();
        _properties = properties.ToArray();
        ValidateUnique(_categories);
        ValidateUnique(_properties);
    }

    internal int CategoryOrder(in InspectionCategoryDefinition definition)
    {
        foreach (ref readonly InspectionCategoryOrder candidate in _categories.AsSpan())
            if (candidate.Category == definition.Id) return candidate.Order;
        return definition.DefaultOrder;
    }

    internal int PropertyOrder(in InspectionPropertyDefinition definition)
    {
        foreach (ref readonly InspectionPropertyOrder candidate in _properties.AsSpan())
            if (candidate.Property == definition.Id) return candidate.Order;
        return definition.DefaultOrder;
    }

    private static void ValidateUnique(InspectionCategoryOrder[] values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            if (!values[index].IsValid)
                throw new ArgumentException("Inspection category ordering must be valid.", nameof(values));
            for (int other = 0; other < index; other++)
                if (values[other].Category == values[index].Category)
                    throw new ArgumentException("Inspection category ordering must be unique.", nameof(values));
        }
    }

    private static void ValidateUnique(InspectionPropertyOrder[] values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            if (!values[index].IsValid)
                throw new ArgumentException("Inspection property ordering must be valid.", nameof(values));
            for (int other = 0; other < index; other++)
                if (values[other].Property == values[index].Property)
                    throw new ArgumentException("Inspection property ordering must be unique.", nameof(values));
        }
    }
}

/// <summary>Formatted client presentation shared by hover and Inventory consumers.</summary>
public sealed class InspectionPresentation
{
    internal InspectionPresentation(string title, string[] rows)
    {
        Title = title;
        Rows = rows;
        DetailText = string.Join('\n', rows);
    }

    public string Title { get; }
    public IReadOnlyList<string> Rows { get; }
    public string DetailText { get; }
}

/// <summary>Engine-neutral deterministic formatting of disclosed typed inspection facts.</summary>
public static class InspectionPresentationFormatter
{
    public static bool TryFormat(
        string? title,
        InspectionPropertySet? properties,
        InspectionPropertyCatalog catalog,
        InspectionPresentationProfile profile,
        out InspectionPresentation? presentation)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(profile);
        presentation = null;
        if (string.IsNullOrWhiteSpace(title) || title != title.Trim() || properties is null)
            return false;
        var rows = new PresentationRow[properties.Count];
        for (int index = 0; index < properties.Count; index++)
        {
            InspectionPropertyFact fact = properties[index];
            InspectionPropertyValue value = fact.Value;
            if (!catalog.TryGetProperty(fact.Id, out InspectionPropertyDefinition property) ||
                property.ValueKind != value.Kind ||
                !catalog.TryGetCategory(property.Category, out InspectionCategoryDefinition category) ||
                !TryFormatValue(in property, in value, out string? text))
                return false;
            rows[index] = new PresentationRow(
                Category: category.Id,
                CategoryOrder: profile.CategoryOrder(in category),
                PropertyOrder: profile.PropertyOrder(in property),
                Property: property.Id,
                Text: text!);
        }
        Array.Sort(rows, static (left, right) => left.CompareTo(right));
        var textRows = new string[rows.Length];
        for (int index = 0; index < rows.Length; index++) textRows[index] = rows[index].Text;
        presentation = new InspectionPresentation(title, textRows);
        return true;
    }

    private static bool TryFormatValue(
        in InspectionPropertyDefinition definition,
        in InspectionPropertyValue value,
        out string? text)
    {
        string rendered = value.Kind switch
        {
            InspectionPropertyValueKind.Integer => value.First.ToString(CultureInfo.InvariantCulture),
            InspectionPropertyValueKind.IntegerRange =>
                $"{value.First.ToString(CultureInfo.InvariantCulture)}–{value.Second.ToString(CultureInfo.InvariantCulture)}",
            InspectionPropertyValueKind.Token => FormatToken(value.Token!),
            InspectionPropertyValueKind.Fraction =>
                $"{value.First.ToString(CultureInfo.InvariantCulture)} / {value.Second.ToString(CultureInfo.InvariantCulture)}",
            InspectionPropertyValueKind.MassGrams => FormatMass(value.First),
            _ => string.Empty,
        };
        if (rendered.Length == 0)
        {
            text = null;
            return false;
        }
        text = definition.LabelMode == InspectionPropertyLabelMode.ValueOnly
            ? rendered
            : $"{definition.Label} {rendered}";
        return true;
    }

    private static string FormatToken(string token)
    {
        var builder = new StringBuilder(token.Length);
        bool capitalize = true;
        foreach (char character in token)
        {
            if (character is '-' or '.')
            {
                builder.Append(' ');
                capitalize = true;
                continue;
            }
            builder.Append(capitalize ? char.ToUpperInvariant(character) : character);
            capitalize = false;
        }
        return builder.ToString();
    }

    private static string FormatMass(long grams) => grams % 1_000 == 0
        ? $"{(grams / 1_000).ToString(CultureInfo.InvariantCulture)} kg"
        : $"{(grams / 1_000m).ToString("0.###", CultureInfo.InvariantCulture)} kg";

    private readonly record struct PresentationRow(
        InspectionCategoryId Category,
        int CategoryOrder,
        int PropertyOrder,
        InspectionPropertyId Property,
        string Text) : IComparable<PresentationRow>
    {
        public int CompareTo(PresentationRow other)
        {
            int categoryOrder = CategoryOrder.CompareTo(other.CategoryOrder);
            if (categoryOrder != 0) return categoryOrder;
            int category = Category.CompareTo(other.Category);
            if (category != 0) return category;
            int propertyOrder = PropertyOrder.CompareTo(other.PropertyOrder);
            return propertyOrder != 0 ? propertyOrder : Property.CompareTo(other.Property);
        }
    }
}

/// <summary>First stock presentation vocabulary/profile; authoritative objects do not own it.</summary>
public static class InspectionPresentationDefaults
{
    public static InspectionPropertyCatalog Catalog { get; } = new(
        [
            new(InspectionCategoryIds.Stats, 100, "Stats"),
            new(InspectionCategoryIds.Attributes, 200, "Attributes"),
            new(InspectionCategoryIds.Durability, 300, "Durability"),
            new(InspectionCategoryIds.Weight, 400, "Weight"),
        ],
        [
            new(InspectionPropertyIds.Damage, InspectionCategoryIds.Stats,
                InspectionPropertyValueKind.IntegerRange, 100, "Damage"),
            new(InspectionPropertyIds.Element, InspectionCategoryIds.Attributes,
                InspectionPropertyValueKind.Token, 100, "Element",
                InspectionPropertyLabelMode.ValueOnly),
            new(InspectionPropertyIds.Quality, InspectionCategoryIds.Attributes,
                InspectionPropertyValueKind.Integer, 200, "Quality"),
            new(InspectionPropertyIds.Durability, InspectionCategoryIds.Durability,
                InspectionPropertyValueKind.Fraction, 100, "Durability"),
            new(InspectionPropertyIds.Weight, InspectionCategoryIds.Weight,
                InspectionPropertyValueKind.MassGrams, 100, "Weight"),
        ]);

    public static InspectionPresentationProfile Profile { get; } = new(
        [
            new(InspectionCategoryIds.Stats, 100),
            new(InspectionCategoryIds.Attributes, 200),
            new(InspectionCategoryIds.Durability, 300),
            new(InspectionCategoryIds.Weight, 400),
        ],
        [
            new(InspectionPropertyIds.Damage, 100),
            new(InspectionPropertyIds.Element, 100),
            new(InspectionPropertyIds.Quality, 200),
            new(InspectionPropertyIds.Durability, 100),
            new(InspectionPropertyIds.Weight, 100),
        ]);

    public static bool TryFormat(
        string? title,
        InspectionPropertySet? properties,
        out InspectionPresentation? presentation) =>
        InspectionPresentationFormatter.TryFormat(
            title, properties, Catalog, Profile, out presentation);
}
