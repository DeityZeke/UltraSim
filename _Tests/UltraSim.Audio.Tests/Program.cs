using UltraSim.Audio;
using UltraSim.Client.Content;
using UltraSim.Client.Sound;
using System.Diagnostics;

var tests = new (string Name, Action Body)[]
{
    ("KernelAndSoundAreGodotFree", KernelAndSoundAreGodotFree),
    ("EverySemanticEventHasOneStableCue", EverySemanticEventHasOneStableCue),
    ("MasterAndCategoriesComposeWithoutChangingCue", MasterAndCategoriesComposeWithoutChangingCue),
    ("SourceAdmissionIsFiniteAndGenerationFenced", SourceAdmissionIsFiniteAndGenerationFenced),
    ("FootstepsFollowDistanceNotUpdateFrequency", FootstepsFollowDistanceNotUpdateFrequency),
    ("GeneratedOwnedClipsAreBoundedCanonicalPcm", GeneratedOwnedClipsAreBoundedCanonicalPcm),
    ("AudioManifestRejectsMalformedAndOversizePayloads", AudioManifestRejectsMalformedAndOversizePayloads),
    ("CatalogAudioIsScopeAuthorizedAndGenerationFenced", CatalogAudioIsScopeAuthorizedAndGenerationFenced),
    ("WarmCueAndAdmissionPathDoesNotAllocate", WarmCueAndAdmissionPathDoesNotAllocate),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} Audio tests passed.");
PrimitiveCensus();

static void KernelAndSoundAreGodotFree()
{
    foreach (System.Reflection.Assembly assembly in new[]
        { typeof(AudioSourceBudget).Assembly, typeof(ClientSoundEngine).Assembly })
        Check(!assembly.GetReferencedAssemblies().Any(reference =>
            reference.Name?.Contains("Godot", StringComparison.OrdinalIgnoreCase) == true));
}

static void EverySemanticEventHasOneStableCue()
{
    foreach (ClientSoundEvent semantic in Enum.GetValues<ClientSoundEvent>())
    {
        Check(ClientSoundEngine.TryResolve(semantic, out SoundCue cue));
        Check(cue.Id != 0 && cue.Priority > 0 && cue.Gain > 0 && cue.Gain <= 1);
        Check(ClientSoundEngine.TryResolve(semantic, out SoundCue second) && cue == second);
    }
    Check(!ClientSoundEngine.TryResolve((ClientSoundEvent)255, out _));
    Check(ClientSoundEngine.TryResolve(ClientSoundEvent.UiClick, out SoundCue ui) &&
        ui.Category == AudioCategory.Ui && ui.SpatialMode == AudioSpatialMode.NonSpatial);
    Check(ClientSoundEngine.TryResolve(ClientSoundEvent.DoorOpen, out SoundCue door) &&
        door.Category == AudioCategory.Sfx && door.SpatialMode == AudioSpatialMode.World3D);
    Check(ClientSoundEngine.TryResolve(ClientSoundEvent.ForestAmbience, out SoundCue ambient) &&
        ambient.Category == AudioCategory.Ambient && ambient.Loop);
}

static void MasterAndCategoriesComposeWithoutChangingCue()
{
    Check(ClientSoundEngine.TryResolve(ClientSoundEvent.Jump, out SoundCue cue));
    Near(.8f * .5f * cue.Gain, ClientSoundEngine.EffectiveGain(.8f, .5f, cue.Gain));
    Near(0, ClientSoundEngine.EffectiveGain(0, 1, cue.Gain));
    Check(ClientSoundEngine.TryResolve(ClientSoundEvent.Jump, out SoundCue after) && cue == after);
    bool rejected = false;
    try { _ = ClientSoundEngine.EffectiveGain(float.NaN, 1, 1); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected);
}

static void SourceAdmissionIsFiniteAndGenerationFenced()
{
    var budget = new AudioSourceBudget(2);
    Check(budget.TryAdmit(20, out AudioSourceHandle low, out _) && budget.Active == 1);
    Check(budget.TryAdmit(50, out AudioSourceHandle high, out _) && budget.Active == 2);
    Check(!budget.TryAdmit(20, out _, out _) && budget.Dropped == 1);
    Check(budget.TryAdmit(90, out AudioSourceHandle replacement, out AudioSourceHandle replaced));
    Check(replaced == low && replacement.Slot == low.Slot && replacement != low);
    Check(!budget.Retire(low) && budget.Retire(replacement) && budget.Retire(high));
    Check(budget.Active == 0 && budget.Peak == 2);
    budget.Clear();
    Check(!budget.Retire(replacement));
}

static void FootstepsFollowDistanceNotUpdateFrequency()
{
    static int Walk(int increments)
    {
        var cadence = new ClientFootstepCadence();
        int emitted = 0;
        _ = cadence.Observe(0, 0, 0, true, true);
        for (int i = 1; i <= increments; i++)
            if (cadence.Observe(i * (4_000 / increments), 0,
                    i * (1_000 / increments), true, true)) emitted++;
        return emitted;
    }
    int slow = Walk(20), fast = Walk(200);
    Check(slow is >= 3 and <= 5 && fast is >= 3 and <= 5);
    var idle = new ClientFootstepCadence();
    for (int i = 0; i < 100; i++)
        Check(!idle.Observe(0, 0, i * 100, false, true));
    Check(!idle.Observe(900, 0, 10_100, true, false));
    Check(!idle.Observe(20_000, 0, 10_200, true, true));
}

