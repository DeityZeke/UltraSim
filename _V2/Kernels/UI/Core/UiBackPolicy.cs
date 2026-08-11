namespace UltraSim.UI;

public enum UiBackPolicy : byte
{
    None = 0,
    SendAction = 1,
    CloseAndFocusParent = 2,
    ReplaceWithParentState = 3,
}
