using System.Text.Json;

namespace UltraSim.Client.Content;

public sealed record ContentPackManifest(
    string Schema,
    ContentPackInfo Pack,
    IReadOnlyList<ContentPackFileEntry> Files,
    IReadOnlyList<ContentPackDefinitionEntry> Definitions,
    IReadOnlyList<ContentPackDependency> Dependencies,
    IReadOnlyList<ContentPackFilter> Filters)
{
    public const string CurrentSchema = "ultrasim.content-pack.v1";
    public const int CurrentPipelineFormat = 1;
    public const string ManifestPath = "pack.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static bool TryDeserialize(string? json, out ContentPackManifest manifest, out string error)
    {
        manifest = default!;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Content pack manifest is empty.";
            return false;
        }

        try
        {
            ContentPackManifest? parsed = JsonSerializer.Deserialize<ContentPackManifest>(json, JsonOptions);
            if (parsed is null)
            {
                error = "Content pack manifest is empty.";
                return false;
            }

            manifest = parsed with
            {
                Files = parsed.Files ?? Array.Empty<ContentPackFileEntry>(),
                Definitions = parsed.Definitions ?? Array.Empty<ContentPackDefinitionEntry>(),
                Dependencies = parsed.Dependencies ?? Array.Empty<ContentPackDependency>(),
                Filters = parsed.Filters ?? Array.Empty<ContentPackFilter>()
            };
            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Content pack manifest JSON is invalid: {ex.Message}";
            return false;
        }
    }
}

public sealed record ContentPackInfo(
    string Id,
    string Name,
    string Version,
    int MinPipelineFormat,
    int MaxPipelineFormat,
    string? ContentHash);

public sealed record ContentPackFileEntry(
    string Path,
    string Kind,
    long SizeBytes,
    string Sha256);

public sealed record ContentPackDefinitionEntry(
    string Id,
    string Kind,
    string Mode,
    string? DisplayName,
    string? Model,
    string? Texture,
    string? Material,
    ContentPackVector3? VisualOffset,
    ContentPackVector3? VisualRotationDegrees,
    ContentPackVector3? VisualScale,
    ContentPackVector3? CameraPivotOffset,
    ContentPackVector3? NameplateOffset,
    ContentPackVector3? EffectsOffset,
    IReadOnlyList<ContentPackMaterialSlotEntry>? MaterialSlots);

public sealed record ContentPackMaterialSlotEntry(
    string SlotId,
    string Resource,
    string? Texture,
    string? Material,
    string? HueChannel,
    int? DefaultHue);

public sealed record ContentPackVector3(float X, float Y, float Z);

public sealed record ContentPackDependency(
    string Id,
    string? MinVersion,
    string? MaxVersion,
    bool Required);

public sealed record ContentPackFilter(
    string Namespace,
    string Path);

public static class ContentPackDefinitionKind
{
    public const string ItemVisual = "item-visual";
    public const string Mobile = "mobile";
    public const string Material = "material";
    public const string Texture = "texture";
    public const string Model = "model";
    public const string Icon = "icon";
    public const string UiSurface = "ui-surface";
    public const string SoundEvent = "sound-event";
}

public static class ContentPackDefinitionMode
{
    public const string Append = "append";
    public const string Replace = "replace";
    public const string Patch = "patch";
}

public static class ContentPackHueChannel
{
    public const string Main = "main";
    public const string Secondary = "secondary";
    public const string Trim = "trim";
}
