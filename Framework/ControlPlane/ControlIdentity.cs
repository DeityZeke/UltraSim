namespace UltraSim.Framework.ControlPlane;

/// <summary>Finite management-plane limits. All collection inputs are copied with a limit.</summary>
public static class ControlLimits
{
    public const int Id = 96, Category = 192, CategoryDepth = 8, Label = 128, Description = 1024;
    public const int Text = 1024, Options = 32, Providers = 64, Publications = 512;
    public const int Batch = 32, Arguments = 16, Columns = 24, Rows = 1024, Capabilities = 64;
    internal static string IdText(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > Id || value[0] is < 'a' or > 'z' ||
            value.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_')) ||
            value.EndsWith('.') || value.Contains("..")) throw new ArgumentException("Noncanonical control identity.");
        return value;
    }
    internal static string Bounded(string value, int max)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > max || value.Any(char.IsControl)) throw new ArgumentException("Invalid bounded text.");
        return value;
    }
    internal static T[] Copy<T>(IEnumerable<T> source, int maximum)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new List<T>();
        foreach (T item in source)
        {
            if (result.Count == maximum) throw new ArgumentException("Control Plane capacity exceeded.");
            result.Add(item);
        }
        return result.ToArray();
    }
}

public readonly record struct ControlProviderId
{
    public ControlProviderId(string value) => Value = ControlLimits.IdText(value);
    public string? Value { get; }
    public bool IsValid => Value is not null;
    public override string ToString() => Value ?? "";
}

public readonly record struct ControlId
{
    public ControlId(string value) => Value = ControlLimits.IdText(value);
    public string? Value { get; }
    public bool IsValid => Value is not null;
    public override string ToString() => Value ?? "";
}

public readonly record struct DiagnosticId
{
    public DiagnosticId(string value) => Value = ControlLimits.IdText(value);
    public string? Value { get; }
    public bool IsValid => Value is not null;
    public override string ToString() => Value ?? "";
}

public readonly record struct ControlActionId
{
    public ControlActionId(string value) => Value = ControlLimits.IdText(value);
    public string? Value { get; }
    public bool IsValid => Value is not null;
    public override string ToString() => Value ?? "";
}

public readonly record struct ControlCollectionId
{
    public ControlCollectionId(string value) => Value = ControlLimits.IdText(value);
    public string? Value { get; }
    public bool IsValid => Value is not null;
    public override string ToString() => Value ?? "";
}

public readonly record struct ControlCapabilityId
{
    public ControlCapabilityId(string value) => Value = ControlLimits.IdText(value);
    public string? Value { get; }
    public bool IsValid => Value is not null;
    public override string ToString() => Value ?? "";
}

public readonly record struct ControlOptionId
{
    public ControlOptionId(string value) => Value = ControlLimits.IdText(value);
    public string? Value { get; }
    public bool IsValid => Value is not null;
    public override string ToString() => Value ?? "";
}

public readonly record struct ControlRowId
{
    public ControlRowId(string value) => Value = ControlLimits.IdText(value);
    public string? Value { get; }
    public bool IsValid => Value is not null;
    public override string ToString() => Value ?? "";
}

public readonly record struct ControlRequestId
{
    public ControlRequestId(string value) => Value = ControlLimits.IdText(value);
    public string? Value { get; }
    public bool IsValid => Value is not null;
    public override string ToString() => Value ?? "";
}

public readonly record struct ControlCategoryPath
{
    public ControlCategoryPath(string value)
    {
        ControlLimits.Bounded(value, ControlLimits.Category);
        string[] parts = value.Split('/');
        if (parts.Length > ControlLimits.CategoryDepth || parts.Any(p => p.Length == 0 ||
            !char.IsAsciiLetter(p[0]) || p.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')))
            throw new ArgumentException("Noncanonical category.");
        Value = value;
    }
    public string? Value { get; }
    public bool IsValid => Value is not null;
    public override string ToString() => Value ?? "";
}
