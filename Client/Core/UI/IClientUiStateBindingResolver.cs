using UltraSim.UI;

namespace UltraSim.Client.UI;

public interface IClientUiStateBindingResolver
{
    string SourceId { get; }

    bool TryResolve(
        UiStateBindingDescriptor binding,
        out UiModelSlotValue value,
        out string error);
}
