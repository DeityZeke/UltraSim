using Godot;
using UltraSim.Client.Content;
using UltraSim.Client.Sound;

namespace UltraSim.Client.Godot;

/// <summary>
/// Product-owned generated clips are ordinary passive world.local content. Cache failure only
/// removes optional audio; the deterministic validated source remains available as fallback.
/// </summary>
internal static class GodotAudioContentBank
{
    private sealed record Authored(
        ContentPackageManifest Manifest, ContentActivationPlan Plan,
        ContentFilePayload[] Payloads, LogicalContentId[] Ids);

    private static readonly Lazy<Authored> s_authored = new(CreateAuthored);

    internal static LogicalContentId CueId(SoundCueId cue) =>
        cue is >= SoundCueId.FootstepGrass and <= SoundCueId.ForestWind
            ? s_authored.Value.Ids[(int)cue] : default;

    internal static bool TryOpenOwnedClip(SoundCueId cue, out ResolvedContentAudioClip clip)
    {
        clip = default;
        LogicalContentId id = CueId(cue);
        if (!id.IsValid) return false;
        string root = ProjectSettings.GlobalizePath("user://content-cache");
        if (!ContentCache.TryOpen(root, out ContentCache? cache, out _)) return false;
        using (cache)
        {
            Authored source = s_authored.Value;
            ContentScopeActivationKey key = new(source.Plan.ScopeActivation,
                source.Plan.PlanHash);
            if (!cache!.TryOpenCatalog(ContentBaseCatalog.Empty, key,
                    out ContentCatalogLease? catalog, out _)) return false;
            using (catalog)
                return ContentAudioClipResolver.TryRead(cache, catalog!, id, out clip);
        }
    }

    internal static byte[][] LoadOrGenerate(out bool fromCatalog)
    {
        Authored source = s_authored.Value;
        byte[][] result = new byte[source.Ids.Length][];
        fromCatalog = false;
        string root = ProjectSettings.GlobalizePath("user://content-cache");
        try
        {
            if (ContentCache.TryOpen(root, out ContentCache? cache, out _))
            {
                using (cache)
                {
                    ContentScopeActivationKey key = new(source.Plan.ScopeActivation,
                        source.Plan.PlanHash);
                    if (!cache!.TryOpenCatalog(ContentBaseCatalog.Empty, key,
                            out ContentCatalogLease? catalog, out _))
                    {
                        if (cache.TryInstallPackage(ContentScopeKey.Local,
                                source.Manifest, source.Payloads, out _, out _) &&
                            cache.TryPublishCatalog(ContentBaseCatalog.Empty, source.Plan,
                                persistentlyPinned: false, out catalog, out _))
                        {
                            // Published through the same Local scope/CAS implementation as
                            // Official presentation, never through ResourceLoader paths.
                        }
                    }
                    using (catalog)
                    {
                        if (catalog is not null)
                        {
                            bool complete = true;
                            for (int i = 1; i < source.Ids.Length; i++)
                            {
                                if (ContentAudioClipResolver.TryRead(cache, catalog,
                                        source.Ids[i], out ResolvedContentAudioClip clip))
                                    result[i] = clip.WavBytes;
                                else { complete = false; break; }
                            }
                            if (complete) fromCatalog = true;
                        }
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or ObjectDisposedException)
        {
            // Optional presentation bytes may fail independently of Game authority.
        }
        if (!fromCatalog)
            for (int i = 1; i < result.Length; i++)
                result[i] = source.Payloads[i - 1].Bytes.ToArray();
        return result;
    }

    private static Authored CreateAuthored()
    {
        if (!ContentPackageId.TryCreate("ultrasim.generated-audio-v1", out ContentPackageId packageId) ||
            !ContentNamespaceId.TryCreate("sound", out ContentNamespaceId nameSpace))
            throw new InvalidOperationException("The owned sound package identity is invalid.");
        var files = new ContentFileDescriptor[14];
        var definitions = new ContentDefinitionDescriptor[14];
        var payloads = new ContentFilePayload[14];
        var ids = new LogicalContentId[15];
        for (int i = 1; i <= 14; i++)
        {
            string slug = ((SoundCueId)i).ToString().ToLowerInvariant();
            byte[] bytes = GeneratedSoundClips.CreateWav((SoundCueId)i);
            if (!ContentFilePath.TryCreate($"audio/{slug}.wav", out ContentFilePath path) ||
                !LogicalContentId.TryCreate($"sound:{slug}", out ids[i]) ||
                !ContentFileDescriptor.TryCreate(path, ContentFileKind.PcmWav, bytes.Length,
                    ContentHash.Compute(bytes), default, out files[i - 1]) ||
                !ContentDefinitionDescriptor.TryCreateFileDefinition(ids[i],
                    ContentDefinitionKind.AudioClip, ContentDefinitionOperation.Add,
                    path, out ContentDefinitionDescriptor? definition))
                throw new InvalidOperationException("The generated audio file definition is invalid.");
            definitions[i - 1] = definition!;
            payloads[i - 1] = new(path, bytes);
        }
        if (!ContentPackageManifest.TryCreate(1, 1, packageId, nameSpace,
                "UltraSim generated sound cues", "1.0.0", ContentTrustClass.ScopeAuthorizedPassive,
                files, definitions, out ContentPackageManifest? manifest, out string? error) ||
            !ContentActivationPlan.TryCreate(ContentScopeKey.Local,
                // The package is generated by the binary. A new binary may change its
                // bytes; never reinterpret an existing activation under the same ID.
                new ContentActivationId(manifest!.ManifestHash.Part0 ^ 0x5038_2D41_5544_494FUL,
                    manifest.ManifestHash.Part1 ^ 0x2D4C_4F43_414C_0001UL),
                ContentBaseCatalog.Empty.Fingerprint,
                [new ContentActivationPackage(packageId, manifest!.ManifestHash,
                    ContentRequirementPolicy.Required)], out ContentActivationPlan? plan,
                out error))
            throw new InvalidOperationException($"The generated audio package is invalid: {error}");
        return new(manifest!, plan!, payloads, ids);
    }
}
