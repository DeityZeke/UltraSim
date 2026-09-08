using System.Reflection;
using UltraSim.UI;

namespace UltraSim.Client.StandardUi;

/// <summary>
/// Small explicit library of passive stock UI definitions selected by client composition.
/// Definitions are canonical <c>.usui</c> resources; this assembly contains no Game model,
/// handler, Inventory authority, script, or executable UI payload.
/// </summary>
public static class ClientStandardUiLibrary
{
    private const string InventoryResource =
        "UltraSim.Client.StandardUi.Assets.game.inventory.gump.v7.usui";
    private static readonly Lazy<CompiledUiDefinition> Inventory =
        new(() => ReadCanonical(InventoryResource), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Returns a stock definition only when its complete typed identity matches.</summary>
    public static CompiledUiDefinition? Resolve(UiDefinitionCacheKey cacheKey)
    {
        CompiledUiDefinition inventory = Inventory.Value;
        return cacheKey.DefinitionKey == inventory.Key &&
            cacheKey.DefinitionVersion == inventory.Version &&
            cacheKey.DefinitionHash == inventory.Hash
                ? inventory
                : null;
    }

    /// <summary>The exact passive Inventory definition selected for the stock library.</summary>
    public static CompiledUiDefinition InventoryDefinition => Inventory.Value;

    private static CompiledUiDefinition ReadCanonical(string resourceName)
    {
        Assembly assembly = typeof(ClientStandardUiLibrary).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidDataException($"The standard UI resource '{resourceName}' is absent.");
        int maximum = UiDefinitionArtifactLimits.Default.MaximumArtifactBytes;
        if (stream.Length is <= 0 or > int.MaxValue || stream.Length > maximum)
            throw new InvalidDataException($"The standard UI resource '{resourceName}' exceeds its bound.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1 ||
            !UiDefinitionArtifactCodec.TryReadCanonical(bytes, out CompiledUiDefinition? definition) ||
            definition is null)
        {
            throw new InvalidDataException($"The standard UI resource '{resourceName}' is not canonical.");
        }
        return definition;
    }
}
