namespace UltraSim.UI;

/// <summary>Bounds for one passive canonical compiled-definition artifact.</summary>
public readonly record struct UiDefinitionArtifactLimits(
    int MaximumArtifactBytes,
    UiCompileLimits MaximumDefinitionLimits)
{
    public static UiDefinitionArtifactLimits Default => new(
        MaximumArtifactBytes: 1 * 1024 * 1024,
        MaximumDefinitionLimits: UiCompileLimits.Default);

    internal void Validate()
    {
        if (MaximumArtifactBytes is < 64 or > 16 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaximumArtifactBytes));
        new UiSemanticWireLimits(MaximumArtifactBytes, MaximumDefinitionLimits).Validate();
    }
}

/// <summary>
/// Canonical engine-neutral passive artifact for an already-compiled UI definition. It carries
/// layout/schema identity only: no model values, action requests, scripts, or executable code.
/// </summary>
public static class UiDefinitionArtifactCodec
{
    public static bool TryWriteCanonical(
        Span<byte> destination,
        CompiledUiDefinition definition,
        out int written,
        UiDefinitionArtifactLimits limits = default) =>
        UiSemanticWireCodec.TryWriteDefinitionArtifactCore(
            destination, definition, out written, Resolve(limits));

    public static bool TryEncodeCanonical(
        CompiledUiDefinition definition,
        out byte[] bytes,
        UiDefinitionArtifactLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        limits = Resolve(limits);
        bytes = new byte[limits.MaximumArtifactBytes];
        if (!TryWriteCanonical(bytes, definition, out int written, limits))
        {
            bytes = [];
            return false;
        }
        Array.Resize(ref bytes, written);
        return true;
    }

    public static bool TryReadCanonical(
        ReadOnlySpan<byte> source,
        out CompiledUiDefinition? definition,
        UiDefinitionArtifactLimits limits = default) =>
        UiSemanticWireCodec.TryReadDefinitionArtifactCore(
            source, out definition, Resolve(limits));

    private static UiDefinitionArtifactLimits Resolve(UiDefinitionArtifactLimits limits)
    {
        if (limits.MaximumArtifactBytes == 0) limits = UiDefinitionArtifactLimits.Default;
        limits.Validate();
        return limits;
    }
}