static void GeneratedOwnedClipsAreBoundedCanonicalPcm()
{
    var distinct = new HashSet<ContentHash>();
    for (int i = 1; i <= 14; i++)
    {
        byte[] wav = GeneratedSoundClips.CreateWav((SoundCueId)i);
        Check(ContentAudioArtifact.TryInspect(wav, out ContentAudioArtifactInfo info));
        Check(info.SampleRate == GeneratedSoundClips.SampleRate &&
            info.DataOffset == 44 && info.Samples > 0 && wav.Length <= ContentAudioArtifact.MaximumBytes);
        Check(wav.AsSpan().SequenceEqual(GeneratedSoundClips.CreateWav((SoundCueId)i)));
        Check(wav.AsSpan(info.DataOffset).IndexOfAnyExcept((byte)0) >= 0 &&
            distinct.Add(ContentHash.Compute(wav)),
            "Representative cues must carry non-silent and distinct owned PCM.");
        wav[0] = 0;
        Check(!ContentAudioArtifact.TryInspect(wav, out _));
    }
    Check(distinct.Count == 14);
}

static void AudioManifestRejectsMalformedAndOversizePayloads()
{
    byte[] wav = GeneratedSoundClips.CreateWav(SoundCueId.UiClick);
    ContentFilePath.TryCreate("audio/click.wav", out ContentFilePath path);
    Check(!ContentFileDescriptor.TryCreate(path, ContentFileKind.PcmWav,
        ContentAudioArtifact.MaximumBytes + 1L, ContentHash.Compute(wav), default, out _));
    byte[] damaged = wav.ToArray();
    damaged[20] = 3;
    Check(!ContentAudioArtifact.TryInspect(damaged, out _));
    var manifest = MakePackage("sound.clip-one", "sound:click", wav);
    Check(ContentPackageValidator.TryValidatePayloads(manifest, [new ContentFilePayload(path, wav)],
        out _, out _));
    Check(!ContentPackageValidator.TryValidatePayloads(manifest,
        [new ContentFilePayload(path, damaged)], out _, out _));
}

static void CatalogAudioIsScopeAuthorizedAndGenerationFenced()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-audio-tests-" + Guid.NewGuid().ToString("N"));
    try
    {
        byte[] first = GeneratedSoundClips.CreateWav(SoundCueId.UiClick);
        byte[] second = GeneratedSoundClips.CreateWav(SoundCueId.UiOpen);
        ContentPackageManifest a = MakePackage("sound.clip-one", "sound:click", first);
        ContentPackageManifest b = MakePackage("sound.clip-two", "sound:click", second);
        ContentFilePath.TryCreate("audio/click.wav", out ContentFilePath path);
        Check(ContentCache.TryOpen(root, out ContentCache? cache, out _));
        using (cache)
        {
            Check(cache!.TryInstallPackage(ContentScopeKey.Local, a,
                [new ContentFilePayload(path, first)], out _, out _));
            Check(cache.TryInstallPackage(ContentScopeKey.ForRealm(new(1, 1)), a,
                [new ContentFilePayload(path, first)], out _, out _));
            Check(cache.GetSnapshot().BlobCount == 1);
            Check(!cache.TryOpenPackage(ContentScopeKey.ForRealm(new(1, 2)),
                a.PackageId, a.ManifestHash, out _, out _));
            Check(cache.TryInstallPackage(ContentScopeKey.Local, b,
                [new ContentFilePayload(path, second)], out _, out _));
            ContentActivationPlan planA = MakePlan(a, 1);
            ContentActivationPlan planB = MakePlan(b, 2);
            Check(cache.TryPublishCatalog(ContentBaseCatalog.Empty, planA, false,
                out ContentCatalogLease? firstCatalog, out _));
            using (firstCatalog)
            {
                Check(cache.TryPublishCatalog(ContentBaseCatalog.Empty, planB, false,
                    out ContentCatalogLease? secondCatalog, out _));
                using (secondCatalog)
                {
                    LogicalContentId.TryCreate("sound:click", out LogicalContentId id);
                    Check(ContentAudioClipResolver.TryRead(cache, firstCatalog!, id,
                        out ResolvedContentAudioClip oldClip));
                    Check(ContentAudioClipResolver.TryRead(cache, secondCatalog!, id,
                        out ResolvedContentAudioClip newClip));
                    Check(oldClip.BlobHash != newClip.BlobHash &&
                        oldClip.CatalogGeneration != newClip.CatalogGeneration &&
                        oldClip.WavBytes.AsSpan().SequenceEqual(first) &&
                        newClip.WavBytes.AsSpan().SequenceEqual(second));
                }
            }
        }
        Check(ContentCache.TryOpen(root, out ContentCache? reopened, out _));
        using (reopened)
        {
            ContentActivationPlan plan = MakePlan(a, 1);
            ContentScopeActivationKey key = new(plan.ScopeActivation, plan.PlanHash);
            Check(reopened!.TryOpenCatalog(ContentBaseCatalog.Empty, key,
                out ContentCatalogLease? warm, out _));
            using (warm)
            {
                LogicalContentId.TryCreate("sound:click", out LogicalContentId id);
                Check(ContentAudioClipResolver.TryRead(reopened, warm!, id, out _));
            }
        }
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void WarmCueAndAdmissionPathDoesNotAllocate()
{
    var budget = new AudioSourceBudget(16);
    for (int i = 0; i < 64; i++)
    {
        _ = ClientSoundEngine.TryResolve(ClientSoundEvent.Footstep, out _);
        _ = budget.TryAdmit(30, out AudioSourceHandle handle, out _);
        _ = budget.Retire(handle);
    }
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 10_000; i++)
    {
        Check(ClientSoundEngine.TryResolve(ClientSoundEvent.Footstep, out SoundCue cue));
        Check(budget.TryAdmit(cue.Priority, out AudioSourceHandle handle, out _));
        Check(budget.Retire(handle));
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated == 0, $"Warm semantic cue/admission path allocated {allocated} bytes.");
}

