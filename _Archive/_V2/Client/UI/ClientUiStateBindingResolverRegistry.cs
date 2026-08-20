using UltraSim.UI;

namespace UltraSim.Client.UI;

public sealed class ClientUiStateBindingResolverRegistry
{
    private readonly Dictionary<string, IClientUiStateBindingResolver> _resolversBySourceId = new(StringComparer.Ordinal);

    public int Count => _resolversBySourceId.Count;

    public bool TryRegister(IClientUiStateBindingResolver resolver, out string error)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        UiValidationResult sourceResult = UiValidation.ValidateStableId(resolver.SourceId, nameof(resolver.SourceId), 96);
        if (!sourceResult.IsValid)
        {
            error = sourceResult.Error;
            return false;
        }

        if (!_resolversBySourceId.TryAdd(resolver.SourceId, resolver))
        {
            error = $"A UI state binding resolver for source '{resolver.SourceId}' is already registered.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryGet(string sourceId, out IClientUiStateBindingResolver resolver)
    {
        return _resolversBySourceId.TryGetValue(sourceId, out resolver!);
    }

    public bool TryResolve(
        UiStateBindingDescriptor binding,
        out UiModelSlotValue value,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (!_resolversBySourceId.TryGetValue(binding.SourceId, out IClientUiStateBindingResolver? resolver))
        {
            value = UiModelSlotValue.Null(binding.SlotId);
            error = $"No UI state binding resolver is registered for source '{binding.SourceId}'.";
            return false;
        }

        return resolver.TryResolve(binding, out value, out error);
    }
}
