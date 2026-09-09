using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UltraSim.Client.Content;

[Flags]
public enum ActorPresentationSemanticClips : byte
{
    None = 0,
    Idle = 1 << 0,
    Walk = 1 << 1,
    Jump = 1 << 2,
    RequiredV1 = Idle | Walk | Jump,
}

public enum ActorPresentationArtifactError : byte
{
    None = 0,
    InvalidContainer,
    LimitExceeded,
    InvalidJson,
    UnsupportedFeature,
    InvalidReference,
    InvalidSkeleton,
    MissingSemanticClip,
}

/// <summary>Strict passive GLB bounds for the first animated humanoid content profile.</summary>
public readonly record struct ActorPresentationArtifactLimits(
    int MaximumArtifactBytes,
    int MaximumJsonBytes,
    int MaximumNodes,
    int MaximumMeshes,
    int MaximumPrimitives,
    int MaximumMeshVertices,
    int MaximumMeshIndices,
    int MaximumAccessors,
    int MaximumBufferViews,
    int MaximumMaterials,
    int MaximumBones,
    int MaximumAnimations,
    int MaximumAnimationTracks,
    int MaximumAnimationKeys,
    int MaximumStringCharacters)
{
    public static ActorPresentationArtifactLimits Default { get; } = new(
        32 * 1024 * 1024,
        4 * 1024 * 1024,
        256,
        32,
        128,
        250_000,
        1_000_000,
        4_096,
        4_096,
        64,
        128,
        32,
        1_024,
        1_000_000,
        256);

    internal static ActorPresentationArtifactLimits Resolve(
        ActorPresentationArtifactLimits limits) =>
        limits.MaximumArtifactBytes == 0 ? Default : limits;

    internal bool IsValid =>
        MaximumArtifactBytes is >= 20 and <= 512 * 1024 * 1024 &&
        MaximumJsonBytes >= 2 && MaximumJsonBytes <= MaximumArtifactBytes &&
        MaximumNodes is >= 1 and <= 16_384 &&
        MaximumMeshes is >= 1 and <= 4_096 &&
        MaximumPrimitives is >= 1 and <= 65_536 &&
        MaximumMeshVertices is >= 1 and <= 16_000_000 &&
        MaximumMeshIndices is >= 3 and <= 64_000_000 &&
        MaximumAccessors is >= 1 and <= 65_536 &&
        MaximumBufferViews is >= 1 and <= 65_536 &&
        MaximumMaterials is >= 1 and <= 4_096 &&
        MaximumBones is >= 1 and <= 1_024 &&
        MaximumAnimations is >= 1 and <= 1_024 &&
        MaximumAnimationTracks is >= 1 and <= 65_536 &&
        MaximumAnimationKeys is >= 1 and <= 16_000_000 &&
        MaximumStringCharacters is >= 1 and <= 4_096;
}

/// <summary>Validated, engine-neutral facts about one self-contained actor GLB.</summary>
public readonly record struct ActorPresentationArtifactInfo(
    int ArtifactBytes,
    int JsonBytes,
    int Nodes,
    int Meshes,
    int Primitives,
    int MeshVertices,
    int MeshIndices,
    int Accessors,
    int BufferViews,
    int Materials,
    int Bones,
    int Animations,
    int AnimationTracks,
    int AnimationKeys,
    ContentHash OrderedSkeletonFingerprint,
    ActorPresentationSemanticClips SemanticClips,
    string MainHandBone)
{
    public bool IsValid => ArtifactBytes >= 20 && Nodes > 0 && Meshes > 0 && Primitives > 0 &&
        MeshVertices > 0 && MeshIndices > 0 && Accessors > 0 && BufferViews > 0 &&
        OrderedSkeletonFingerprint.IsValid &&
        (SemanticClips & ActorPresentationSemanticClips.RequiredV1) ==
            ActorPresentationSemanticClips.RequiredV1 &&
        !string.IsNullOrEmpty(MainHandBone);
}

