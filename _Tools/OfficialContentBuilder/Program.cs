using System.Security.Cryptography;
using UltraSim.Client.Content;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("usage: OfficialContentBuilder <output-root> [presentation-assets-root]");
    return 2;
}

string outputRoot = Path.GetFullPath(args[0]);
string repositoryRoot = FindRepositoryRoot();
string assetRoot = args.Length == 2
    ? Path.GetFullPath(args[1])
    : Path.Combine(repositoryRoot, "Client", "PresentationAssets");
string xPath = Path.Combine(assetRoot, "Avatars", "x_bot", "x_bot_animated.glb");
string yPath = Path.Combine(assetRoot, "Avatars", "y_bot", "y_bot_animated.glb");
byte[] x = File.ReadAllBytes(xPath);
byte[] y = File.ReadAllBytes(yPath);

RequireExact(x, OfficialLocalPresentation.HumanXBytes, OfficialLocalPresentation.HumanXSha256, "X");
RequireExact(y, OfficialLocalPresentation.HumanYBytes, OfficialLocalPresentation.HumanYSha256, "Y");
RequireProfile(x, "X");
RequireProfile(y, "Y");

ContentFileDescriptor xFile = OfficialLocalPresentation.Manifest.Files.Single(file =>
    string.Equals(file.Path.Value, OfficialLocalPresentation.HumanXPath, StringComparison.Ordinal));
ContentFileDescriptor yFile = OfficialLocalPresentation.Manifest.Files.Single(file =>
    string.Equals(file.Path.Value, OfficialLocalPresentation.HumanYPath, StringComparison.Ordinal));
ContentFilePayload[] payloads =
[
    new(xFile.Path, x),
    new(yFile.Path, y),
];
if (!ContentPackageValidator.TryValidatePayloads(
        OfficialLocalPresentation.Manifest, payloads, out _, out string? validationError) ||
    !ContentPackageArchive.TryEncodeCanonical(
        OfficialLocalPresentation.Manifest, payloads, out byte[] archive))
    throw new InvalidOperationException(
        $"The Official Local package could not be validated/encoded: {validationError}");

Directory.CreateDirectory(outputRoot);
string output = Path.Combine(outputRoot, OfficialLocalPresentation.ArchiveFileName);
if (!File.Exists(output) || !File.ReadAllBytes(output).AsSpan().SequenceEqual(archive))
    File.WriteAllBytes(output, archive);
Console.WriteLine(
    $"OFFICIAL CONTENT PASS package={OfficialLocalPresentation.Manifest.PackageId} " +
    $"manifest={OfficialLocalPresentation.Manifest.ManifestHash} " +
    $"plan={OfficialLocalPresentation.Plan.PlanHash} archive={Hex(SHA256.HashData(archive))} " +
    $"bytes={archive.Length} output={output}");
return 0;

static void RequireExact(byte[] bytes, long expectedSize, string expectedHash, string actor)
{
    string hash = Hex(SHA256.HashData(bytes));
    if (bytes.LongLength != expectedSize || !string.Equals(hash, expectedHash, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"The canonical {actor} actor source changed: bytes={bytes.LongLength}, sha256={hash}.");
}

static string Hex(ReadOnlySpan<byte> bytes) =>
    Convert.ToHexString(bytes).ToLowerInvariant();

static void RequireProfile(byte[] bytes, string actor)
{
    ActorPresentationResolutionRequirements required =
        ConceptDemoActorPresentationProfile.Requirements;
    if (!ActorPresentationArtifact.TryInspect(
            bytes, out ActorPresentationArtifactInfo info, out ActorPresentationArtifactError error) ||
        info.Bones != required.ExactBoneCount ||
        info.OrderedSkeletonFingerprint != required.OrderedSkeletonFingerprint ||
        (info.SemanticClips & required.RequiredClips) != required.RequiredClips)
        throw new InvalidOperationException(
            $"The canonical {actor} actor source violates the passive profile: {error}.");
}

static string FindRepositoryRoot()
{
    string? current = Directory.GetCurrentDirectory();
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current, "verify.sh")) &&
            Directory.Exists(Path.Combine(current, "Client", "Content")))
            return current;
        current = Directory.GetParent(current)?.FullName;
    }
    throw new InvalidOperationException("The UltraSim repository root was not found.");
}
