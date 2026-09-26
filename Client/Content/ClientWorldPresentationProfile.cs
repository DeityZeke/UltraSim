namespace UltraSim.Client.Content;

/// <summary>Logical world palette identities, not art bytes or semantic activation.</summary>
public static class ClientWorldPresentationProfile
{
    public static LogicalContentId WallTexture { get; } = Id("cabin:texture/wall");
    public static LogicalContentId WoodTexture { get; } = Id("cabin:texture/wood");
    public static LogicalContentId WallMaterial { get; } = Id("cabin:material/wall");
    public static LogicalContentId WoodMaterial { get; } = Id("cabin:material/wood");
    public static LogicalContentId BlueGlowMaterial { get; } = Id("test-cabin:material/blue-glow");
    public static ContentScopeActivationId LocalDefault { get; } = new(
        ContentScopeKey.Local, new ContentActivationId(0x4631_362D_4341_4249UL, 0x4E2D_4445_4641_554CUL));

    private static LogicalContentId Id(string value) =>
        LogicalContentId.TryCreate(value, out var id) ? id : throw new InvalidOperationException(value);
}

/// <summary>Owner-thread resource resolution from the current immutable presentation generation.</summary>
public interface IWorldPresentationGenerationSource
{
    PresentationSourceKind GetSource(LogicalContentId id);
    bool TryResolveMaterialPresentation(LogicalContentId id,
        out ResolvedMaterialPresentation? presentation, out PresentationResourceResolutionError error);
}
