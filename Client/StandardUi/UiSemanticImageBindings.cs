using UltraSim.Framework.Definitions;
using UltraSim.UI;

namespace UltraSim.Client.StandardUi;

/// <summary>
/// Bounded, presentation-only attachment of already admitted public Definition references to
/// image elements. The caller owns admission; this type never grants content authorization.
/// The exact view instance and compiled definition handle prevent another view or stale layout
/// from reusing element IDs and displaying a different semantic subject's image.
/// </summary>
public sealed class UiSemanticImageBindings
{
    private readonly Dictionary<UiElementId, SemanticPresentationReference> _bindings;

    public UiSemanticImageBindings(UiViewInstanceId instance, CompiledUiDefinition definition,
        IEnumerable<(UiElementId Element, SemanticPresentationReference Reference)> bindings)
    {
        if (!instance.IsValid) throw new ArgumentException("A live UI instance is required.", nameof(instance));
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(bindings);
        Instance = instance;
        Definition = definition.Handle;
        var images = definition.Elements.ToArray().Where(element => element.Kind == UiElementKind.Image)
            .Select(element => element.Id).ToHashSet();
        _bindings = new Dictionary<UiElementId, SemanticPresentationReference>();
        foreach (var (element, reference) in bindings)
        {
            if (_bindings.Count == 64 || !images.Contains(element) || !reference.IsValid ||
                !_bindings.TryAdd(element, reference))
                throw new ArgumentException("Invalid or unbounded semantic image binding.", nameof(bindings));
        }
    }

    public UiViewInstanceId Instance { get; }
    public UiDefinitionHandle Definition { get; }

    public bool TryGet(UiViewInstanceId instance, CompiledUiDefinition definition, UiElementId element,
        out SemanticPresentationReference reference)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (instance == Instance && definition.Handle == Definition &&
            _bindings.TryGetValue(element, out reference))
            return true;
        reference = default;
        return false;
    }
}
