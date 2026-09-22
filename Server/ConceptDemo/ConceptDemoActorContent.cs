using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using UltraSim.Client.Content;

namespace UltraSim.Server.ConceptDemo;

internal static class ConceptDemoActorContent
{
    private const uint JsonChunk = 0x4E4F_534A;
    private const uint BinaryChunk = 0x004E_4942;

    internal static (byte[] X, byte[] Y) LoadOfficialActors(string? officialContentRoot)
    {
        string root = ResolveOfficialContentRoot(officialContentRoot);
        string archivePath = Path.Combine(root, OfficialLocalPresentation.ArchiveFileName);
        byte[] archive = File.ReadAllBytes(archivePath);
        if (!OfficialLocalPresentation.TryDecodeArchive(
                archive, out FetchedContentPackage? package))
            throw new InvalidOperationException(
                $"The Official actor package is absent or does not match the compiled identity: {archivePath}.");
        ContentFilePayload x = package!.Payloads.Single(payload =>
            string.Equals(payload.Path.Value, OfficialLocalPresentation.HumanXPath,
                StringComparison.Ordinal));
        ContentFilePayload y = package.Payloads.Single(payload =>
            string.Equals(payload.Path.Value, OfficialLocalPresentation.HumanYPath,
                StringComparison.Ordinal));
        byte[] xBytes = x.Bytes.ToArray();
        byte[] yBytes = y.Bytes.ToArray();
        _ = RequireConceptDemoProfile(xBytes);
        _ = RequireConceptDemoProfile(yBytes);
        return (xBytes, yBytes);
    }

    internal static ActorPresentationArtifactInfo RequireConceptDemoProfile(
        ReadOnlySpan<byte> bytes)
    {
        ActorPresentationResolutionRequirements required =
            ConceptDemoActorPresentationProfile.Requirements;
        if (!ActorPresentationArtifact.TryInspect(
                bytes, out ActorPresentationArtifactInfo info, out var error) ||
            info.Bones != required.ExactBoneCount ||
            info.OrderedSkeletonFingerprint != required.OrderedSkeletonFingerprint ||
            (info.SemanticClips & required.RequiredClips) != required.RequiredClips)
            throw new InvalidOperationException(
                $"The embedded actor does not satisfy the Concept Demo profile: {error}.");
        return info;
    }

    /// <summary>
    /// Produces an unmistakable but structurally identical X presentation for the server Replace
    /// canary. This is deterministic authoring data; clients receive and validate an ordinary GLB.
    /// </summary>
    internal static byte[] CreateXReplacement(ReadOnlySpan<byte> source)
    {
        ActorPresentationArtifactInfo sourceInfo = RequireConceptDemoProfile(source);
        int jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source[12..]));
        int jsonStart = 20;
        int binaryHeader = checked(jsonStart + jsonLength);
        int binaryLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source[binaryHeader..]));
        uint binaryType = BinaryPrimitives.ReadUInt32LittleEndian(source[(binaryHeader + 4)..]);
        if (binaryType != BinaryChunk || binaryHeader + 8 + binaryLength != source.Length)
            throw new InvalidOperationException("The embedded X actor GLB chunk layout is invalid.");

        JsonNode root = JsonNode.Parse(source.Slice(jsonStart, jsonLength)) ??
            throw new InvalidOperationException("The embedded X actor JSON is invalid.");
        JsonArray? materials = root["materials"]?.AsArray();
        if (materials is null || materials.Count == 0)
            throw new InvalidOperationException("The embedded X actor has no replaceable materials.");
        foreach (JsonNode? material in materials)
        {
            JsonObject? pbr = material?["pbrMetallicRoughness"]?.AsObject();
            if (pbr is null) continue;
            pbr["baseColorFactor"] = new JsonArray(0.92, 0.08, 0.78, 1.0);
            pbr["metallicFactor"] = 0.15;
            pbr["roughnessFactor"] = 0.38;
        }

        byte[] canonicalJson = JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions
        {
            WriteIndented = false,
        });
        int paddedJsonLength = checked((canonicalJson.Length + 3) & ~3);
        int totalLength = checked(12 + 8 + paddedJsonLength + 8 + binaryLength);
        var result = new byte[totalLength];
        BinaryPrimitives.WriteUInt32LittleEndian(result, 0x4654_6C67);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), checked((uint)totalLength));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), checked((uint)paddedJsonLength));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), JsonChunk);
        canonicalJson.CopyTo(result.AsSpan(20));
        result.AsSpan(20 + canonicalJson.Length, paddedJsonLength - canonicalJson.Length).Fill(0x20);
        int outputBinaryHeader = 20 + paddedJsonLength;
        BinaryPrimitives.WriteUInt32LittleEndian(
            result.AsSpan(outputBinaryHeader), checked((uint)binaryLength));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(outputBinaryHeader + 4), BinaryChunk);
        source.Slice(binaryHeader + 8, binaryLength).CopyTo(result.AsSpan(outputBinaryHeader + 8));

        ActorPresentationArtifactInfo replacementInfo = RequireConceptDemoProfile(result);
        if (ContentHash.Compute(result) == ContentHash.Compute(source) ||
            replacementInfo.OrderedSkeletonFingerprint != sourceInfo.OrderedSkeletonFingerprint ||
            replacementInfo.Bones != sourceInfo.Bones ||
            replacementInfo.SemanticClips != sourceInfo.SemanticClips)
            throw new InvalidOperationException(
                "The X actor replacement was not visibly distinct or did not retain its passive profile.");
        return result;
    }

    private static string ResolveOfficialContentRoot(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        string? environment = Environment.GetEnvironmentVariable(
            "ULTRASIM_OFFICIAL_CONTENT_ROOT");
        if (!string.IsNullOrWhiteSpace(environment)) return Path.GetFullPath(environment);
        string installed = Path.Combine(AppContext.BaseDirectory, "Content", "Official");
        if (Directory.Exists(installed)) return installed;
        string development = Path.GetFullPath(Path.Combine(
            Environment.CurrentDirectory, "Client", "OfficialContent"));
        if (Directory.Exists(development)) return development;
        return installed;
    }
}
