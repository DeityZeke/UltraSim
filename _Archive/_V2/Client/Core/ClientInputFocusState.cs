namespace UltraSim.Client;

public readonly record struct ClientInputFocusState(
    bool TextInputFocused,
    bool MenuOpen,
    bool ModalInputCaptured,
    bool ExplicitGameplayBlock)
{
    public bool BlocksGameplayMovement =>
        TextInputFocused ||
        MenuOpen ||
        ModalInputCaptured ||
        ExplicitGameplayBlock;
}
