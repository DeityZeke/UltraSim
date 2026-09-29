using System.Text.Json;

namespace UltraSim.Framework.RuntimeHost;

/// <summary>Strict, finite on-disk package metadata; source paths and compiler output are a later seam.</summary>
public static class ModuleManifestJson
{
    public const string Format = "UltraSim.ServerModule/v2";
    public const int MaximumBytes = 32 * 1024;

    public static ModulePackageManifest Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length is < 1 or > MaximumBytes)
            throw new FormatException("Module manifest byte length is outside its bound.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid("Root must be an object.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? format = null, id = null, version = null, stage = null, packageClass = null;
            List<ModuleDependency>? dependencies = null;
            List<ModuleCapabilityId>? provides = null, requires = null, optional = null;
            List<ModulePackageId>? before = null, after = null, conflicts = null;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw Invalid($"Duplicate property {property.Name}.");
                switch (property.Name)
                {
                    case "format": format = Text(property.Value, "format"); break;
                    case "id": id = Text(property.Value, "id"); break;
                    case "version": version = Text(property.Value, "version"); break;
                    case "stage": stage = Text(property.Value, "stage"); break;
                    case "class": packageClass = Text(property.Value, "class"); break;
                    case "dependencies": dependencies = Dependencies(property.Value); break;
                    case "provides": provides = Capabilities(property.Value, "provides"); break;
                    case "requires": requires = Capabilities(property.Value, "requires"); break;
                    case "optional": optional = Capabilities(property.Value, "optional"); break;
                    case "before": before = Packages(property.Value, "before"); break;
                    case "after": after = Packages(property.Value, "after"); break;
                    case "conflicts": conflicts = Packages(property.Value, "conflicts"); break;
                    default: throw Invalid($"Unknown property {property.Name}.");
                }
            }
            if (format != Format) throw Invalid("Unsupported module manifest format.");
            if (id is null || version is null || stage is null || packageClass is null)
                throw Invalid("Missing required id, version, stage or class.");
            return new ModulePackageManifest(new(id), ModuleVersion.Parse(version), Stage(stage), Class(packageClass),
                dependencies, provides, requires, optional, before, after, conflicts);
        }
        catch (JsonException exception) { throw new FormatException("Invalid module manifest JSON.", exception); }
        catch (ArgumentException exception) { throw new FormatException("Invalid module manifest field.", exception); }
    }

    private static string Text(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.String ? element.GetString()! : throw Invalid($"{property} must be a string.");

    private static List<ModuleCapabilityId> Capabilities(JsonElement array, string property)
    {
        var values = new List<ModuleCapabilityId>();
        foreach (JsonElement item in Items(array, property)) values.Add(new(Text(item, property)));
        return values;
    }

    private static List<ModulePackageId> Packages(JsonElement array, string property)
    {
        var values = new List<ModulePackageId>();
        foreach (JsonElement item in Items(array, property)) values.Add(new(Text(item, property)));
        return values;
    }

    private static List<ModuleDependency> Dependencies(JsonElement array)
    {
        var values = new List<ModuleDependency>();
        foreach (JsonElement item in Items(array, "dependencies"))
        {
            if (item.ValueKind != JsonValueKind.Object) throw Invalid("Dependency must be an object.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? package = null, minimum = null, maximum = null;
            foreach (JsonProperty property in item.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw Invalid($"Duplicate dependency property {property.Name}.");
                switch (property.Name)
                {
                    case "package": package = Text(property.Value, "package"); break;
                    case "minimumVersion": minimum = Text(property.Value, "minimumVersion"); break;
                    case "maximumVersionExclusive": maximum = Text(property.Value, "maximumVersionExclusive"); break;
                    default: throw Invalid($"Unknown dependency property {property.Name}.");
                }
            }
            if (package is null || minimum is null) throw Invalid("Dependency requires package and minimumVersion.");
            values.Add(new(new(package), ModuleVersion.Parse(minimum), maximum is null ? null : ModuleVersion.Parse(maximum)));
        }
        return values;
    }

    private static IEnumerable<JsonElement> Items(JsonElement array, string property)
    {
        if (array.ValueKind != JsonValueKind.Array) throw Invalid($"{property} must be an array.");
        if (array.GetArrayLength() > ModulePackageManifest.MaximumRelations)
            throw Invalid($"{property} exceeds the relation bound.");
        return array.EnumerateArray();
    }

    private static ModulePackageStage Stage(string text) => text switch
    {
        "Contracts" => ModulePackageStage.Contracts,
        "Core" => ModulePackageStage.Core,
        "Scripts" => ModulePackageStage.Scripts,
        _ => throw Invalid("Unknown package stage.")
    };

    private static ModulePackageClass Class(string text) => text switch
    {
        "RuntimeExtension" => ModulePackageClass.RuntimeExtension,
        "Definition" => ModulePackageClass.Definition,
        "System" => ModulePackageClass.System,
        "EngineCore" => ModulePackageClass.EngineCore,
        "KernelExtension" => ModulePackageClass.KernelExtension,
        "SchemaExtension" => ModulePackageClass.SchemaExtension,
        "FrameworkExtension" => ModulePackageClass.FrameworkExtension,
        _ => throw Invalid("Unknown package class.")
    };

    private static FormatException Invalid(string detail) => new(detail);
}
