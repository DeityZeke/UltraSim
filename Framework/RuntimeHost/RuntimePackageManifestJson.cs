using System.Text.Json;

namespace UltraSim.Framework.RuntimeHost;

/// <summary>Strict, finite on-disk package metadata; no source path is a package identity.</summary>
public static class RuntimePackageManifestJson
{
    public const string Format = "UltraSim.RuntimePackage/v4";
    public const int MaximumBytes = 32 * 1024;

    public static RuntimePackageManifest Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length is < 1 or > MaximumBytes)
            throw new FormatException("Runtime package manifest byte length is outside its bound.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid("Root must be an object.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? format = null, id = null, version = null, stage = null, packageClass = null, entrypoint = null;
            List<RuntimePackageDependency>? dependencies = null;
            List<PackageCapabilityId>? provides = null, requires = null, optional = null;
            List<RuntimeServiceId>? services = null, requiresServices = null, optionalServices = null;
            List<RuntimePackageId>? before = null, after = null, conflicts = null, aliases = null;
            RuntimePackageDurableSchema? durableSchema = null;
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
                    case "services": services = Services(property.Value, "services"); break;
                    case "requiresServices": requiresServices = Services(property.Value, "requiresServices"); break;
                    case "optionalServices": optionalServices = Services(property.Value, "optionalServices"); break;
                    case "entrypoint": entrypoint = Text(property.Value, "entrypoint"); break;
                    case "before": before = Packages(property.Value, "before"); break;
                    case "after": after = Packages(property.Value, "after"); break;
                    case "conflicts": conflicts = Packages(property.Value, "conflicts"); break;
                    case "aliases": aliases = Packages(property.Value, "aliases"); break;
                    case "durableSchema": durableSchema = DurableSchema(property.Value); break;
                    default: throw Invalid($"Unknown property {property.Name}.");
                }
            }
            if (format != Format) throw Invalid("Unsupported runtime package manifest format.");
            if (id is null || version is null || stage is null || packageClass is null)
                throw Invalid("Missing required id, version, stage or class.");
            return new RuntimePackageManifest(new(id), RuntimePackageVersion.Parse(version), Stage(stage), Class(packageClass),
                dependencies, provides, requires, optional, before, after, conflicts,
                services, requiresServices, optionalServices, entrypoint, aliases, durableSchema);
        }
        catch (JsonException exception) { throw new FormatException("Invalid runtime package manifest JSON.", exception); }
        catch (ArgumentException exception) { throw new FormatException("Invalid runtime package manifest field.", exception); }
    }

    private static string Text(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.String ? element.GetString()! : throw Invalid($"{property} must be a string.");

    private static List<PackageCapabilityId> Capabilities(JsonElement array, string property)
    {
        var values = new List<PackageCapabilityId>();
        foreach (JsonElement item in Items(array, property)) values.Add(new(Text(item, property)));
        return values;
    }

    private static List<RuntimeServiceId> Services(JsonElement array, string property)
    {
        var values = new List<RuntimeServiceId>();
        foreach (JsonElement item in Items(array, property)) values.Add(new(Text(item, property)));
        return values;
    }

    private static List<RuntimePackageId> Packages(JsonElement array, string property)
    {
        var values = new List<RuntimePackageId>();
        foreach (JsonElement item in Items(array, property)) values.Add(new(Text(item, property)));
        return values;
    }

    private static List<RuntimePackageDependency> Dependencies(JsonElement array)
    {
        var values = new List<RuntimePackageDependency>();
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
            values.Add(new(new(package), RuntimePackageVersion.Parse(minimum), maximum is null ? null : RuntimePackageVersion.Parse(maximum)));
        }
        return values;
    }

    private static RuntimePackageDurableSchema DurableSchema(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("durableSchema must be an object.");
        string? service = null, artifact = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw Invalid($"Duplicate durableSchema property {property.Name}.");
            switch (property.Name)
            {
                case "participantService": service = Text(property.Value, property.Name); break;
                case "artifactKey": artifact = Text(property.Value, property.Name); break;
                default: throw Invalid($"Unknown durableSchema property {property.Name}.");
            }
        }
        if (service is null || artifact is null) throw Invalid("durableSchema requires participantService and artifactKey.");
        return new(new RuntimeServiceId(service), artifact);
    }

    private static IEnumerable<JsonElement> Items(JsonElement array, string property)
    {
        if (array.ValueKind != JsonValueKind.Array) throw Invalid($"{property} must be an array.");
        if (array.GetArrayLength() > RuntimePackageManifest.MaximumRelations)
            throw Invalid($"{property} exceeds the relation bound.");
        return array.EnumerateArray();
    }

    private static RuntimePackageStage Stage(string text) => text switch
    {
        "Contracts" => RuntimePackageStage.Contracts,
        "Core" => RuntimePackageStage.Core,
        "Scripts" => RuntimePackageStage.Scripts,
        _ => throw Invalid("Unknown package stage.")
    };

    private static RuntimePackageClass Class(string text) => text switch
    {
        "Runtime" => RuntimePackageClass.Runtime,
        "Definition" => RuntimePackageClass.Definition,
        "System" => RuntimePackageClass.System,
        "EngineCore" => RuntimePackageClass.EngineCore,
        "Kernel" => RuntimePackageClass.Kernel,
        "Schema" => RuntimePackageClass.Schema,
        "Framework" => RuntimePackageClass.Framework,
        _ => throw Invalid("Unknown package class.")
    };

    private static FormatException Invalid(string detail) => new(detail);
}