static void PrimitiveCensus()
{
    const int iterations = 200_000;
    static double Ns(long start, int count) =>
        (Stopwatch.GetTimestamp() - start) * 1_000_000_000d / Stopwatch.Frequency / count;
    for (int i = 0; i < 10_000; i++)
        _ = ClientSoundEngine.TryResolve(ClientSoundEvent.Footstep, out _);
    int checksum = 0;
    long start = Stopwatch.GetTimestamp();
    for (int i = 0; i < iterations; i++)
    {
        _ = ClientSoundEngine.TryResolve((ClientSoundEvent)(1 + i % 14), out SoundCue cue);
        checksum += (int)cue.Id;
    }
    double resolve = Ns(start, iterations);
    var budget = new AudioSourceBudget(16);
    start = Stopwatch.GetTimestamp();
    for (int i = 0; i < iterations; i++)
    {
        _ = budget.TryAdmit(30, out AudioSourceHandle handle, out _);
        _ = budget.Retire(handle);
    }
    double admitRetire = Ns(start, iterations);
    var cadence = new ClientFootstepCadence();
    _ = cadence.Observe(0, 0, 0, true, true);
    start = Stopwatch.GetTimestamp();
    for (int i = 1; i <= iterations; i++)
        if (cadence.Observe(i * 20L, 0, i * 5L, true, true)) checksum++;
    double cadenceNs = Ns(start, iterations);
    float mutedSum = 0;
    start = Stopwatch.GetTimestamp();
    for (int i = 0; i < iterations; i++)
        mutedSum += ClientSoundEngine.EffectiveGain(0, 1, .6f);
    double mutedGain = Ns(start, iterations);
    Console.WriteLine($"AUDIO PRIMITIVE CENSUS ns/op cue={resolve:F1} " +
        $"admit+retire={admitRetire:F1} cadence={cadenceNs:F1} muted-gain={mutedGain:F1} " +
        $"iterations={iterations} frequency={Stopwatch.Frequency} checksum={checksum} " +
        $"mutedSum={mutedSum} native-excluded=true");
}

static ContentPackageManifest MakePackage(string packageName, string logical, byte[] wav)
{
    Check(ContentPackageId.TryCreate(packageName, out ContentPackageId package));
    Check(ContentNamespaceId.TryCreate("sound", out ContentNamespaceId nameSpace));
    Check(ContentFilePath.TryCreate("audio/click.wav", out ContentFilePath path));
    Check(LogicalContentId.TryCreate(logical, out LogicalContentId id));
    Check(ContentFileDescriptor.TryCreate(path, ContentFileKind.PcmWav,
        wav.Length, ContentHash.Compute(wav), default, out ContentFileDescriptor file));
    Check(ContentDefinitionDescriptor.TryCreateFileDefinition(id,
        ContentDefinitionKind.AudioClip, ContentDefinitionOperation.Add,
        path, out ContentDefinitionDescriptor? definition));
    Check(ContentPackageManifest.TryCreate(1, 1, package, nameSpace,
        "Audio Test", "1", ContentTrustClass.ScopeAuthorizedPassive,
        [file], [definition!], out ContentPackageManifest? manifest, out _));
    return manifest!;
}

static ContentActivationPlan MakePlan(ContentPackageManifest manifest, ulong id)
{
    Check(ContentActivationPlan.TryCreate(ContentScopeKey.Local,
        new ContentActivationId(0x415544494F, id), ContentBaseCatalog.Empty.Fingerprint,
        [new ContentActivationPackage(manifest.PackageId, manifest.ManifestHash,
            ContentRequirementPolicy.Required)], out ContentActivationPlan? plan, out _));
    return plan!;
}

static void Check(bool condition, string? message = null)
{
    if (!condition) throw new InvalidOperationException(message ?? "Audio assertion failed.");
}

static void Near(float expected, float actual)
{
    if (Math.Abs(expected - actual) > .00001f)
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}
