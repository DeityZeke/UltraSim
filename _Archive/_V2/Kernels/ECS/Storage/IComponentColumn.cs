namespace UltraSim.ECS;

internal interface IComponentColumn
{
    int Capacity { get; }
    void EnsureCapacity(int capacity);
    void SetDefaultAt(int slot);
    void SwapLastIntoSlot(int slot, int last);
    void CopySlot(int source, int destination);
    void CopySlotRange(int source, int destinationStart, int count);
    void CopyFrom(IComponentColumn source, int sourceSlot, int destinationSlot);
}
