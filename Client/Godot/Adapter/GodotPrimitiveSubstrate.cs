using Godot;
using UltraSim.Client.Core;

namespace UltraSim.Client.Godot;

/// <summary>
/// Permanent renderer-owned primitive realization. No package, catalog, or optional model is
/// needed to create these finite meshes/materials; callers must already hold disclosed truth.
/// </summary>
internal static class GodotPrimitiveSubstrate
{
    internal static StandardMaterial3D CreateNeutralMaterial() => new()
    {
        AlbedoColor = new Color(0.48f, 0.5f, 0.52f),
        Roughness = 0.95f,
    };

    internal static ShaderMaterial CreateMissingTextureMaterial(out Shader shader)
    {
        shader = new Shader
        {
            Code = "shader_type spatial;\n" +
                "render_mode diffuse_lambert;\n" +
                "void fragment() {\n" +
                "  vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;\n" +
                "  vec3 p = floor(world * 5.0);\n" +
                "  float c = mod(p.x + p.y + p.z, 2.0);\n" +
                "  ALBEDO = mix(vec3(1.0, 0.0, 0.85), vec3(1.0, 0.95, 0.0), c);\n" +
                "  ROUGHNESS = 0.8;\n" +
                "}\n",
        };
        return new ShaderMaterial { Shader = shader };
    }

    internal static Node3D CreateActor(ClientActorVisualKind kind, GodotOwnedResources resources)
    {
        ClientPrimitivePresentation descriptor = ClientPrimitivePresentationCatalog.ForActor(kind);
        Vector3 size = new(descriptor.WidthMillimeters * 0.001f,
            descriptor.HeightMillimeters * 0.001f,
            descriptor.DepthMillimeters * 0.001f);
        Color color = kind switch
        {
            ClientActorVisualKind.HumanX => new Color(0.25f, 0.72f, 0.92f),
            ClientActorVisualKind.HumanY => new Color(0.86f, 0.52f, 0.24f),
            _ => new Color(0.48f, 0.35f, 0.23f),
        };
        var material = resources.Own(new StandardMaterial3D { AlbedoColor = color, Roughness = 0.9f });
        if (descriptor.Shape == ClientPrimitiveShape.Capsule)
        {
            float radius = MathF.Min(size.X, size.Z) * 0.5f;
            return new MeshInstance3D
            {
                Mesh = resources.Own(new CapsuleMesh { Radius = radius, Height = size.Y, Material = material }),
                Position = new Vector3(0, size.Y * 0.5f, 0),
            };
        }
        return new MeshInstance3D
        {
            Mesh = resources.Own(GodotMeshGeometry.CreateGroundedBox(size, material)),
        };
    }

    /// <summary>One finite icon for supported image-role UI when optional art is absent.</summary>
    internal static Texture2D CreateMissingIcon()
    {
        using Image image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
            image.SetPixel(x, y, (x / 4 + y / 4) % 2 == 0
                ? new Color(0.82f, 0.04f, 0.62f)
                : new Color(0.02f, 0.02f, 0.02f));
        Texture2D texture = ImageTexture.CreateFromImage(image);
        return texture;
    }

    /// <summary>Neutral bounded silhouette for a valid item lacking optional portrait art.</summary>
    internal static Texture2D CreateInventoryPrimitiveIcon()
    {
        using Image image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        image.Fill(new Color(0.12f, 0.17f, 0.22f));
        for (int y = 7; y < 25; y++)
        for (int x = 7; x < 25; x++)
            image.SetPixel(x, y, x is 7 or 24 || y is 7 or 24
                ? new Color(0.69f, 0.74f, 0.77f) : new Color(0.36f, 0.43f, 0.49f));
        return ImageTexture.CreateFromImage(image);
    }
}