/// <summary>
/// Passive admission for the D0.7b humanoid profile. It accepts only self-contained glTF 2.0
/// binary data, a single bounded skeleton, triangle meshes, embedded binary buffers, and the
/// finite Idle/Walk/Jump vocabulary. Native Godot import remains a later realization step.
/// </summary>
public static class ActorPresentationArtifact
{
    private const uint GlbMagic = 0x4654_6C67; // glTF
    private const uint GlbVersion = 2;
    private const uint JsonChunk = 0x4E4F_534A;
    private const uint BinaryChunk = 0x004E_4942;
    private const string SkeletonDomain = "ultrasim.actor-skeleton.v1";
    private static readonly string[] SupportedExtensions = ["KHR_materials_specular"];

    public static bool TryInspect(
        ReadOnlySpan<byte> bytes,
        out ActorPresentationArtifactInfo info,
        out ActorPresentationArtifactError error,
        ActorPresentationArtifactLimits limits = default)
    {
        info = default;
        error = ActorPresentationArtifactError.None;
        limits = ActorPresentationArtifactLimits.Resolve(limits);
        if (!limits.IsValid || bytes.Length < 20 || bytes.Length > limits.MaximumArtifactBytes)
            return Fail(ActorPresentationArtifactError.LimitExceeded, out error);
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != GlbMagic ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != GlbVersion ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) != bytes.Length)
            return Fail(ActorPresentationArtifactError.InvalidContainer, out error);

        int cursor = 12;
        if (!TryReadChunk(bytes, ref cursor, out ReadOnlySpan<byte> jsonBytes, out uint jsonType) ||
            jsonType != JsonChunk || jsonBytes.Length > limits.MaximumJsonBytes ||
            !TryReadChunk(bytes, ref cursor, out ReadOnlySpan<byte> binaryBytes, out uint binaryType) ||
            binaryType != BinaryChunk || cursor != bytes.Length)
            return Fail(ActorPresentationArtifactError.InvalidContainer, out error);

        try
        {
            using JsonDocument document = JsonDocument.Parse(jsonBytes.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("asset", out JsonElement asset) ||
                !TryString(asset, "version", limits.MaximumStringCharacters, out string version) ||
                version != "2.0" ||
                !ExtensionsSupported(root, limits.MaximumStringCharacters) ||
                root.TryGetProperty("cameras", out _) ||
                root.TryGetProperty("images", out JsonElement images) && images.GetArrayLength() != 0 ||
                root.TryGetProperty("textures", out JsonElement textures) && textures.GetArrayLength() != 0)
                return Fail(ActorPresentationArtifactError.UnsupportedFeature, out error);

            if (!TryArray(root, "nodes", 1, limits.MaximumNodes, out JsonElement nodes) ||
                !TryArray(root, "meshes", 1, limits.MaximumMeshes, out JsonElement meshes) ||
                !TryArray(root, "accessors", 1, limits.MaximumAccessors, out JsonElement accessors) ||
                !TryArray(root, "bufferViews", 1, limits.MaximumBufferViews, out JsonElement views) ||
                !TryArray(root, "buffers", 1, 1, out JsonElement buffers) ||
                !TryArray(root, "skins", 1, 1, out JsonElement skins) ||
                !TryArray(root, "animations", 1, limits.MaximumAnimations, out JsonElement animations))
                return Fail(ActorPresentationArtifactError.LimitExceeded, out error);

            JsonElement buffer = buffers[0];
            if (buffer.TryGetProperty("uri", out _) ||
                !TryInt(buffer, "byteLength", 1, binaryBytes.Length, out int declaredBinary) ||
                binaryBytes.Length - declaredBinary is < 0 or > 3)
                return Fail(ActorPresentationArtifactError.UnsupportedFeature, out error);

            if (!TryValidateBufferViews(
                    views, declaredBinary, out BufferViewInfo[] viewInfo) ||
                !TryAccessorInfo(accessors, viewInfo, out AccessorInfo[] accessorInfo))
                return Fail(ActorPresentationArtifactError.InvalidReference, out error);
            if (!ValidateNodes(nodes, meshes.GetArrayLength(), skins.GetArrayLength(),
                    limits.MaximumStringCharacters) ||
                !ValidateSceneGraph(root, nodes, limits.MaximumStringCharacters))
                return Fail(ActorPresentationArtifactError.InvalidReference, out error);

            int materialCount = 0;
            if (root.TryGetProperty("materials", out JsonElement materials))
            {
                if (materials.ValueKind != JsonValueKind.Array ||
                    materials.GetArrayLength() > limits.MaximumMaterials ||
                    !ValidateNames(materials, limits.MaximumStringCharacters))
                    return Fail(ActorPresentationArtifactError.LimitExceeded, out error);
                materialCount = materials.GetArrayLength();
            }

            if (!TryValidateMeshes(meshes, accessorInfo, materialCount, limits,
                    out int primitiveCount, out int meshVertices, out int meshIndices))
                return Fail(ActorPresentationArtifactError.InvalidReference, out error);

            if (!TryValidateSkeleton(skins[0], nodes, accessorInfo,
                    limits, out string[] boneNames, out string mainHand))
                return Fail(ActorPresentationArtifactError.InvalidSkeleton, out error);

            if (!TryValidateAnimations(animations, nodes.GetArrayLength(), accessorInfo,
                    limits, out int tracks, out int keys,
                    out ActorPresentationSemanticClips clips))
                return Fail(ActorPresentationArtifactError.InvalidReference, out error);
            if ((clips & ActorPresentationSemanticClips.RequiredV1) !=
                ActorPresentationSemanticClips.RequiredV1)
                return Fail(ActorPresentationArtifactError.MissingSemanticClip, out error);

            info = new ActorPresentationArtifactInfo(
                bytes.Length, jsonBytes.Length, nodes.GetArrayLength(), meshes.GetArrayLength(),
                primitiveCount, meshVertices, meshIndices,
                accessors.GetArrayLength(), views.GetArrayLength(), materialCount,
                boneNames.Length, animations.GetArrayLength(), tracks, keys,
                ComputeSkeletonFingerprint(boneNames), clips, mainHand);
            return info.IsValid;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            OverflowException or ArgumentException)
        {
            info = default;
            return Fail(ActorPresentationArtifactError.InvalidJson, out error);
        }
    }

    private static bool TryReadChunk(
        ReadOnlySpan<byte> bytes,
        ref int cursor,
        out ReadOnlySpan<byte> payload,
        out uint type)
    {
        payload = default;
        type = 0;
        if (cursor > bytes.Length - 8) return false;
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[cursor..]);
        type = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(cursor + 4)..]);
        cursor += 8;
        if ((length & 3) != 0 || length > int.MaxValue || length > bytes.Length - cursor)
            return false;
        payload = bytes.Slice(cursor, (int)length);
        cursor += (int)length;
        return true;
    }

    private static bool ExtensionsSupported(JsonElement root, int maximumStringCharacters)
    {
        foreach (string property in new[] { "extensionsUsed", "extensionsRequired" })
        {
            if (!root.TryGetProperty(property, out JsonElement extensions)) continue;
            if (extensions.ValueKind != JsonValueKind.Array ||
                extensions.GetArrayLength() > SupportedExtensions.Length) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement value in extensions.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String ||
                    value.GetString() is not { } extension ||
                    extension.Length > maximumStringCharacters ||
                    !SupportedExtensions.Contains(extension, StringComparer.Ordinal) ||
                    !seen.Add(extension)) return false;
            }
        }
        return true;
    }

    private static bool TryValidateBufferViews(
        JsonElement views,
        int binaryBytes,
        out BufferViewInfo[] result)
    {
        result = new BufferViewInfo[views.GetArrayLength()];
        int index = 0;
        foreach (JsonElement view in views.EnumerateArray())
        {
            if (!TryInt(view, "buffer", 0, 0, out _) ||
                !TryInt(view, "byteLength", 1, binaryBytes, out int length))
                return false;
            int offset = 0;
            if (view.TryGetProperty("byteOffset", out JsonElement offsetElement) &&
                !TryInt(offsetElement, 0, binaryBytes, out offset)) return false;
            if (offset > binaryBytes - length) return false;
            int stride = 0;
            if (view.TryGetProperty("byteStride", out JsonElement strideElement) &&
                (!TryInt(strideElement, 4, 252, out stride) || (stride & 3) != 0))
                return false;
            result[index++] = new BufferViewInfo(length, stride);
        }
        return true;
    }

    private static bool TryAccessorInfo(
        JsonElement accessors,
        BufferViewInfo[] views,
        out AccessorInfo[] result)
    {
        result = new AccessorInfo[accessors.GetArrayLength()];
        int index = 0;
        foreach (JsonElement accessor in accessors.EnumerateArray())
        {
            if (accessor.TryGetProperty("sparse", out _) ||
                !TryInt(accessor, "bufferView", 0, views.Length - 1, out int viewIndex) ||
                !TryInt(accessor, "count", 1, 2_000_000, out int count) ||
                !TryInt(accessor, "componentType", 5120, 5126, out int component) ||
                component == 5124 ||
                !TryString(accessor, "type", 8, out string type) ||
                type is not ("SCALAR" or "VEC2" or "VEC3" or "VEC4" or "MAT2" or "MAT3" or "MAT4"))
                return false;
            int byteOffset = 0;
            if (accessor.TryGetProperty("byteOffset", out JsonElement offsetElement) &&
                !TryInt(offsetElement, 0, views[viewIndex].Length - 1, out byteOffset))
                return false;
            if (accessor.TryGetProperty("normalized", out JsonElement normalized) &&
                normalized.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;
            int componentBytes = ComponentBytes(component);
            int elementBytes = ElementBytes(type, componentBytes);
            if (componentBytes == 0 || elementBytes == 0 || byteOffset % componentBytes != 0)
                return false;
            int stride = views[viewIndex].Stride == 0
                ? elementBytes
                : views[viewIndex].Stride;
            if (stride < elementBytes ||
                (long)byteOffset + (long)stride * (count - 1) + elementBytes >
                    views[viewIndex].Length)
                return false;
            result[index] = new AccessorInfo(count, component, type, elementBytes);
            index++;
        }
        return true;
    }

    private static int ComponentBytes(int componentType) => componentType switch
    {
        5120 or 5121 => 1,
        5122 or 5123 => 2,
        5125 or 5126 => 4,
        _ => 0,
    };

    private static int ElementBytes(string type, int componentBytes)
    {
        int rows;
        int columns;
        switch (type)
        {
            case "SCALAR": rows = columns = 1; break;
            case "VEC2": rows = 2; columns = 1; break;
            case "VEC3": rows = 3; columns = 1; break;
            case "VEC4": rows = 4; columns = 1; break;
            case "MAT2": rows = columns = 2; break;
            case "MAT3": rows = columns = 3; break;
            case "MAT4": rows = columns = 4; break;
            default: return 0;
        }
        int columnBytes = checked(rows * componentBytes);
        if (columns > 1) columnBytes = (columnBytes + 3) & ~3;
        return checked(columnBytes * columns);
    }

    private static bool ValidateNodes(
        JsonElement nodes,
        int meshCount,
        int skinCount,
        int maximumStringCharacters)
    {
        int count = nodes.GetArrayLength();
        foreach (JsonElement node in nodes.EnumerateArray())
        {
            if (node.TryGetProperty("name", out JsonElement name) &&
                !TryString(name, maximumStringCharacters, out _)) return false;
            if (node.TryGetProperty("mesh", out JsonElement mesh) &&
                !TryInt(mesh, 0, meshCount - 1, out _)) return false;
            if (node.TryGetProperty("skin", out JsonElement skin) &&
                !TryInt(skin, 0, skinCount - 1, out _)) return false;
            if (!node.TryGetProperty("children", out JsonElement children)) continue;
            if (children.ValueKind != JsonValueKind.Array || children.GetArrayLength() > count)
                return false;
            var seen = new HashSet<int>();
            foreach (JsonElement child in children.EnumerateArray())
                if (!TryInt(child, 0, count - 1, out int value) || !seen.Add(value)) return false;
        }
        return true;
    }

    private static bool ValidateSceneGraph(
        JsonElement root,
        JsonElement nodes,
        int maximumStringCharacters)
    {
        int nodeCount = nodes.GetArrayLength();
        if (!TryArray(root, "scenes", 1, 1, out JsonElement scenes) ||
            !TryInt(root, "scene", 0, 0, out _) ||
            scenes[0].TryGetProperty("name", out JsonElement name) &&
                !TryString(name, maximumStringCharacters, out _) ||
            !scenes[0].TryGetProperty("nodes", out JsonElement sceneNodes) ||
            sceneNodes.ValueKind != JsonValueKind.Array ||
            sceneNodes.GetArrayLength() is < 1 ||
            sceneNodes.GetArrayLength() > nodeCount)
            return false;

        var parents = new int[nodeCount];
        foreach (JsonElement node in nodes.EnumerateArray())
        {
            if (!node.TryGetProperty("children", out JsonElement children)) continue;
            foreach (JsonElement child in children.EnumerateArray())
            {
                int childIndex = child.GetInt32(); // ValidateNodes admitted this exact range.
                parents[childIndex]++;
                if (parents[childIndex] > 1) return false;
            }
        }

        var roots = new bool[nodeCount];
        var queue = new int[nodeCount];
        int tail = 0;
        foreach (JsonElement sceneNode in sceneNodes.EnumerateArray())
        {
            if (!TryInt(sceneNode, 0, nodeCount - 1, out int rootIndex) ||
                roots[rootIndex] || parents[rootIndex] != 0)
                return false;
            roots[rootIndex] = true;
            queue[tail++] = rootIndex;
        }
        for (int index = 0; index < nodeCount; index++)
            if ((parents[index] == 0) != roots[index]) return false;

        int visited = 0;
        for (int head = 0; head < tail; head++)
        {
            int nodeIndex = queue[head];
            visited++;
            if (!nodes[nodeIndex].TryGetProperty("children", out JsonElement children)) continue;
            foreach (JsonElement child in children.EnumerateArray())
                queue[tail++] = child.GetInt32();
        }
        return visited == nodeCount;
    }

    private static bool ValidateNames(JsonElement values, int maximumStringCharacters)
    {
        foreach (JsonElement value in values.EnumerateArray())
            if (value.TryGetProperty("name", out JsonElement name) &&
                !TryString(name, maximumStringCharacters, out _)) return false;
        return true;
    }

    private static bool TryValidateMeshes(
        JsonElement meshes,
        AccessorInfo[] accessors,
        int materialCount,
        ActorPresentationArtifactLimits limits,
        out int primitiveCount,
        out int vertexCount,
        out int indexCount)
    {
        primitiveCount = 0;
        vertexCount = 0;
        indexCount = 0;
        foreach (JsonElement mesh in meshes.EnumerateArray())
        {
            if (!mesh.TryGetProperty("primitives", out JsonElement primitives) ||
                primitives.ValueKind != JsonValueKind.Array || primitives.GetArrayLength() == 0)
                return false;
            primitiveCount = checked(primitiveCount + primitives.GetArrayLength());
            if (primitiveCount > limits.MaximumPrimitives) return false;
            foreach (JsonElement primitive in primitives.EnumerateArray())
            {
                if (primitive.TryGetProperty("mode", out JsonElement mode) &&
                    !TryInt(mode, 4, 4, out _) ||
                    primitive.TryGetProperty("targets", out _) ||
                    !primitive.TryGetProperty("attributes", out JsonElement attributes) ||
                    attributes.ValueKind != JsonValueKind.Object ||
                    !attributes.TryGetProperty("POSITION", out JsonElement position) ||
                    !TryInt(position, 0, accessors.Length - 1, out int positionAccessor) ||
                    !TryInt(primitive, "indices", 0, accessors.Length - 1, out int indexAccessor))
                    return false;
                AccessorInfo positions = accessors[positionAccessor];
                AccessorInfo indices = accessors[indexAccessor];
                if (positions.Type != "VEC3" || positions.ComponentType != 5126 ||
                    indices.Type != "SCALAR" ||
                    indices.ComponentType is not (5121 or 5123 or 5125) ||
                    indices.Count % 3 != 0)
                    return false;
                if (primitive.TryGetProperty("material", out JsonElement material) &&
                    !TryInt(material, 0, materialCount - 1, out _)) return false;
                foreach (JsonProperty attribute in attributes.EnumerateObject())
                {
                    if (attribute.Name.Length > 32 ||
                        !TryInt(attribute.Value, 0, accessors.Length - 1,
                            out int attributeAccessor) ||
                        accessors[attributeAccessor].Count != positions.Count)
                        return false;
                }
                vertexCount = checked(vertexCount + positions.Count);
                indexCount = checked(indexCount + indices.Count);
                if (vertexCount > limits.MaximumMeshVertices ||
                    indexCount > limits.MaximumMeshIndices)
                    return false;
            }
        }
        return true;
    }

    private static bool TryValidateSkeleton(
        JsonElement skin,
        JsonElement nodes,
        AccessorInfo[] accessors,
        ActorPresentationArtifactLimits limits,
        out string[] boneNames,
        out string mainHand)
    {
        boneNames = [];
        mainHand = string.Empty;
        if (!skin.TryGetProperty("joints", out JsonElement joints) ||
            joints.ValueKind != JsonValueKind.Array || joints.GetArrayLength() is < 1 ||
            joints.GetArrayLength() > limits.MaximumBones ||
            skin.TryGetProperty("inverseBindMatrices", out JsonElement inverse) &&
                !TryInt(inverse, 0, accessors.Length - 1, out _) ||
            skin.TryGetProperty("skeleton", out JsonElement skeleton) &&
                !TryInt(skeleton, 0, nodes.GetArrayLength() - 1, out _))
            return false;

        boneNames = new string[joints.GetArrayLength()];
        var indices = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        int cursor = 0;
        foreach (JsonElement joint in joints.EnumerateArray())
        {
            if (!TryInt(joint, 0, nodes.GetArrayLength() - 1, out int nodeIndex) ||
                !indices.Add(nodeIndex) ||
                !TryString(nodes[nodeIndex], "name", limits.MaximumStringCharacters, out string name) ||
                !names.Add(name)) return false;
            boneNames[cursor++] = name;
            if (name.EndsWith("RightHand", StringComparison.OrdinalIgnoreCase)) mainHand = name;
        }
        if (skin.TryGetProperty("inverseBindMatrices", out inverse))
        {
            int accessorIndex = inverse.GetInt32();
            AccessorInfo matrices = accessors[accessorIndex];
            if (matrices.Count != boneNames.Length || matrices.ComponentType != 5126 ||
                matrices.Type != "MAT4") return false;
        }
        return mainHand.Length != 0;
    }

    private static bool TryValidateAnimations(
        JsonElement animations,
        int nodeCount,
        AccessorInfo[] accessors,
        ActorPresentationArtifactLimits limits,
        out int trackCount,
        out int keyCount,
        out ActorPresentationSemanticClips clips)
    {
        trackCount = 0;
        keyCount = 0;
        clips = ActorPresentationSemanticClips.None;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement animation in animations.EnumerateArray())
        {
            if (!TryString(animation, "name", limits.MaximumStringCharacters, out string name) ||
                !names.Add(name) ||
                !animation.TryGetProperty("samplers", out JsonElement samplers) ||
                samplers.ValueKind != JsonValueKind.Array || samplers.GetArrayLength() == 0 ||
                !animation.TryGetProperty("channels", out JsonElement channels) ||
                channels.ValueKind != JsonValueKind.Array || channels.GetArrayLength() == 0)
                return false;
            trackCount = checked(trackCount + channels.GetArrayLength());
            if (trackCount > limits.MaximumAnimationTracks) return false;

            int[] samplerInputs = new int[samplers.GetArrayLength()];
            int[] samplerOutputs = new int[samplers.GetArrayLength()];
            bool[] samplerCubic = new bool[samplers.GetArrayLength()];
            int samplerIndex = 0;
            foreach (JsonElement sampler in samplers.EnumerateArray())
            {
                if (!TryInt(sampler, "input", 0, accessors.Length - 1,
                        out samplerInputs[samplerIndex]) ||
                    !TryInt(sampler, "output", 0, accessors.Length - 1,
                        out int outputAccessor) ||
                    sampler.TryGetProperty("interpolation", out JsonElement interpolation) &&
                    (!TryString(interpolation, 16, out string mode) ||
                     mode is not ("LINEAR" or "STEP" or "CUBICSPLINE"))) return false;
                AccessorInfo input = accessors[samplerInputs[samplerIndex]];
                if (input.ComponentType != 5126 || input.Type != "SCALAR") return false;
                // Output type is validated against the target path below; retain its index.
                samplerOutputs[samplerIndex] = outputAccessor;
                samplerCubic[samplerIndex] = sampler.TryGetProperty(
                    "interpolation", out JsonElement interpolationValue) &&
                    interpolationValue.GetString() == "CUBICSPLINE";
                samplerIndex++;
            }
            foreach (JsonElement channel in channels.EnumerateArray())
            {
                if (!TryInt(channel, "sampler", 0, samplers.GetArrayLength() - 1,
                        out int usedSampler) ||
                    !channel.TryGetProperty("target", out JsonElement target) ||
                    !TryInt(target, "node", 0, nodeCount - 1, out _) ||
                    !TryString(target, "path", 16, out string path) ||
                    path is not ("translation" or "rotation" or "scale")) return false;
                AccessorInfo input = accessors[samplerInputs[usedSampler]];
                AccessorInfo output = accessors[samplerOutputs[usedSampler]];
                int outputMultiplier = samplerCubic[usedSampler] ? 3 : 1;
                if (output.ComponentType != 5126 ||
                    output.Type != (path == "rotation" ? "VEC4" : "VEC3") ||
                    output.Count != checked(input.Count * outputMultiplier))
                    return false;
                keyCount = checked(keyCount + input.Count);
                if (keyCount > limits.MaximumAnimationKeys) return false;
            }

            string leaf = SemanticLeaf(name);
            if (leaf.Equals("Idle", StringComparison.OrdinalIgnoreCase))
                clips |= ActorPresentationSemanticClips.Idle;
            else if (leaf.Equals("Walk", StringComparison.OrdinalIgnoreCase))
                clips |= ActorPresentationSemanticClips.Walk;
            else if (leaf.Equals("Jump", StringComparison.OrdinalIgnoreCase))
                clips |= ActorPresentationSemanticClips.Jump;
        }
        return true;
    }

    private static string SemanticLeaf(string value)
    {
        int separator = Math.Max(value.LastIndexOf('/'), value.LastIndexOf(':'));
        string leaf = separator >= 0 ? value[(separator + 1)..] : value;
        return leaf.EndsWith("-loop", StringComparison.OrdinalIgnoreCase) ? leaf[..^5] : leaf;
    }

    private static ContentHash ComputeSkeletonFingerprint(IEnumerable<string> boneNames)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, SkeletonDomain);
        foreach (string bone in boneNames) Append(hash, bone);
        Span<byte> digest = stackalloc byte[ContentHash.ByteLength];
        if (!hash.TryGetHashAndReset(digest, out int written) || written != digest.Length)
            throw new InvalidOperationException("The skeleton fingerprint was incomplete.");
        return ContentHash.FromDigest(digest);
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool TryArray(
        JsonElement owner,
        string property,
        int minimum,
        int maximum,
        out JsonElement value)
    {
        if (!owner.TryGetProperty(property, out value) || value.ValueKind != JsonValueKind.Array)
            return false;
        int count = value.GetArrayLength();
        return count >= minimum && count <= maximum;
    }

    private static bool TryInt(
        JsonElement owner,
        string property,
        int minimum,
        int maximum,
        out int value)
    {
        value = 0;
        return owner.TryGetProperty(property, out JsonElement element) &&
            TryInt(element, minimum, maximum, out value);
    }

    private static bool TryInt(
        JsonElement element,
        int minimum,
        int maximum,
        out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value) &&
            value >= minimum && value <= maximum;
    }

    private static bool TryString(
        JsonElement owner,
        string property,
        int maximumCharacters,
        out string value)
    {
        value = string.Empty;
        return owner.TryGetProperty(property, out JsonElement element) &&
            TryString(element, maximumCharacters, out value);
    }

    private static bool TryString(
        JsonElement element,
        int maximumCharacters,
        out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } text ||
            text.Length == 0 || text.Length > maximumCharacters) return false;
        foreach (char character in text)
            if (char.IsControl(character)) return false;
        value = text;
        return true;
    }

    private static bool Fail(
        ActorPresentationArtifactError value,
        out ActorPresentationArtifactError error)
    {
        error = value;
        return false;
    }

    private readonly record struct BufferViewInfo(int Length, int Stride);

    private readonly record struct AccessorInfo(
        int Count,
        int ComponentType,
        string Type,
        int ElementBytes);
}
