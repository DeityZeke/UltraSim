namespace UltraSim.UI.Schema
{
    /// <summary>
    /// Wire type codes for dynamic payload slots.
    /// Values are stable — do not renumber.
    /// </summary>
    public enum UiSlotType : byte
    {
        String  = 0,
        Int32   = 1,
        Float32 = 2,
        Bool    = 3,
    }

    /// <summary>
    /// Describes one dynamic slot in the panel's payload manifest.
    /// The manifest is ordered; slot index equals position in the list.
    /// </summary>
    public readonly record struct UiSlotDescriptor(byte SlotIndex, UiSlotType SlotType);
}
