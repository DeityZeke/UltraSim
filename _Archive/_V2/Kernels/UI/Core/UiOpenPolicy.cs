namespace UltraSim.UI;

public enum UiOpenPolicy : byte
{
    New = 0,
    ReplaceCurrent = 1,
    FocusExisting = 2,
    SingletonByKey = 3,
}
