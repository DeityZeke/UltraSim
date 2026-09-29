using System.Globalization;

namespace UltraSim.Framework.RuntimeHost;

/// <summary>A stable package identity. It is never a path or a process-local runtime index.</summary>
public readonly record struct ModulePackageId : IComparable<ModulePackageId>
{
    public string Value { get; }

    public ModulePackageId(string value)
    {
        ModuleIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public bool IsValid => Value is not null;
    public int CompareTo(ModulePackageId other) => StringComparer.Ordinal.Compare(Value, other.Value);
    public override string ToString() => Value ?? "<invalid>";
}

/// <summary>A stable capability identity published by exactly one selected package.</summary>
public readonly record struct ModuleCapabilityId : IComparable<ModuleCapabilityId>
{
    public string Value { get; }

    public ModuleCapabilityId(string value)
    {
        ModuleIdentity.Validate(value, nameof(value));
        Value = value;
    }

    public bool IsValid => Value is not null;
    public int CompareTo(ModuleCapabilityId other) => StringComparer.Ordinal.Compare(Value, other.Value);
    public override string ToString() => Value ?? "<invalid>";
}

/// <summary>Canonical three-component package version; not an assembly or runtime generation.</summary>
public readonly record struct ModuleVersion(int Major, int Minor, int Patch) : IComparable<ModuleVersion>
{
    public static ModuleVersion Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] parts = text.Split('.');
        if (parts.Length != 3) throw new FormatException("Package versions require major.minor.patch.");
        Span<int> values = stackalloc int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length == 0 || (part.Length > 1 && part[0] == '0') ||
                !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
                throw new FormatException("Package versions require canonical nonnegative integers.");
        }
        return new ModuleVersion(values[0], values[1], values[2]);
    }

    public bool IsValid => Major >= 0 && Minor >= 0 && Patch >= 0;
    public int CompareTo(ModuleVersion other)
    {
        int major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        int minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }
    public override string ToString() => FormattableString.Invariant($"{Major}.{Minor}.{Patch}");
}

internal static class ModuleIdentity
{
    internal static void Validate(string value, string parameter)
    {
        if (value is null || value.Length is < 1 or > 96 || value[0] is < 'a' or > 'z')
            throw new ArgumentException("Identity must be 1–96 canonical ASCII characters and begin with a lowercase letter.", parameter);
        char previous = value[0];
        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_') ||
                (c == '.' && previous == '.'))
                throw new ArgumentException("Identity contains noncanonical characters.", parameter);
            previous = c;
        }
        if (previous == '.') throw new ArgumentException("Identity cannot end with a dot.", parameter);
    }
}
