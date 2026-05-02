using UltraSim.UI;

namespace UltraSim.Client.UI;

public sealed record ClientUiRepeaterRowState(
    string RepeaterSlotId,
    string RowKey,
    IReadOnlyList<UiModelSlotValue> Values);
