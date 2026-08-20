using System.Collections.Generic;

namespace UltraSim.UI.Schema
{
    public readonly record struct UiRepeaterItemData(ulong RowKey, object?[] Values);

    public sealed class UiRepeaterPayloadData
    {
        public UiRepeaterPayloadData(byte repeaterId, IReadOnlyList<UiRepeaterItemData> items)
        {
            RepeaterId = repeaterId;
            Items = items;
        }

        public byte RepeaterId { get; }
        public IReadOnlyList<UiRepeaterItemData> Items { get; }
    }
}
